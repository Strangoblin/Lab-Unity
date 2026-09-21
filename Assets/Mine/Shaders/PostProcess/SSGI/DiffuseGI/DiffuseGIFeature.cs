// ════════════════════════════════════════════════════════════
//  DiffuseGIFeature — 屏幕空间间接漫反射与保边重建
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class DiffuseGIFeature : ScriptableRendererFeature
{
    public enum Performance { Low, Medium, High }
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

        [Header("Performance")]
        public Performance performance = Performance.Medium;

        [Header("Artistic")]
        [Range(0f, 4f)] public float intensity = 1f;
        [Range(0f, 4f)] public float distanceFalloff = 0f;
        [ColorUsage(false, false)] public Color receiverAlbedo = new Color(0.8f, 0.8f, 0.8f, 1f);

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int TraceParamsID = Shader.PropertyToID("_GITraceParams");
        internal static readonly int FilterParamsID = Shader.PropertyToID("_GIFilterParams");
        internal static readonly int SourceSizeID = Shader.PropertyToID("_GISourceSize");
        internal static readonly int IntensityID = Shader.PropertyToID("_GIIntensity");
        internal static readonly int ReceiverAlbedoID = Shader.PropertyToID("_GIReceiverAlbedo");
        internal static readonly int RayCountID = Shader.PropertyToID("_GIRayCount");
        internal static readonly int StepCountID = Shader.PropertyToID("_GIStepCount");
        internal static readonly int DebugModeID = Shader.PropertyToID("_GIDebugMode");
        internal static readonly int TextureID = Shader.PropertyToID("_GITexture");
        internal static readonly int TraceTextureID = Shader.PropertyToID("_GITraceTexture");
    }

    // ════════════════════════════════════════════════════════════
    //  性能档位 — 分辨率、射线和步进预算同源展开
    // ════════════════════════════════════════════════════════════
    internal static Vector3Int GetTier(Performance performance)
    {
        switch (performance)
        {
            case Performance.Low: return new Vector3Int(4, 1, 24);
            case Performance.High: return new Vector3Int(2, 4, 64);
            default: return new Vector3Int(2, 2, 48);
        }
    }

    class DiffuseGIPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly Settings _settings;

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
        }

        public DiffuseGIPass(Material material, Settings settings)
        {
            _material = material;
            _settings = settings;
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
            compositeDesc.name = "DiffuseGI.Composite";
            compositeDesc.clearBuffer = false;
            TextureHandle composite = renderGraph.CreateTexture(compositeDesc);
            RenderTextureDescriptor giDesc = fullDesc;
            giDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            TextureHandle resolved = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Resolved", false);
            giDesc.width = Mathf.Max(1, (fullDesc.width + tier.x - 1) / tier.x);
            giDesc.height = Mathf.Max(1, (fullDesc.height + tier.x - 1) / tier.x);
            TextureHandle trace = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Trace", false);
            TextureHandle blur = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Blur", false);
            TextureHandle filtered = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giDesc, "DiffuseGI.Filtered", false);
            Vector4 size = new Vector4(1f / giDesc.width, 1f / giDesc.height, giDesc.width, giDesc.height);
            TextureHandle source = resources.activeColorTexture;

            AddPass(renderGraph, resources, "DiffuseGI.Trace", source, trace, 0, tier, size);
            AddPass(renderGraph, resources, "DiffuseGI.BlurHorizontal", trace, blur, 1, tier, size);
            AddPass(renderGraph, resources, "DiffuseGI.BlurVertical", blur, filtered, 2, tier, size);
            AddPass(renderGraph, resources, "DiffuseGI.Resolve", filtered, resolved, 3, tier, size);
            AddPass(renderGraph, resources, "DiffuseGI.Composite", source, composite, 4, tier, size);
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
                data.traceParams = new Vector4(Mathf.Clamp(_settings.maxDistance, 0.1f, 50f),
                    Mathf.Clamp(_settings.thickness, 0.001f, 1f), Mathf.Clamp(_settings.normalBias, 0.001f, 0.5f),
                    Mathf.Clamp(_settings.distanceFalloff, 0f, 4f));
                data.filterParams = new Vector4(Mathf.Clamp(_settings.depthSigma, 0.01f, 2f), 32f, 0f, 0f);
                data.sourceSize = size;
                Color albedo = _settings.receiverAlbedo.linear;
                data.receiverAlbedo = new Color(Mathf.Clamp01(albedo.r), Mathf.Clamp01(albedo.g), Mathf.Clamp01(albedo.b), 1f);
                data.intensity = Mathf.Clamp(_settings.intensity, 0f, 4f);
                data.rayCount = tier.y;
                data.stepCount = tier.z;
                data.debugMode = (int)_settings.debug;

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
                    builder.UseGlobalTexture(Settings.TextureID, AccessFlags.Read);
                    builder.UseGlobalTexture(Settings.TraceTextureID, AccessFlags.Read);
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
                    Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1f, 1f, 0f, 0f), pass.material, pass.shaderPass);
                });
            }
        }
    }

    public Settings settings = new Settings();
    private Material _material;
    private DiffuseGIPass _pass;

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
        _pass = new DiffuseGIPass(_material, settings);
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
