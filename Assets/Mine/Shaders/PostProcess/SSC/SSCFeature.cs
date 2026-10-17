// ════════════════════════════════════════════════════════════
//  SSC Feature — screen space clouds
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// 屏幕空间云（SSC）：低分辨率 ray march 生成密度/散射缓冲，再与相机颜色合成。
/// 噪声固定使用 2D 循环纹理的 R 通道，不再保留 3D / RG 噪声分支。
/// </summary>
/// <remarks>
/// 渲染流：Ray March → 云缓冲；Composite → 新颜色目标；最后把新目标接回 cameraColor。
/// 两段 Pass 都通过 RenderGraph 的 TextureHandle 声明依赖，不创建额外的主色拷贝。
/// </remarks>
public sealed class SSCFeature : ScriptableRendererFeature
{
    public enum RayCount
    {
        COUNT64,
        COUNT128,
        COUNT256
    }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader sscShader;
        public Texture2D mainTex2D;

        [Header("Performance")]
        public RayCount rayCount = RayCount.COUNT128;
        [Range(0, 4)] public int downsample = 0;

        [Header("Artistic")]
        public Color baseColorA = Color.white;
        public Color baseColorB = Color.black;

        [Range(0f, 100.0f)] public float scale = 1.0f;
        [Range(0f, 100.0f)] public float speed = 1.0f;
        [Range(0f, 100.0f)] public float height = 100.0f;
        [Range(0f, 360.0f)] public float rotation = 0.0f;
        [Range(0f, 1.0f)] public float thickness = 0.5f;
        [Range(0f, 1f)] public float middle = 0.5f;
        [Range(0f, 1f)] public float threshold = 0.2f;
        public Vector3 size = new Vector3(100, 50, 100);

        [Range(-1f, 1f)] public float phaseG = 0.8f;
        [Range(0f, 2f)] public float ambientIntensity = 1.0f;

        [Range(0f, 1f)] public float jitter = 1.0f;

        [Header("Debug")]
        [FormerlySerializedAs("SSCFeature")]
        [Tooltip("true = 直接输出 SSC 云缓冲，跳过场景合成。")]
        public bool showSSC = false;

