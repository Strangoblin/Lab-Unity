// ════════════════════════════════════════════════════════════
//  SSL Feature — screen space light (ray march / radial blur)
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// 屏幕空间光：RAY3D 沿视线步进累积阴影光照或深度密度，RBR2D 沿主光方向做全屏径向模糊。
/// 管线为 生成 → 多级模糊链 → 合成，中间结果按 <c>_SSLTex</c> 发布给其他 Shader。
/// </summary>
public sealed class SSLFeature : ScriptableRendererFeature
{
    public enum SSLMode
    {
        RAY3D_Fog,
        RAY3D_Light,
        RBR2D
    }

    /// <summary>纯成本档位：一次决定步进数与模糊链层级 / 迭代数。</summary>
    public enum QualityMode
    {
        Low,
        Medium,
        High
    }

    public enum DebugMode
    {
        Off,
        Light
    }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader sslShader;
        public SSLMode sslMode = SSLMode.RAY3D_Light;

        [Header("Performance")]
        [Tooltip("Low = 16 步、无模糊链；Medium = 32 步、1 级 x1；High = 64 步、2 级 x2。"
                 + "默认 Medium 逐字段复现整理前的面板值 (32, 1, 1)。")]
        public QualityMode quality = QualityMode.Medium;

        [Header("Artistic")]
        [Range(0.1f, 100f)] public float maxDistance = 10f;
        [Range(0f, 5f)] public float intensity = 1f;
        [Range(0f, 0.999f)] public float sslScale = 0.5f;
        [Range(0f, 1f)] public float jitterScale = 0.5f;
        [Range(0f, 5f)] public float blurScale = 0.5f;

        [Header("Debug")]
        [FormerlySerializedAs("SSLFeature")]
        [Tooltip("Off = 正常合成；Light = 直接把 _SSLTex 输出到屏幕，便于人工观测中间结果。")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int MaxStepsID      = Shader.PropertyToID("_MaxSteps");
        internal static readonly int MaxDistanceID   = Shader.PropertyToID("_MaxDistance");
        internal static readonly int IntensityID     = Shader.PropertyToID("_Intensity");
        internal static readonly int SSLScaleID      = Shader.PropertyToID("_SSLScale");
        internal static readonly int BlurScaleID     = Shader.PropertyToID("_BlurScale");
        internal static readonly int JitterScaleID   = Shader.PropertyToID("_JitterScale");
        internal static readonly int SSLTexID        = Shader.PropertyToID("_SSLTex");

        // ════════════════════════════════════════════════════════════
        //  档位表 — 纯函数展开，无共享可变状态
        // ════════════════════════════════════════════════════════════
        public static void ResolveQuality(QualityMode quality,
            out int maxSteps, out int blurLevels, out int blurIterations)
        {
            switch (quality)
            {
                case QualityMode.Low:
                    maxSteps = 16; blurLevels = 0; blurIterations = 1;
                    break;
                case QualityMode.High:
                    maxSteps = 64; blurLevels = 2; blurIterations = 2;
                    break;
                default:
                    maxSteps = 32; blurLevels = 1; blurIterations = 1;
                    break;
            }
        }
    }

