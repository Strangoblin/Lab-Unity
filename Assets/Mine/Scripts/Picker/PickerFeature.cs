using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace Mine.Picker
{
    // ════════════════════════════════════════════════════════════
    //  PickerFeature — GPU Picker + Outline 统一入口
    // ════════════════════════════════════════════════════════════

    public class PickerFeature : ScriptableRendererFeature
    {
        public static PickerPass   Picker  { get; private set; }
        public static OutlinePass Outline { get; private set; }

        // ── 面板参数 ────────────────────────────────────────────

        [SerializeField] private Shader _pickerShader;
        [SerializeField] private Shader _outlineMaskShader;
        [SerializeField] private Shader _outlineCompositeShader;
        [SerializeField] private PickerPass.DebugView _debugView = PickerPass.DebugView.Off;
        [SerializeField] private bool _debugShowMask = false;

        private PickerPass  _pickerPass;
        private OutlinePass _outlinePass;

        // ════════════════════════════════════════════════════════
        //  Create — 实例化 Pass，注册全局静态引用
        // ════════════════════════════════════════════════════════

        public override void Create()
        {
            _pickerPass = new PickerPass(_pickerShader)
                { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            _pickerPass.debugView = _debugView;
            Picker = _pickerPass;

            _outlinePass = new OutlinePass(_outlineMaskShader, _outlineCompositeShader)
                { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            _outlinePass.debugShowMask = _debugShowMask;
            Outline = _outlinePass;
        }

        // ════════════════════════════════════════════════════════
        //  AddRenderPasses
        // ════════════════════════════════════════════════════════

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game
                && renderingData.cameraData.cameraType != CameraType.SceneView) return;

            if (_pickerPass != null)
            {
                _pickerPass.debugView = _debugView;
                renderer.EnqueuePass(_pickerPass);
            }
            if (_outlinePass != null)
            {
                _outlinePass.debugShowMask = _debugShowMask;
                renderer.EnqueuePass(_outlinePass);
            }
        }

        // ════════════════════════════════════════════════════════
        //  Dispose
        // ════════════════════════════════════════════════════════

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pickerPass?.Dispose();  _pickerPass  = null;
                _outlinePass?.Dispose(); _outlinePass = null;
                Picker  = null;
                Outline = null;
            }
            base.Dispose(disposing);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  PickerPass — MRT 绘制 ObjectID / Depth / Normal
    // ════════════════════════════════════════════════════════════

    public class PickerPass : ScriptableRenderPass
    {
        public enum DebugView { Off, ObjectID, Depth, Normal }

        public DebugView debugView { get; set; } = DebugView.Off;

        private readonly Shader _pickerShader;
        private RenderTexture _objIDRT;
        private int           _objIDWidth, _objIDHeight;
        private Renderer[]    _cachedRenderers;
        private int           _cacheFrame = -1;

        public RenderTexture ObjIDRenderTexture => _objIDRT;
        public TextureHandle ObjIDHandle   { get; private set; }
        public TextureHandle DepthHandle   { get; private set; }
        public TextureHandle NormalHandle  { get; private set; }

        // ════════════════════════════════════════════════════════

        public PickerPass(Shader pickerShader = null)
        {
            _pickerShader = pickerShader;
            profilingSampler = new ProfilingSampler("Picker/Picker MRT");
        }

        // ════════════════════════════════════════════════════════
        //  RecordRenderGraph — MRT 绘制 + 可选 Debug 输出
        // ════════════════════════════════════════════════════════

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData   = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var camDesc = cameraData.cameraTargetDescriptor;
            camDesc.depthBufferBits = 0;
            camDesc.msaaSamples     = 1;

            EnsureObjIDRT(camDesc.width, camDesc.height);
            var objIDHandle = renderGraph.ImportTexture(RTHandles.Alloc(_objIDRT));
            DepthHandle  = UniversalRenderer.CreateRenderGraphTexture(renderGraph, camDesc, "_PickerDepth",  true);
            NormalHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, camDesc, "_PickerNormal", true);
            ObjIDHandle  = objIDHandle;

            var renderers = GetPickerRenderers();

            using (var builder = renderGraph.AddUnsafePass<DrawPassData>("Picker/Picker MRT", out var d))
            {
                d.renderers = renderers;
                builder.SetRenderAttachment(objIDHandle,   0, AccessFlags.Write);
                builder.SetRenderAttachment(DepthHandle,   1, AccessFlags.Write);
                builder.SetRenderAttachment(NormalHandle,  2, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DrawPassData dp, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    foreach (var r in dp.renderers)
                        if (r.sharedMaterial != null) cmd.DrawRenderer(r, r.sharedMaterial, 0, 0);
                });
            }

            if (debugView != DebugView.Off)
            {
                var debugTex = debugView switch
                {
                    DebugView.Depth  => DepthHandle,
                    DebugView.Normal => NormalHandle,
                    _                => objIDHandle,
                };
                using (var builder = renderGraph.AddUnsafePass<DebugBlitData>("Picker/Debug", out var db))
                {
                    db.source = debugTex;
                    db.dest   = resourceData.activeColorTexture;
                    builder.UseTexture(debugTex, AccessFlags.Read);
                    builder.UseTexture(db.dest, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (DebugBlitData bd, UnsafeGraphContext ctx) =>
                        Blitter.BlitCameraTexture(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd), bd.source, bd.dest));
                }
            }
        }

        // ════════════════════════════════════════════════════════
        //  Renderer 缓存 — 避免每帧 FindObjectsByType
        // ════════════════════════════════════════════════════════

        private Renderer[] GetPickerRenderers()
        {
            var frame = Time.frameCount;
            if (_cachedRenderers != null && frame - _cacheFrame <= 300)
            {
                foreach (var r in _cachedRenderers)
                    if (r == null) goto rebuild;
                return _cachedRenderers;
            }
        rebuild:
            var shader = _pickerShader != null ? _pickerShader : Shader.Find("Picker/Picker");
            if (shader == null) return System.Array.Empty<Renderer>();

            var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var list = new List<Renderer>();
            foreach (var r in all)
                if (r.sharedMaterial != null && r.sharedMaterial.shader == shader)
                    list.Add(r);

            _cachedRenderers = list.ToArray();
            _cacheFrame = frame;
            return _cachedRenderers;
        }

        // ════════════════════════════════════════════════════════
        //  持久化 RT — 尺寸变化时自动重建
        // ════════════════════════════════════════════════════════

        private void EnsureObjIDRT(int w, int h)
        {
            if (_objIDRT != null && _objIDWidth == w && _objIDHeight == h) return;
            if (_objIDRT != null) { _objIDRT.Release(); Object.DestroyImmediate(_objIDRT); }
            _objIDWidth = w; _objIDHeight = h;
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { sRGB = false };
            _objIDRT = new RenderTexture(desc) { name = "_PickerObjID", filterMode = FilterMode.Point };
            _objIDRT.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = _objIDRT;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }

        public void Dispose()
        {
            if (_objIDRT != null) { _objIDRT.Release(); Object.DestroyImmediate(_objIDRT); _objIDRT = null; }
            _cachedRenderers = null;
        }

        class DrawPassData  { public Renderer[] renderers; }
        class DebugBlitData { public TextureHandle source, dest; }
    }

    // ════════════════════════════════════════════════════════════
    //  OutlinePass — Mask 绘制 + 四邻采样描边合成
    // ════════════════════════════════════════════════════════════

    public class OutlinePass : ScriptableRenderPass
    {
        private readonly Material _maskMaterial;
        private readonly Material _compositeMaterial;

        public int  selectedObjectID { get; set; }
        public bool debugShowMask   { get; set; }
        public TextureHandle MaskHandle { get; private set; }

        // ════════════════════════════════════════════════════════

        public OutlinePass(Shader maskShader, Shader compositeShader)
        {
            profilingSampler = new ProfilingSampler("Picker/Outline Mask");
            if (maskShader      != null) _maskMaterial      = CoreUtils.CreateEngineMaterial(maskShader);
            if (compositeShader != null) _compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
        }

        // ════════════════════════════════════════════════════════
        //  RecordRenderGraph — Mask 绘制 → 描边合成
        // ════════════════════════════════════════════════════════

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_maskMaterial == null || _compositeMaterial == null) return;
            if (selectedObjectID <= 0) return;

            var cameraData   = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var camDesc = cameraData.cameraTargetDescriptor;
            camDesc.depthBufferBits = 0;
            camDesc.msaaSamples     = 1;

            var targetRenderer = FindRendererByID(selectedObjectID);
            if (targetRenderer == null) return;

            var maskDesc = new RenderTextureDescriptor(camDesc.width, camDesc.height, RenderTextureFormat.R8, 0);
            MaskHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, maskDesc, "_OutlineMask", true);

            // ── Mask 绘制 ───────────────────────────────────────

            using (var builder = renderGraph.AddUnsafePass<MaskPassData>("Picker/Outline Mask", out var md))
            {
                md.renderer     = targetRenderer;
                md.maskMaterial = _maskMaterial;
                builder.SetRenderAttachment(MaskHandle, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MaskPassData d, UnsafeGraphContext ctx) =>
                    CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd).DrawRenderer(d.renderer, d.maskMaterial, 0, 0));
            }

            // ── 描边合成 ────────────────────────────────────────

            var cameraColorHandle = resourceData.activeColorTexture;
            var texelSize = new Vector2(1f / camDesc.width, 1f / camDesc.height);
            using (var builder = renderGraph.AddUnsafePass<CompositePassData>("Picker/Outline Composite", out var cd))
            {
                cd.maskHandle        = MaskHandle;
                cd.cameraColorHandle = cameraColorHandle;
                cd.material          = _compositeMaterial;
                cd.debugShowMask     = debugShowMask;
                cd.texelSize         = texelSize;
                builder.UseTexture(MaskHandle,        AccessFlags.Read);
                builder.UseTexture(cameraColorHandle, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CompositePassData d, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    if (d.debugShowMask) { Blitter.BlitCameraTexture(cmd, d.maskHandle, d.cameraColorHandle); }
                    else
                    {
                        cmd.SetGlobalTexture("_OutlineMaskTex", d.maskHandle);
                        d.material.SetVector("_OutlineMaskTex_TexelSize",
                            new Vector4(d.texelSize.x, d.texelSize.y, 0, 0));
                        Blitter.BlitCameraTexture(cmd, d.cameraColorHandle, d.cameraColorHandle, d.material, 0);
                    }
                });
            }
        }

        // ════════════════════════════════════════════════════════
        //  按 ObjectID 查找 Renderer
        // ════════════════════════════════════════════════════════

        private static Renderer FindRendererByID(int id)
        {
            var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var r in all)
                if (r.sharedMaterial != null
                    && r.sharedMaterial.HasProperty("_ObjectID")
                    && r.sharedMaterial.GetInt("_ObjectID") == id)
                    return r;
            return null;
        }

        public void Dispose()
        {
            if (_maskMaterial != null)      Object.DestroyImmediate(_maskMaterial);
            if (_compositeMaterial != null) Object.DestroyImmediate(_compositeMaterial);
        }

        class MaskPassData      { public Renderer renderer; public Material maskMaterial; }
        class CompositePassData { public TextureHandle maskHandle, cameraColorHandle; public Material material; public bool debugShowMask; public Vector2 texelSize; }
    }
}
