// ════════════════════════════════════════════════════════════
//  SSC Feature — screen space clouds
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// 屏幕空间云（SSC）：低分辨率 ray march 生成密度/散射缓冲，再与相机颜色合成。
/// 噪声固定使用 2D 循环纹理的 R 通道，不再保留 3D / RG 噪声分支。
/// </summary>
/// <remarks>
/// 渲染流：Ray March → 云缓冲（并导出 `_SSCCloudTexture` 全局，A 通道为覆盖率）；
/// Composite → 新颜色目标；最后把新目标接回 cameraColor。两段 Pass 分别调度，
/// Ray March 可早于其它后处理，供 MaskRBR 等消费者扣除云区。两段 Pass 都通过
/// RenderGraph 的 TextureHandle 声明依赖，不创建额外的主色拷贝。
/// </remarks>
public sealed class SSCFeature : ScriptableRendererFeature
{
    public enum RayCount
    {
        COUNT64,
        COUNT128,
        COUNT256,
        COUNT32 = 3
    }

    public enum Quality
    {
        [InspectorName("Low")] Low,
        [InspectorName("Medium")] Medium,
        [InspectorName("High")] High
    }

    public readonly struct Tier
    {
        public readonly RayCount rayCount;
        public readonly int downsample;

        public Tier(RayCount rayCount, int downsample)
        {
            this.rayCount = rayCount;
            this.downsample = downsample;
        }
    }

    public static Tier GetTier(Quality quality)
    {
        switch (quality)
        {
            case Quality.Low: return new Tier(RayCount.COUNT32, 3);
            case Quality.High: return new Tier(RayCount.COUNT128, 1);
            default: return new Tier(RayCount.COUNT64, 2);
        }
    }

    // 云缓冲的 A 通道是覆盖率；消费者（MaskRBR）按 (1 - coverage) 扣除源遮罩。
    public static readonly int CloudTextureID = Shader.PropertyToID("_SSCCloudTexture");

    // 本次相机是否已记录 Ray March。消费者在 RecordRenderGraph 中读取，
    // 仅在为 true 时声明全局纹理依赖，避免 SSC 关闭时 UseGlobalTexture 抛异常。
    public static bool CloudOcclusionReady { get; private set; }

    // 本相机 SSC 的产出／写入时刻，供消费者校验偏序；SSC 未启用时为 null。
    // 一个 Feature 有两段时间，passEvent 这种单值字段表达不了，故单独发布。
    public static RenderPassEvent? CloudBufferEvent { get; private set; }
    public static RenderPassEvent? CloudCompositeEvent { get; private set; }

    static SSCFeature()
    {
        RenderPipelineManager.beginCameraRendering += ResetCloudOcclusion;
    }

    static void ResetCloudOcclusion(ScriptableRenderContext context, Camera camera)
    {
        CloudOcclusionReady = false;
        CloudBufferEvent = null;
        CloudCompositeEvent = null;
    }

    [System.Serializable]
    public sealed class Settings : ISerializationCallbackReceiver
    {
        [Header("Technical")]
        public Shader sscShader;
        public Texture2D mainTex2D;

        [Header("Performance")]
        public Quality quality = Quality.Medium;

        [SerializeField, HideInInspector, FormerlySerializedAs("downsample")]
        int _legacyDownsample = -1;
        [SerializeField, HideInInspector] RayCount _customRayCount = RayCount.COUNT64;
        [SerializeField, HideInInspector] int _customDownsample = 2;
        [SerializeField, HideInInspector] bool _preserveCustomBudget;
        [SerializeField, HideInInspector] Quality _customBudgetQuality = Quality.Medium;

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

        [Tooltip("HG anisotropy: higher values concentrate direct light into the forward lobe.")]
        [Range(-1f, 1f)] public float phaseG = 0.8f;
        [Tooltip("Additive isotropic HG0 lobe used to approximate cloud multiple scattering.")]
        [Range(0f, 2f)] public float secondaryScattering = 1.0f;
        [Range(0f, 2f)] public float ambientIntensity = 1.0f;

