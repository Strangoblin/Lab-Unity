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
    public enum Performance { Low, Medium, High }
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

        [Header("Performance")]
        public Performance performance = Performance.Medium;

        [Header("Artistic")]
        [Range(0f, 4f)] public float intensity = 1f;
        [Range(0f, 4f)] public float falloff = 0f;

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int ParamsID = Shader.PropertyToID("_AOParams");
        internal static readonly int FilterParamsID = Shader.PropertyToID("_AOFilterParams");
        internal static readonly int SourceSizeID = Shader.PropertyToID("_AOSourceSize");
        internal static readonly int ModeID = Shader.PropertyToID("_AOMode");
        internal static readonly int SampleCountID = Shader.PropertyToID("_AOSampleCount");
        internal static readonly int StepCountID = Shader.PropertyToID("_AOStepCount");
        internal static readonly int DebugModeID = Shader.PropertyToID("_AODebugMode");
        internal static readonly int TextureID = Shader.PropertyToID("_AOTexture");
    }

    // ════════════════════════════════════════════════════════════
    //  性能档位 — 分辨率除数、方向/样本数与步进预算同源展开
    // ════════════════════════════════════════════════════════════
    internal static Vector3Int GetTier(Performance performance)
    {
        switch (performance)
        {
            case Performance.Low: return new Vector3Int(4, 4, 6);
            case Performance.High: return new Vector3Int(2, 8, 12);
            default: return new Vector3Int(2, 6, 8);
        }
    }

    class AOPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly Settings _settings;

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
        }

        public AOPass(Material material, Settings settings)
        {
            _material = material;
            _settings = settings;
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
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()
                || !resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                return;

            Vector3Int tier = GetTier(_settings.performance);
            RenderTextureDescriptor fullDesc = camera.cameraTargetDescriptor;
            fullDesc.depthBufferBits = 0;
            fullDesc.msaaSamples = 1;
            fullDesc.bindMS = false;
            fullDesc.useMipMap = false;
            fullDesc.autoGenerateMips = false;
            fullDesc.enableRandomWrite = false;

            var compositeDesc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            compositeDesc.name = "AO.Composite";
            compositeDesc.clearBuffer = false;
            TextureHandle composite = renderGraph.CreateTexture(compositeDesc);
            RenderTextureDescriptor aoDesc = fullDesc;
            aoDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
            aoDesc.width = Mathf.Max(1, (fullDesc.width + tier.x - 1) / tier.x);
            aoDesc.height = Mathf.Max(1, (fullDesc.height + tier.x - 1) / tier.x);
            TextureHandle occlusion = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoDesc, "AO.Trace", false);
            TextureHandle blur = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoDesc, "AO.Blur", false);
            TextureHandle filtered = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoDesc, "AO.Filtered", false);
            Vector4 size = new Vector4(1f / aoDesc.width, 1f / aoDesc.height, aoDesc.width, aoDesc.height);
            TextureHandle source = resources.activeColorTexture;

            AddPass(renderGraph, resources, "AO.Trace", source, occlusion, 0, tier, size);
            AddPass(renderGraph, resources, "AO.BlurHorizontal", occlusion, blur, 1, tier, size);
            AddPass(renderGraph, resources, "AO.BlurVertical", blur, filtered, 2, tier, size);
            AddPass(renderGraph, resources, "AO.Composite", source, composite, 3, tier, size);
            resources.cameraColor = composite;
        }

        // ════════════════════════════════════════════════════════════
        //  单阶段记录 — 显式资源依赖与相机独立参数快照
        // ════════════════════════════════════════════════════════════
        private void AddPass(RenderGraph graph, UniversalResourceData resources, string name,
            TextureHandle source, TextureHandle target, int shaderPass, Vector3Int tier, Vector4 size)
        {
            using (var builder = graph.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.shaderPass = shaderPass;
                data.parameters = new Vector4(Mathf.Clamp(_settings.radius, 0.1f, 5f),
                    Mathf.Clamp(_settings.bias, 0.001f, 0.5f), Mathf.Clamp(_settings.falloff, 0f, 4f),
                    Mathf.Clamp(_settings.intensity, 0f, 4f));
                data.filterParams = new Vector4(Mathf.Clamp(_settings.depthSigma, 0.01f, 2f), 32f,
                    Mathf.Sin(Mathf.Clamp(_settings.angleBias, 0f, 60f) * Mathf.Deg2Rad), 0f);
                data.sourceSize = size;
                data.mode = (int)_settings.mode;
                data.sampleCount = tier.y;
                data.stepCount = tier.z;
                data.debugMode = (int)_settings.debug;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                if (shaderPass == 2)
                    builder.SetGlobalTextureAfterPass(target, Settings.TextureID);
                if (shaderPass == 3)
                    builder.UseGlobalTexture(Settings.TextureID, AccessFlags.Read);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                {
                    pass.material.SetVector(Settings.ParamsID, pass.parameters);
                    pass.material.SetVector(Settings.FilterParamsID, pass.filterParams);
                    pass.material.SetVector(Settings.SourceSizeID, pass.sourceSize);
                    pass.material.SetInt(Settings.ModeID, pass.mode);
                    pass.material.SetInt(Settings.SampleCountID, pass.sampleCount);
                    pass.material.SetInt(Settings.StepCountID, pass.stepCount);
                    pass.material.SetInt(Settings.DebugModeID, pass.debugMode);
                    Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1f, 1f, 0f, 0f), pass.material, pass.shaderPass);
                });
            }
        }
    }

    public Settings settings = new Settings();
    private Material _material;
    private AOPass _pass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 显式 Shader 引用、相机筛选与材质释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        CoreUtils.Destroy(_material);
        _material = null;
        _pass = null;
        if (settings.shader == null) return;
        _material = CoreUtils.CreateEngineMaterial(settings.shader);
        _pass = new AOPass(_material, settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraData camera = renderingData.cameraData;
        if (_pass == null || camera.renderType == CameraRenderType.Overlay
            || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)) return;
        if (settings.intensity <= 0f && settings.debug == DebugMode.Off) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
        _pass = null;
    }
}
