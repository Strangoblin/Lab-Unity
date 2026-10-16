// ════════════════════════════════════════════════════════════
//  SSO Feature — screen space outline
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// 屏幕空间轮廓：深度 / 法线差分检测轮廓，叠加投影条纹阴影后与场景色合成。
/// 差分方式与阴影方式由关键字切换，参数分层见 Settings 的 Header 分组。
/// </summary>
public sealed class SSOFeature : ScriptableRendererFeature
{
    public enum SSOType
    {
        DDXY,
        Basic,
        Sobel
    }

    public enum ShadowType
    {
        None,
        Hard,
        Soft
    }

    public enum DebugMode
    {
        Off,
        Outline
    }

    [System.Serializable]
    public sealed class OutlineParams
    {
        [Range(0f, 1f)] public float intensity = 1.0f;
        [Range(0f, 5f)] public float thickness = 1;
        public Vector2 threshold = new Vector2(0.1f, 0.2f);
    }

    [System.Serializable]
    public sealed class ShadowParams
    {
        public ShadowType shadowType = ShadowType.Soft;
        [Range(0f, 1f)] public float Intensity = 0.5f;
        [Range(0f, 1f)] public float Sharpness = 1f;
        [Range(0f, 1f)] public float Thickness = 0.1f;
        [Range(0f, 1f)] public float Density = 0.5f;
    }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader ssoShader;
        public SSOType ssoType = SSOType.Basic;
        public OutlineParams depthParams  = new OutlineParams();
        public OutlineParams normalParams = new OutlineParams();
        public ShadowParams shadowParams = new ShadowParams();

        [Header("Performance")]
        [Range(0, 4)] public int downsample = 0;

        [Header("Artistic")]
        [Range(0f, 1f)] public float jitter = 1.0f;
        public Color OutlineColor = Color.white;

        [Header("Debug")]
        [FormerlySerializedAs("SSOFeature")]
        [Tooltip("Off = 正常合成；Outline = 直接输出轮廓 / 条纹中间结果，便于人工观测。")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int DepthIntensityID  = Shader.PropertyToID("_DepthIntensity");
        internal static readonly int DepthThicknessID  = Shader.PropertyToID("_DepthThickness");
        internal static readonly int DepthThresholdID  = Shader.PropertyToID("_DepthThreshold");

        internal static readonly int NormalIntensityID = Shader.PropertyToID("_NormalIntensity");
        internal static readonly int NormalThicknessID = Shader.PropertyToID("_NormalThickness");
        internal static readonly int NormalThresholdID = Shader.PropertyToID("_NormalThreshold");

        internal static readonly int ShadowIntensityID = Shader.PropertyToID("_ShadowIntensity");
        internal static readonly int ShadowSharpnessID = Shader.PropertyToID("_ShadowSharpness");
        internal static readonly int ShadowThicknessID = Shader.PropertyToID("_ShadowThickness");
        internal static readonly int ShadowDensityID   = Shader.PropertyToID("_ShadowDensity");

        internal static readonly int JitterID       = Shader.PropertyToID("_Jitter");
        internal static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
        internal static readonly int SSOTexID       = Shader.PropertyToID("_SSOTex");
    }