    sealed class SSLRenderPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle tempMainRT;
            public TextureHandle[] blurPing;
            public TextureHandle[] blurPong;
            public int blurLevels;
            public int blurIterations;
            public DebugMode debug;
        }

        // Shader Pass 顺序：0 = SSL 生成, 1 = BlurH, 2 = BlurV, 3 = Mix
        const int GeneratePassIndex     = 0;
        const int BlurHorizontalPassIndex = 1;
        const int BlurVerticalPassIndex   = 2;
        const int MixPassIndex            = 3;

        // 模式关键字：RAY3D 再由 FOG / LIGHT 细分
        const string RBR2DKeyword  = "SSL_RBR2D";
        const string RAY3DKeyword  = "SSL_RAY3D";
        const string FogKeyword    = "SSL_FOG";
        const string LightKeyword  = "SSL_LIGHT";

        readonly Settings _settings;
        readonly Material _material;

        public SSLRenderPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.sslShader);
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        // ════════════════════════════════════════════════════════════
        //  RecordRenderGraph — 生成、多级模糊链、合成，RT 由 RenderGraph 管理
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || _material == null) return;

            // ── 质量档展开（纯函数，不缓存状态） ──
            Settings.ResolveQuality(_settings.quality,
                out int maxSteps, out int blurLevels, out int blurIterations);

            // ── 材质参数 ──
            _material.SetInt(Settings.MaxStepsID, maxSteps);
            _material.SetFloat(Settings.MaxDistanceID, _settings.maxDistance);
            _material.SetFloat(Settings.IntensityID, _settings.intensity);
            _material.SetFloat(Settings.SSLScaleID, _settings.sslScale);
            _material.SetFloat(Settings.BlurScaleID, _settings.blurScale);
            _material.SetFloat(Settings.JitterScaleID, _settings.jitterScale);

            // ── 模式关键字 ──
            _material.DisableKeyword(RBR2DKeyword);
            _material.DisableKeyword(RAY3DKeyword);
            _material.DisableKeyword(FogKeyword);
            _material.DisableKeyword(LightKeyword);
            switch (_settings.sslMode)
            {
                case SSLMode.RAY3D_Fog:
                    _material.EnableKeyword(RAY3DKeyword);
                    _material.EnableKeyword(FogKeyword);
                    break;
                case SSLMode.RAY3D_Light:
                    _material.EnableKeyword(RAY3DKeyword);
                    _material.EnableKeyword(LightKeyword);
                    break;
                case SSLMode.RBR2D:
                    _material.EnableKeyword(RBR2DKeyword);
                    break;
            }

            // ── RT ──
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;

            TextureHandle tempMainRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSLTempMainRT", false);

            // 多级模糊纹理
            int maxLevel = blurLevels;
            var blurPing = new TextureHandle[maxLevel + 1];
            var blurPong = new TextureHandle[maxLevel + 1];
            for (int i = 0; i <= maxLevel; i++)
            {
                int w = Mathf.Max(1, desc.width >> i);
                int h = Mathf.Max(1, desc.height >> i);
                var lvlDesc = new RenderTextureDescriptor(w, h, RenderTextureFormat.Default, 0)
                { sRGB = false, useMipMap = false };
                blurPing[i] = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, lvlDesc, $"_SSL_Ping_L{i}", false);
                blurPong[i] = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, lvlDesc, $"_SSL_Pong_L{i}", false);
            }

            using (var builder = renderGraph.AddUnsafePass<PassData>("SSL", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.tempMainRT = tempMainRT;
                passData.blurPing = blurPing;
                passData.blurPong = blurPong;
                passData.blurLevels = blurLevels;
                passData.blurIterations = blurIterations;
                passData.debug = _settings.debug;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(tempMainRT, AccessFlags.ReadWrite);
                for (int i = 0; i <= maxLevel; i++)
                {
                    builder.UseTexture(blurPing[i], AccessFlags.ReadWrite);
                    builder.UseTexture(blurPong[i], AccessFlags.ReadWrite);
                }
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // 原图留档，供最终合成使用
                    Blitter.BlitCameraTexture(cmd, data.source, data.tempMainRT);

                    // Pass 0: SSL 生成（RAY3D 步进 / RBR2D 径向模糊）→ blurPing[0] (full res)
                    Blitter.BlitCameraTexture(cmd, data.source, data.blurPing[0], data.material, GeneratePassIndex);

                    // ── 多级模糊 ──
                    if (data.blurLevels > 0)
                    {
                        // Downsampling blur
                        for (int i = 0; i < data.blurLevels; i++)
                        {
                            int srcLvl = i;
                            int dstLvl = i + 1;
                            for (int j = 0; j < data.blurIterations; j++)
                            {
                                Blitter.BlitCameraTexture(cmd, data.blurPing[srcLvl], data.blurPong[dstLvl], data.material, BlurHorizontalPassIndex);
                                Blitter.BlitCameraTexture(cmd, data.blurPong[dstLvl], data.blurPing[dstLvl], data.material, BlurVerticalPassIndex);
                            }
                        }

                        // Upsampling blur
                        for (int i = data.blurLevels - 1; i >= 0; i--)
                        {
                            int srcLvl = i + 1;
                            int dstLvl = i;
                            for (int j = 0; j < data.blurIterations; j++)
                            {
                                Blitter.BlitCameraTexture(cmd, data.blurPing[srcLvl], data.blurPong[dstLvl], data.material, BlurHorizontalPassIndex);
                                Blitter.BlitCameraTexture(cmd, data.blurPong[dstLvl], data.blurPing[dstLvl], data.material, BlurVerticalPassIndex);
                            }
                        }
                    }

                    // 中间结果供其他 Shader 复用
                    cmd.SetGlobalTexture(Settings.SSLTexID, data.blurPing[0]);

                    // Pass 3: 合成 tempMain + SSL → screen
                    Blitter.BlitCameraTexture(cmd, data.tempMainRT, data.source, data.material, MixPassIndex);

                    // Debug: 用 SSL 中间结果顶掉最终输出
                    if (data.debug == DebugMode.Light)
                    {
                        Blitter.BlitCameraTexture(cmd, data.blurPing[0], data.source);
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
    SSLRenderPass sslPass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 材质随 Pass 一起重建与释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        sslPass?.Release();
        sslPass = settings.sslShader != null ? new SSLRenderPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        sslPass?.Release();
        sslPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (sslPass == null) return;
        renderer.EnqueuePass(sslPass);
    }
}