        internal static readonly int BaseColorAID = Shader.PropertyToID("_BaseColorA");
        internal static readonly int BaseColorBID = Shader.PropertyToID("_BaseColorB");
        internal static readonly int MainTex2DID = Shader.PropertyToID("_MainTex2D");
        internal static readonly int ParamAID = Shader.PropertyToID("_CloudParamA");
        internal static readonly int ParamBID = Shader.PropertyToID("_CloudParamB");
        internal static readonly int CloudMiddleID = Shader.PropertyToID("_CloudMiddle");
        internal static readonly int CloudThresholdID = Shader.PropertyToID("_CloudThreshold");
        internal static readonly int PhaseGID = Shader.PropertyToID("_PhaseG");
        internal static readonly int AmbientIntensityID = Shader.PropertyToID("_AmbientIntensity");
        internal static readonly int JitterID = Shader.PropertyToID("_Jitter");
        internal static readonly int SSCTexID = Shader.PropertyToID("_SSCTex");
    }

    sealed class SSCRenderPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle cloudRT;
            public TextureHandle compositeRT;
            public bool showSSC;
        }

        // Shader Pass 顺序：0 = Ray March, 1 = Composite
        const int RayMarchPassIndex = 0;
        const int CompositePassIndex = 1;

        const string RayCount64Keyword = "SSC_RAY_COUNT_64";
        const string RayCount128Keyword = "SSC_RAY_COUNT_128";
        const string RayCount256Keyword = "SSC_RAY_COUNT_256";

        readonly Settings _settings;
        readonly Material _material;

        public SSCRenderPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.sscShader);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        // ════════════════════════════════════════════════════════════
        //  RecordRenderGraph — Ray March 写低分辨率云缓冲，Composite 写全分辨率颜色
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || _material == null) return;

            ConfigureMaterial();

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;

            int shift = Mathf.Clamp(_settings.downsample, 0, 4);
            RenderTextureDescriptor cloudDesc = desc;
            cloudDesc.width = Mathf.Max(1, desc.width >> shift);
            cloudDesc.height = Mathf.Max(1, desc.height >> shift);

            TextureHandle cloudRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, cloudDesc, "_SSCCloudRT", false, FilterMode.Bilinear);
            TextureHandle compositeRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSCCompositeRT", false, FilterMode.Bilinear);

            AddRayMarchPass(renderGraph, source, cloudRT);
            AddCompositePass(renderGraph, source, cloudRT, compositeRT);

            // 合成结果直接接管相机颜色，后续 Pass 读取新颜色。
            resourceData.cameraColor = compositeRT;
        }

        void AddRayMarchPass(RenderGraph renderGraph, TextureHandle source, TextureHandle cloudRT)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("SSC Ray March", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.cloudRT = cloudRT;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(cloudRT, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(cmd, data.source, data.cloudRT, data.material, RayMarchPassIndex);
                });
            }
        }

        void AddCompositePass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle cloudRT,
            TextureHandle compositeRT)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("SSC Composite", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.cloudRT = cloudRT;
                passData.compositeRT = compositeRT;
                passData.showSSC = _settings.showSSC;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(cloudRT, AccessFlags.Read);
                builder.UseTexture(compositeRT, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    if (data.showSSC)
                    {
                        Blitter.BlitCameraTexture(cmd, data.cloudRT, data.compositeRT);
                        return;
                    }

                    cmd.SetGlobalTexture(Settings.SSCTexID, data.cloudRT);
                    Blitter.BlitCameraTexture(
                        cmd, data.source, data.compositeRT, data.material, CompositePassIndex);
                });
            }
        }

        // ════════════════════════════════════════════════════════════
        //  ConfigureMaterial — 每帧把 Inspector 参数与 ray count 变体写入材质
        // ════════════════════════════════════════════════════════════
        void ConfigureMaterial()
        {
            _material.SetColor(Settings.BaseColorAID, _settings.baseColorA);
            _material.SetColor(Settings.BaseColorBID, _settings.baseColorB);
            _material.SetTexture(Settings.MainTex2DID, _settings.mainTex2D);
            _material.SetVector(Settings.ParamAID,
                new Vector4(_settings.scale, _settings.speed, _settings.rotation, _settings.height));
            _material.SetVector(Settings.ParamBID,
                new Vector4(_settings.size.x, _settings.size.y, _settings.size.z, _settings.thickness));
            _material.SetFloat(Settings.CloudMiddleID, _settings.middle);
            _material.SetFloat(Settings.CloudThresholdID, _settings.threshold);
            _material.SetFloat(Settings.PhaseGID, _settings.phaseG);
            _material.SetFloat(Settings.AmbientIntensityID, _settings.ambientIntensity);
            _material.SetFloat(Settings.JitterID, _settings.jitter);

            _material.DisableKeyword(RayCount64Keyword);
            _material.DisableKeyword(RayCount128Keyword);
            _material.DisableKeyword(RayCount256Keyword);

            switch (_settings.rayCount)
            {
                case RayCount.COUNT64:  _material.EnableKeyword(RayCount64Keyword);  break;
                case RayCount.COUNT256: _material.EnableKeyword(RayCount256Keyword); break;
                default:                _material.EnableKeyword(RayCount128Keyword); break;
            }
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
        }
    }

    public Settings settings = new Settings();
    SSCRenderPass sscPass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 材质随 Pass 一起重建与释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        sscPass?.Release();
        sscPass = settings.sscShader != null ? new SSCRenderPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        sscPass?.Release();
        sscPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (sscPass == null) return;
        renderer.EnqueuePass(sscPass);
    }
}