        [Header("Integration")]
        [Tooltip("Light probe offset along the light direction, in world units. The previous code used step * 20, so the effective distance drifted with the view ray's span through the cloud box. Above 10 world units the probe leaves the slab and the value stops changing the image.")]
        [Range(0f, 10f)] public float lightDistance = 10.0f;
        [Tooltip("Beer-Lambert extinction length: optical depth = density * march step / softness. 0 keeps the old hard integration, larger is softer; past 10 the cloud becomes translucent enough that the image stops changing.")]
        [Range(0f, 10f)] public float softness = 8.0f;

        [Range(0f, 1f)] public float jitter = 1.0f;

        [Header("Event")]
        [Tooltip("When the cloud buffer is produced and exported as _SSCCloudTexture. Consumers such as MaskRBR must run later than this.")]
        [FormerlySerializedAs("rayMarchEvent")]
        public RenderPassEvent cloudBufferEvent = RenderPassEvent.AfterRenderingOpaques;
        [Tooltip("When SSC writes the composited color back to the camera. Keep it after every consumer of the cloud buffer, otherwise rays are added on top of the clouds.")]
        [FormerlySerializedAs("passEvent")]
        public RenderPassEvent compositeEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Debug")]
        [FormerlySerializedAs("SSCFeature")]
        [Tooltip("true = 直接输出 SSC 云缓冲，跳过场景合成。")]
        public bool showSSC = false;

        public Tier Budget
        {
            get
            {
                if (_preserveCustomBudget)
                {
                    if (quality == _customBudgetQuality)
                        return new Tier(_customRayCount, Mathf.Clamp(_customDownsample, 0, 4));

                    _preserveCustomBudget = false;
                    _customBudgetQuality = quality;
                }

                return GetTier(quality);
            }
        }

        // 验证脚本仍可设置旧组合；正常面板只使用三档。
        public RayCount rayCount
        {
            get => Budget.rayCount;
            set => SetBudget(value, downsample);
        }

        public int downsample
        {
            get => Budget.downsample;
            set => SetBudget(rayCount, value);
        }

        public void SetBudget(RayCount rays, int shift)
        {
            _customRayCount = rays;
            _customDownsample = shift;
            for (int i = (int)Quality.Low; i <= (int)Quality.High; i++)
            {
                Tier tier = GetTier((Quality)i);
                if (tier.rayCount != rays || tier.downsample != shift) continue;
                quality = (Quality)i;
                _customBudgetQuality = quality;
                _preserveCustomBudget = false;
                return;
            }

            _customBudgetQuality = quality;
            _preserveCustomBudget = true;
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if ((int)quality < (int)Quality.Low || quality > Quality.High)
                quality = Quality.Medium;

            if (_legacyDownsample < 0) return;

            Quality migrated = _legacyDownsample >= 3 ? Quality.Low
                : _legacyDownsample <= 1 ? Quality.High
                : Quality.Medium;
            quality = migrated;
            _customBudgetQuality = quality;
            _preserveCustomBudget = false;
            _legacyDownsample = -1;
        }

        internal static readonly int BaseColorAID = Shader.PropertyToID("_BaseColorA");
        internal static readonly int BaseColorBID = Shader.PropertyToID("_BaseColorB");
        internal static readonly int MainTex2DID = Shader.PropertyToID("_MainTex2D");
        internal static readonly int ParamAID = Shader.PropertyToID("_CloudParamA");
        internal static readonly int ParamBID = Shader.PropertyToID("_CloudParamB");
        internal static readonly int CloudMiddleID = Shader.PropertyToID("_CloudMiddle");
        internal static readonly int CloudThresholdID = Shader.PropertyToID("_CloudThreshold");
        internal static readonly int PhaseGID = Shader.PropertyToID("_PhaseG");
        internal static readonly int SecondaryScatteringID = Shader.PropertyToID("_SecondaryScattering");
        internal static readonly int AmbientIntensityID = Shader.PropertyToID("_AmbientIntensity");
        internal static readonly int LightDistanceID = Shader.PropertyToID("_LightDistance");
        internal static readonly int SoftnessID = Shader.PropertyToID("_Softness");
        internal static readonly int JitterID = Shader.PropertyToID("_Jitter");
        internal static readonly int SSCTexID = Shader.PropertyToID("_SSCTex");
        internal static readonly int TextureSizeID = Shader.PropertyToID("_SSCTextureSize");
        internal static readonly int DownsampleID = Shader.PropertyToID("_SSCDownsample");
    }

