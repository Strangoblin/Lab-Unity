// ════════════════════════════════════════════════════════════════
//  AOFeature — 屏幕空间环境光遮蔽（SSAO / HBAO）与保边模糊
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class AOFeature : ScriptableRendererFeature
{
    public enum Mode { SSAO, HBAO }
    public enum DebugMode { Off, AO, Depth, Normal }

    [System.Serializable]
    public class Settings
    {
        [Header("Technical · Resources")]
        public Shader shader;

        [Header("Technical · Geometry")]
        public Mode mode = Mode.HBAO;
        [Range(0.1f, 5f)] public float radius = 1f;
        [Range(0.001f, 0.5f)] public float bias = 0.02f;
        [Range(0f, 60f)] public float angleBias = 10f;
        [Range(0.01f, 2f)] public float depthSigma = 0.15f;

        internal static readonly int ParamsID = Shader.PropertyToID("_AOParams");
        internal static readonly int FilterParamsID = Shader.PropertyToID("_AOFilterParams");
        internal static readonly int SourceSizeID = Shader.PropertyToID("_AOSourceSize");
        internal static readonly int ModeID = Shader.PropertyToID("_AOMode");
        internal static readonly int SampleCountID = Shader.PropertyToID("_AOSampleCount");
        internal static readonly int StepCountID = Shader.PropertyToID("_AOStepCount");
        internal static readonly int FrameIndexID = Shader.PropertyToID("_AOFrameIndex");
        internal static readonly int DebugModeID = Shader.PropertyToID("_AODebugMode");
        internal static readonly int TextureID = Shader.PropertyToID("_AOTexture");
    }

    [System.Serializable]
    public sealed class Controls : SSGIStandaloneControls
    {
        [Header("Artistic")]
        [Range(0f, 4f)] public float intensity = 1f;
        [Range(0f, 4f)] public float falloff;

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        public Controls() { temporalBlend = 0.85f; }
    }

    // ════════════════════════════════════════════════════════════
    //  性能档位 — 分辨率除数、方向/样本数与步进预算同源展开
    // ════════════════════════════════════════════════════════════
    internal static Vector3Int GetTier(SSGIQuality performance)
    {
        switch (performance)
        {
            case SSGIQuality.Low: return new Vector3Int(8, 4, 6);
            case SSGIQuality.High: return new Vector3Int(2, 8, 12);
            default: return new Vector3Int(4, 6, 8);
        }
    }

    internal class AOPass : ScriptableRenderPass
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
            public Vector4 parameters;
            public Vector4 filterParams;
            public Vector4 sourceSize;
            public int mode;
            public int sampleCount;
            public int stepCount;
            public int debugMode;
            public int frameIndex;
            public TextureHandle signal;
        }

        public AOPass(Material material, Settings settings, Controls controls = null)
        {
            _material = material;
            _settings = settings;
            _controls = controls;
            _temporal = controls != null ? new SSGITemporalFilter(controls.temporalShader) : null;
            // 事件选择与 DiffuseGI 同源：遮挡只需要"不透明 + 天空盒"的深度与法线；
            // 同事件下自定义 Feature 先于 URP 内置 Pass 执行，故 AfterRenderingSkybox 同时满足
            // "天空盒已绘制"与"早于 _CameraOpaqueTexture 颜色拷贝"（URP 17 RenderGraph：
            // RecordCustomRenderGraphPasses(AfterRenderingSkybox) 在 m_CopyColorPass 之前）。
            // 若晚于颜色拷贝，透明物体折射到的拷贝不含 AO，水面会盖回未遮蔽的亮度。
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        // ════════════════════════════════════════════════════════════
        //  RenderGraph — Trace → BlurHorizontal → BlurVertical → Composite
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            _temporal.BeginFrame(frameData);
            Record(renderGraph, frameData, TextureHandle.nullHandle, false, _temporal.FrameIndex,
                _controls.performance, _controls.intensity, _controls.falloff, _controls.debug);
            _temporal.CompleteFrame(renderGraph, frameData);
        }

        public TextureHandle RecordIntegrated(RenderGraph renderGraph, ContextContainer frameData,
            TextureHandle source, int frameIndex, SSGIQuality performance)
        {
            return Record(renderGraph, frameData, source, true, frameIndex,
                performance, 1f, 0f, DebugMode.Off);
        }

        private TextureHandle Record(RenderGraph renderGraph, ContextContainer frameData,
            TextureHandle inputSource, bool integrated, int frameIndex,
            SSGIQuality performance, float intensity, float falloff, DebugMode debug)
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

            RenderTextureDescriptor aoDesc = fullDesc;
            aoDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
            aoDesc.width = Mathf.Max(1, (fullDesc.width + tier.x - 1) / tier.x);
            aoDesc.height = Mathf.Max(1, (fullDesc.height + tier.x - 1) / tier.x);
            TextureHandle occlusion = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoDesc, "AO.Trace", false);
            TextureHandle blur = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoDesc, "AO.Blur", false);
            TextureHandle filtered = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoDesc, "AO.Filtered", false);
            Vector4 size = new Vector4(1f / aoDesc.width, 1f / aoDesc.height, aoDesc.width, aoDesc.height);
            TextureHandle source = integrated ? inputSource : resources.activeColorTexture;

            AddPass(renderGraph, resources, "AO.Trace", source, occlusion, 0, tier, size, frameIndex, TextureHandle.nullHandle, intensity, falloff, debug);
            AddPass(renderGraph, resources, "AO.BlurHorizontal", occlusion, blur, 1, tier, size, frameIndex, TextureHandle.nullHandle, intensity, falloff, debug);
            AddPass(renderGraph, resources, "AO.BlurVertical", blur, filtered, 2,
                tier, size, frameIndex, TextureHandle.nullHandle, intensity, falloff, debug);
            aoDesc.width = fullDesc.width;
            aoDesc.height = fullDesc.height;
            TextureHandle resolved = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, aoDesc, "AO.Resolved", false);
            AddPass(renderGraph, resources, "AO.Resolve", filtered, resolved, 4,
                tier, size, frameIndex, TextureHandle.nullHandle, intensity, falloff, debug);
            if (integrated)
                return resolved;

            TextureHandle temporal = _temporal.Resolve(renderGraph, frameData,
                resolved, SSGITemporalFilter.Signal.AO, _controls.temporalBlend);
            var compositeDesc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            compositeDesc.name = "AO.Composite";
            compositeDesc.clearBuffer = false;
            TextureHandle composite = renderGraph.CreateTexture(compositeDesc);
            AddPass(renderGraph, resources, "AO.Composite", source, composite, 3,
                tier, size, frameIndex, temporal, intensity, falloff, debug);
            resources.cameraColor = composite;
            return TextureHandle.nullHandle;
        }

        // ════════════════════════════════════════════════════════════
        //  单阶段记录 — 显式资源依赖与相机独立参数快照
        // ════════════════════════════════════════════════════════════
        private void AddPass(RenderGraph graph, UniversalResourceData resources, string name,
            TextureHandle source, TextureHandle target, int shaderPass, Vector3Int tier, Vector4 size,
            int frameIndex, TextureHandle signal, float intensity, float falloff, DebugMode debug)
        {
            using (var builder = graph.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.shaderPass = shaderPass;
                data.parameters = new Vector4(Mathf.Clamp(_settings.radius, 0.1f, 5f),
                    Mathf.Clamp(_settings.bias, 0.001f, 0.5f), Mathf.Clamp(falloff, 0f, 4f),
                    Mathf.Clamp(intensity, 0f, 4f));
                data.filterParams = new Vector4(Mathf.Clamp(_settings.depthSigma, 0.01f, 2f), 32f,
                    Mathf.Sin(Mathf.Clamp(_settings.angleBias, 0f, 60f) * Mathf.Deg2Rad), 0f);
                data.sourceSize = size;
                data.mode = (int)_settings.mode;
                data.sampleCount = tier.y;
                data.stepCount = tier.z;
                data.debugMode = (int)debug;
                data.frameIndex = frameIndex;
                data.signal = signal;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                if (shaderPass == 2)
                    builder.SetGlobalTextureAfterPass(target, Settings.TextureID);
                if (shaderPass == 4)
                    builder.UseGlobalTexture(Settings.TextureID, AccessFlags.Read);
                if (shaderPass == 3 && signal.IsValid())
                {
                    builder.UseTexture(signal, AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);
                }
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                {
                    pass.material.SetVector(Settings.ParamsID, pass.parameters);
                    pass.material.SetVector(Settings.FilterParamsID, pass.filterParams);
                    pass.material.SetVector(Settings.SourceSizeID, pass.sourceSize);
                    pass.material.SetInt(Settings.ModeID, pass.mode);
                    pass.material.SetInt(Settings.SampleCountID, pass.sampleCount);
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
    private AOPass _pass;

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
        _pass = new AOPass(_material, settings, controls);
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