    sealed class SSORenderPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle tempMain;
            public TextureHandle diff;
            public DebugMode debug;
        }

        // Shader Pass 顺序：0 = 轮廓检测, 1 = 合成
        const int OutlinePassIndex   = 0;
        const int CompositePassIndex = 1;

        // 差分方式关键字
        const string DDXYKeyword  = "SSO_DDXY";
        const string BasicKeyword = "SSO_Basic";
        const string SobelKeyword = "SSO_Sobel";

        // 阴影方式关键字
        const string ShadowNoneKeyword = "SSO_SHADOW_NONE";
        const string ShadowHardKeyword = "SSO_SHADOW_HARD";
        const string ShadowSoftKeyword = "SSO_SHADOW_SOFT";

        readonly Settings _settings;
        readonly Material _material;

        public SSORenderPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.ssoShader);
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        // ════════════════════════════════════════════════════════════
        //  RecordRenderGraph — 轮廓检测、合成，RT 由 RenderGraph 管理
        //
        //  两个 Pass 的源色都走 Blitter 的 _BlitTexture（Pass 0 为 tempMain，
        //  Pass 1 为 tempMain），本 Pass 只需再发布 _SSOTex
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (_material == null) return;

            _material.SetFloat(Settings.DepthIntensityID, _settings.depthParams.intensity);
            _material.SetFloat(Settings.DepthThicknessID, _settings.depthParams.thickness);
            _material.SetVector(Settings.DepthThresholdID, _settings.depthParams.threshold);

            _material.SetFloat(Settings.NormalIntensityID, _settings.normalParams.intensity);
            _material.SetFloat(Settings.NormalThicknessID, _settings.normalParams.thickness);
            _material.SetVector(Settings.NormalThresholdID, _settings.normalParams.threshold);

            _material.SetFloat(Settings.ShadowIntensityID, _settings.shadowParams.Intensity);
            _material.SetFloat(Settings.ShadowSharpnessID, _settings.shadowParams.Sharpness);
            _material.SetFloat(Settings.ShadowThicknessID, _settings.shadowParams.Thickness);
            _material.SetFloat(Settings.ShadowDensityID, _settings.shadowParams.Density);

            _material.SetFloat(Settings.JitterID, _settings.jitter);
            _material.SetColor(Settings.OutlineColorID, _settings.OutlineColor);

            // ── 差分方式关键字 ──
            _material.DisableKeyword(DDXYKeyword);
            _material.DisableKeyword(BasicKeyword);
            _material.DisableKeyword(SobelKeyword);
            switch (_settings.ssoType)
            {
                case SSOType.Basic: _material.EnableKeyword(BasicKeyword); break;
                case SSOType.Sobel: _material.EnableKeyword(SobelKeyword); break;
                default:            _material.EnableKeyword(DDXYKeyword);  break;
            }

            // ── 阴影方式关键字 ──
            _material.DisableKeyword(ShadowNoneKeyword);
            _material.DisableKeyword(ShadowHardKeyword);
            _material.DisableKeyword(ShadowSoftKeyword);
            switch (_settings.shadowParams.shadowType)
            {
                case ShadowType.Hard: _material.EnableKeyword(ShadowHardKeyword); break;
                case ShadowType.Soft: _material.EnableKeyword(ShadowSoftKeyword); break;
                default:              _material.EnableKeyword(ShadowNoneKeyword); break;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            TextureHandle tempMain = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSOTempMainRT", false);

            desc.width >>= _settings.downsample;
            desc.height >>= _settings.downsample;
            TextureHandle diff = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSODiffRT", false);

            using (var builder = renderGraph.AddUnsafePass<PassData>("SSO RenderGraph Pass", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.tempMain = tempMain;
                passData.diff = diff;
                passData.debug = _settings.debug;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(tempMain, AccessFlags.ReadWrite);
                builder.UseTexture(diff, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    cmd.SetGlobalTexture(Settings.SSOTexID, data.diff);

                    Blitter.BlitCameraTexture(cmd, data.source, data.tempMain);
                    Blitter.BlitCameraTexture(cmd, data.tempMain, data.diff, data.material, OutlinePassIndex);
                    Blitter.BlitCameraTexture(cmd, data.tempMain, data.source, data.material, CompositePassIndex);

                    // Debug: 用轮廓 / 条纹中间结果顶掉最终输出
                    if (data.debug == DebugMode.Outline)
                    {
                        Blitter.BlitCameraTexture(cmd, data.diff, data.source);
                    }
                });
            }
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
        }
    }

    public Settings settings = new Settings();
    SSORenderPass ssoPass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 材质随 Pass 一起重建与释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        ssoPass?.Release();
        ssoPass = settings.ssoShader != null ? new SSORenderPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        ssoPass?.Release();
        ssoPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ssoPass == null) return;
        renderer.EnqueuePass(ssoPass);
    }
}
