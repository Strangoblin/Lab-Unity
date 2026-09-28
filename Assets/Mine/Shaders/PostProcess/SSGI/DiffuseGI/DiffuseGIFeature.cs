// ════════════════════════════════════════════════════════════
//  DiffuseGIFeature — 屏幕空间间接漫反射与保边重建
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class DiffuseGIFeature : ScriptableRendererFeature
{
    public enum DebugMode { Off, Trace, Indirect, Confidence }

    [System.Serializable]
    public class Settings
    {
        [Header("Technical · Resources")]
        public Shader shader;

        [Header("Technical · Geometry")]
        [Range(0.1f, 50f)] public float maxDistance = 5f;
        [Range(0.001f, 1f)] public float thickness = 0.15f;
        [Range(0.001f, 0.5f)] public float normalBias = 0.03f;
        [Range(0.01f, 2f)] public float depthSigma = 0.15f;

        internal static readonly int TraceParamsID = Shader.PropertyToID("_GITraceParams");
        internal static readonly int FilterParamsID = Shader.PropertyToID("_GIFilterParams");
        internal static readonly int SourceSizeID = Shader.PropertyToID("_GISourceSize");
        internal static readonly int IntensityID = Shader.PropertyToID("_GIIntensity");
        internal static readonly int ReceiverAlbedoID = Shader.PropertyToID("_GIReceiverAlbedo");
        internal static readonly int RayCountID = Shader.PropertyToID("_GIRayCount");
        internal static readonly int StepCountID = Shader.PropertyToID("_GIStepCount");
        internal static readonly int FrameIndexID = Shader.PropertyToID("_DiffuseGIFrameIndex");
        internal static readonly int DebugModeID = Shader.PropertyToID("_GIDebugMode");
        internal static readonly int TextureID = Shader.PropertyToID("_GITexture");
        internal static readonly int TraceTextureID = Shader.PropertyToID("_GITraceTexture");
    }

    [System.Serializable]
    public sealed class Controls : SSGIStandaloneControls
    {
        [Header("Artistic")]
        [Range(0f, 4f)] public float intensity = 1f;
        [Range(0f, 4f)] public float distanceFalloff;
        [ColorUsage(false, false)]
        public Color receiverAlbedo = new(0.8f, 0.8f, 0.8f, 1f);

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;
    }

    // ════════════════════════════════════════════════════════════
    //  性能档位 — 分辨率、射线和步进预算同源展开
    // ════════════════════════════════════════════════════════════
    internal static Vector3Int GetTier(SSGIQuality performance)
    {
        switch (performance)
        {
            case SSGIQuality.Low: return new Vector3Int(4, 4, 24);
            case SSGIQuality.High: return new Vector3Int(2, 8, 64);
            default: return new Vector3Int(4, 6, 48);
        }
    }

    internal class DiffuseGIPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly Settings _settings;
        private readonly Controls _controls;
        private readonly SSGITemporalFilter _temporal;

        class PassData
        {
            public Material material;
            public TextureHandle source;
            public int shaderPass;
            public Vector4 traceParams;
            public Vector4 filterParams;
            public Vector4 sourceSize;
            public Color receiverAlbedo;
            public float intensity;
            public int rayCount;
            public int stepCount;
            public int debugMode;
            public int frameIndex;
            public TextureHandle signal;
        }

        public DiffuseGIPass(Material material, Settings settings, Controls controls = null)
        {
            _material = material;
            _settings = settings;
            _controls = controls;
            _temporal = controls != null ? new SSGITemporalFilter(controls.temporalShader) : null;
            // 事件选择：采集输入只允许"不透明 + 天空盒"，且合成必须落在 URP 拷贝
            // _CameraOpaqueTexture（AfterRenderingSkybox 处的颜色拷贝）之前。
            // 同事件下自定义 Feature 先于 URP 内置 Pass 执行（URP 17 RenderGraph：
            // RecordCustomRenderGraphPasses(AfterRenderingSkybox) 在 CopyColorPass 之前），
            // 因此 AfterRenderingSkybox 同时满足"天空盒已绘制"与"早于颜色拷贝"。
            // 旧值 BeforeRenderingTransparents 会让透明物体（水/雨）用未叠加 GI 的旧拷贝
            // 做折射与混合，从而把 GI 结果覆盖掉。
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        // ════════════════════════════════════════════════════════════
        //  RenderGraph — Trace → Bilateral H/V → Resolve → Composite
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            _temporal.BeginFrame(frameData);
            Record(renderGraph, frameData, TextureHandle.nullHandle, false, _temporal.FrameIndex,
                _controls.performance, _controls.intensity, _controls.distanceFalloff,
                _controls.receiverAlbedo, _controls.debug);
            _temporal.CompleteFrame(renderGraph, frameData);
        }

        public TextureHandle RecordIntegrated(RenderGraph renderGraph, ContextContainer frameData,
            TextureHandle source, int frameIndex, SSGIQuality performance)
        {
            return Record(renderGraph, frameData, source, true, frameIndex, performance,
                1f, 0f, Color.white, DebugMode.Off);
        }

        private TextureHandle Record(RenderGraph renderGraph, ContextContainer frameData,
            TextureHandle inputSource, bool integrated, int frameIndex, SSGIQuality performance,
            float intensity, float distanceFalloff, Color receiverAlbedo, DebugMode debug)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()
                || !resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                return TextureHandle.nullHandle;

            Vector3Int tier = GetTier(performance);
            RenderTextureDescriptor fullDesc = camera.cameraTargetDescriptor;
            fullDesc.depthBufferBits = 0;
            fullDesc.msaaSamples = 1;
            fullDesc.bindMS = false;
            fullDesc.useMipMap = false;
            fullDesc.autoGenerateMips = false;
            fullDesc.enableRandomWrite = false;

            RenderTextureDescriptor giDesc = fullDesc;
            giDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            TextureHandle resolved = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Resolved", false);
            giDesc.width = Mathf.Max(1, (fullDesc.width + tier.x - 1) / tier.x);
            giDesc.height = Mathf.Max(1, (fullDesc.height + tier.x - 1) / tier.x);
            TextureHandle trace = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Trace", false);
            TextureHandle blur = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Blur", false);
            TextureHandle filtered = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Filtered", false);
            Vector4 size = new Vector4(1f / giDesc.width, 1f / giDesc.height, giDesc.width, giDesc.height);
            TextureHandle source = integrated ? inputSource : resources.activeColorTexture;

            AddPass(renderGraph, resources, "DiffuseGI.Trace", source, trace, 0, tier, size, frameIndex, TextureHandle.nullHandle,
                intensity, distanceFalloff, receiverAlbedo, debug);
            AddPass(renderGraph, resources, "DiffuseGI.BlurHorizontal", trace, blur, 1, tier, size, frameIndex, TextureHandle.nullHandle,
                intensity, distanceFalloff, receiverAlbedo, debug);
            AddPass(renderGraph, resources, "DiffuseGI.BlurVertical", blur, filtered, 2, tier, size, frameIndex, TextureHandle.nullHandle,
                intensity, distanceFalloff, receiverAlbedo, debug);
            AddPass(renderGraph, resources, "DiffuseGI.Resolve", filtered, resolved, 3, tier, size, frameIndex, TextureHandle.nullHandle,
                intensity, distanceFalloff, receiverAlbedo, debug);
            if (integrated)
                return resolved;

            TextureHandle temporal = _temporal.Resolve(renderGraph, frameData,
                resolved, SSGITemporalFilter.Signal.DiffuseGI, _controls.temporalBlend);
            var compositeDesc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            compositeDesc.name = "DiffuseGI.Composite";
            compositeDesc.clearBuffer = false;
            TextureHandle composite = renderGraph.CreateTexture(compositeDesc);
            AddPass(renderGraph, resources, "DiffuseGI.Composite", source, composite, 4,
                tier, size, frameIndex, temporal, intensity, distanceFalloff, receiverAlbedo, debug);
            resources.cameraColor = composite;
            return TextureHandle.nullHandle;
        }

        // ════════════════════════════════════════════════════════════
        //  单阶段记录 — 显式资源依赖与相机独立参数快照
        // ════════════════════════════════════════════════════════════
        private void AddPass(RenderGraph graph, UniversalResourceData resources, string name,
            TextureHandle source, TextureHandle target, int shaderPass, Vector3Int tier, Vector4 size,
            int frameIndex, TextureHandle signal, float intensity, float distanceFalloff,
            Color receiverAlbedo, DebugMode debug)
        {
            using (var builder = graph.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.shaderPass = shaderPass;
                data.traceParams = new Vector4(Mathf.Clamp(_settings.maxDistance, 0.1f, 50f),
                    Mathf.Clamp(_settings.thickness, 0.001f, 1f), Mathf.Clamp(_settings.normalBias, 0.001f, 0.5f),
                    Mathf.Clamp(distanceFalloff, 0f, 4f));
                data.filterParams = new Vector4(Mathf.Clamp(_settings.depthSigma, 0.01f, 2f), 32f, 0f, 0f);
                data.sourceSize = size;
                Color albedo = receiverAlbedo.linear;
                data.receiverAlbedo = new Color(Mathf.Clamp01(albedo.r), Mathf.Clamp01(albedo.g), Mathf.Clamp01(albedo.b), 1f);
                data.intensity = Mathf.Clamp(intensity, 0f, 4f);
                data.rayCount = tier.y;
                data.stepCount = tier.z;
                data.debugMode = (int)debug;
                data.frameIndex = frameIndex;
                data.signal = signal;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                if (shaderPass == 0)
                    builder.SetGlobalTextureAfterPass(target, Settings.TraceTextureID);
                if (shaderPass == 3)
                    builder.SetGlobalTextureAfterPass(target, Settings.TextureID);
                if (shaderPass == 4)
                {
                    builder.UseTexture(signal, AccessFlags.Read);
                    builder.UseGlobalTexture(Settings.TraceTextureID, AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);
                }
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                {
                    pass.material.SetVector(Settings.TraceParamsID, pass.traceParams);
                    pass.material.SetVector(Settings.FilterParamsID, pass.filterParams);
                    pass.material.SetVector(Settings.SourceSizeID, pass.sourceSize);
                    pass.material.SetColor(Settings.ReceiverAlbedoID, pass.receiverAlbedo);
                    pass.material.SetFloat(Settings.IntensityID, pass.intensity);
                    pass.material.SetInt(Settings.RayCountID, pass.rayCount);
                    pass.material.SetInt(Settings.StepCountID, pass.stepCount);
                    pass.material.SetInt(Settings.DebugModeID, pass.debugMode);
                    pass.material.SetInt(Settings.FrameIndexID, pass.frameIndex);
                    if (pass.signal.IsValid())
                        context.cmd.SetGlobalTexture(Settings.TextureID, pass.signal);
                    Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1f, 1f, 0f, 0f), pass.material, pass.shaderPass);
                });
            }
        }

        public void Release() => _temporal?.Release();
    }

    public Settings settings = new Settings();
    public Controls controls = new Controls();
    private Material _material;
    private DiffuseGIPass _pass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 显式 Shader 引用、相机筛选与材质释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        _pass?.Release();
        CoreUtils.Destroy(_material);
        _material = null;
        _pass = null;
        if (settings.shader == null) return;
        _material = CoreUtils.CreateEngineMaterial(settings.shader);
        _pass = new DiffuseGIPass(_material, settings, controls);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraData camera = renderingData.cameraData;
        if (_pass == null || camera.renderType == CameraRenderType.Overlay
            || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)) return;
        if (controls.intensity <= 0f && controls.debug == DebugMode.Off) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Release();
        CoreUtils.Destroy(_material);
        _material = null;
        _pass = null;
    }
}