    // Ray March 与 Composite 之间共享本帧的云缓冲与尺寸；TextureHandle 只在同一张图中有效。
    sealed class FrameState
    {
        public TextureHandle cloudTexture;
        public Vector4 textureSize;
        public int downsample;
    }

    // ════════════════════════════════════════════════════════════
    //  Ray March — 低分辨率云缓冲，导出覆盖率全局
    // ════════════════════════════════════════════════════════════
    sealed class RayMarchPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle cloudRT;
        }

        const int RayMarchPassIndex = 0;
        const string RayCount32Keyword = "SSC_RAY_COUNT_32";
        const string RayCount64Keyword = "SSC_RAY_COUNT_64";
        const string RayCount128Keyword = "SSC_RAY_COUNT_128";
        const string RayCount256Keyword = "SSC_RAY_COUNT_256";

        readonly Settings _settings;
        readonly Material _material;
        readonly FrameState _state;

        public RayMarchPass(Settings settings, Material material, FrameState state)
        {
            _settings = settings;
            _material = material;
            _state = state;
            renderPassEvent = settings.cloudBufferEvent;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            _state.cloudTexture = default;
            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid() || _material == null) return;

            Tier tier = _settings.Budget;
            ConfigureMaterial(tier);

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;

            int shift = tier.downsample;
            RenderTextureDescriptor cloudDesc = desc;
            cloudDesc.width = Mathf.Max(1, desc.width >> shift);
            cloudDesc.height = Mathf.Max(1, desc.height >> shift);
            // 云 RGB 是预乘辐亮度，A 是覆盖率；相机 HDR 格式可能没有 alpha。
            cloudDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            _state.textureSize = new Vector4(
                1f / cloudDesc.width, 1f / cloudDesc.height, cloudDesc.width, cloudDesc.height);
            _state.downsample = shift;

            TextureHandle cloudRT = UniversalRenderer.CreateRenderGraphTexture(
                graph, cloudDesc, "_SSCCloudRT", false, FilterMode.Bilinear);

            TextureHandle depth = resources.cameraDepthTexture;
            AddRayMarchPass(graph, source, cloudRT, depth);
            _state.cloudTexture = cloudRT;
            CloudOcclusionReady = true;
        }

        void AddRayMarchPass(RenderGraph graph, TextureHandle source, TextureHandle cloudRT, TextureHandle depth)
        {
            using (var builder = graph.AddUnsafePass<PassData>("SSC Ray March", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.cloudRT = cloudRT;

                builder.UseTexture(source, AccessFlags.Read);
                if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
                builder.UseTexture(cloudRT, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(cloudRT, CloudTextureID);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(cmd, data.source, data.cloudRT, data.material, RayMarchPassIndex);
                });
            }
        }

        // ════════════════════════════════════════════════════════════
        //  ConfigureMaterial — 每帧把 Inspector 参数与 ray count 变体写入材质
        // ════════════════════════════════════════════════════════════
        void ConfigureMaterial(Tier tier)
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
            _material.SetFloat(Settings.SecondaryScatteringID, _settings.secondaryScattering);
            _material.SetFloat(Settings.AmbientIntensityID, _settings.ambientIntensity);
            _material.SetFloat(Settings.LightDistanceID, _settings.lightDistance);
            _material.SetFloat(Settings.SoftnessID, _settings.softness);
            _material.SetFloat(Settings.JitterID, _settings.jitter);
            _material.SetVector(Settings.TextureSizeID, _state.textureSize);
            _material.SetFloat(Settings.DownsampleID, _state.downsample);

            _material.DisableKeyword(RayCount32Keyword);
            _material.DisableKeyword(RayCount64Keyword);
            _material.DisableKeyword(RayCount128Keyword);
            _material.DisableKeyword(RayCount256Keyword);

            switch (tier.rayCount)
            {
                case RayCount.COUNT32:  _material.EnableKeyword(RayCount32Keyword);  break;
                case RayCount.COUNT64:  _material.EnableKeyword(RayCount64Keyword);  break;
                case RayCount.COUNT256: _material.EnableKeyword(RayCount256Keyword); break;
                default:                _material.EnableKeyword(RayCount128Keyword); break;
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Composite — 预乘合成，接管相机颜色
    // ════════════════════════════════════════════════════════════
    sealed class CompositePass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle cloudRT;
            public TextureHandle compositeRT;
            public bool showSSC;
            public Vector4 textureSize;
            public int downsample;
        }

        const int CompositePassIndex = 1;

        readonly Settings _settings;
        readonly Material _material;
        readonly FrameState _state;

        public CompositePass(Settings settings, Material material, FrameState state)
        {
            _settings = settings;
            _material = material;
            _state = state;
            renderPassEvent = settings.compositeEvent;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid() || _material == null || !_state.cloudTexture.IsValid()) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;

            TextureHandle compositeRT = UniversalRenderer.CreateRenderGraphTexture(
                graph, desc, "_SSCCompositeRT", false, FilterMode.Bilinear);

            TextureHandle depth = resources.cameraDepthTexture;
            AddCompositePass(graph, source, _state.cloudTexture, compositeRT, depth);

            // 合成结果直接接管相机颜色，后续 Pass 读取新颜色。
            resources.cameraColor = compositeRT;
        }

        void AddCompositePass(RenderGraph graph, TextureHandle source, TextureHandle cloudRT,
            TextureHandle compositeRT, TextureHandle depth)
        {
            using (var builder = graph.AddUnsafePass<PassData>("SSC Composite", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.cloudRT = cloudRT;
                passData.compositeRT = compositeRT;
                passData.showSSC = _settings.showSSC;
                passData.textureSize = _state.textureSize;
                passData.downsample = _state.downsample;

                builder.UseTexture(source, AccessFlags.Read);
                if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
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

                    data.material.SetVector(Settings.TextureSizeID, data.textureSize);
                    data.material.SetFloat(Settings.DownsampleID, data.downsample);
                    cmd.SetGlobalTexture(Settings.SSCTexID, data.cloudRT);
                    Blitter.BlitCameraTexture(
                        cmd, data.source, data.compositeRT, data.material, CompositePassIndex);
                });
            }
        }
    }

    public Settings settings = new Settings();
    Material _material;
    RayMarchPass _rayMarchPass;
    CompositePass _compositePass;
    FrameState _frameState;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 两个 Pass 共享材质与帧状态
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        CoreUtils.Destroy(_material);
        _material = settings.sscShader != null ? CoreUtils.CreateEngineMaterial(settings.sscShader) : null;
        _frameState = new FrameState();
        _rayMarchPass = _material != null ? new RayMarchPass(settings, _material, _frameState) : null;
        _compositePass = _material != null ? new CompositePass(settings, _material, _frameState) : null;
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
        _rayMarchPass = null;
        _compositePass = null;
        _frameState = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 所有 Feature 的 AddRenderPasses 都在任何 Pass 录制之前完成，
        // 因此这里发布的两个时刻对消费者（不论其在列表中的先后）都已可见。
        CloudBufferEvent = settings.cloudBufferEvent;
        CloudCompositeEvent = settings.compositeEvent;

        if (_rayMarchPass == null || _compositePass == null) return;
        _rayMarchPass.renderPassEvent = settings.cloudBufferEvent;
        _compositePass.renderPassEvent = settings.compositeEvent;
        renderer.EnqueuePass(_rayMarchPass);
        renderer.EnqueuePass(_compositePass);
    }
}
