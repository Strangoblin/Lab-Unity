// ════════════════════════════════════════════════════════════
//  Kuwahara Feature — basic / generalized / anisotropic filters
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 桑原滤波：三种模式共用参数，按模式选择单 Pass（Basic / Generalized）
/// 或四 Pass 张量链（Anisotropic）。算法与 Pass 顺序见 Kuwahara.shader。
/// </summary>
public sealed class KuwaharaFeature : ScriptableRendererFeature
{
    public enum KuwaharaType { Basic, Generalized, Anisotropic }

    /// <summary>纯成本档位：一次决定采样预算与张量链下采样率。</summary>
    public enum QualityMode
    {
        Low,
        Medium,
        High
    }

    public enum DebugMode
    {
        Off,
        StructureTensor
    }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader kuwaharaShader;
        public KuwaharaType kuwaharaType = KuwaharaType.Basic;

        [Header("Performance")]
        [Tooltip("Low = 9 点、张量降采样 2；Medium = 16 点、降采样 1；High = 25 点、降采样 0。"
                 + "默认 High 逐字段复现整理前的面板值 (SampleQuality=High, Downsample=0)。")]
        public QualityMode quality = QualityMode.High;

        [Header("Artistic")]
        [Range(1, 10)] public int Radius = 5;
        [Range(1.0f, 18.0f)] public float sharpness = 8.0f;
        [Range(1.0f, 100.0f)] public float hardness = 8.0f;
        [Range(1.0f, 2000.0f)] public float weightScale = 1000.0f;
        [Range(0.01f, 2.0f)] public float alpha = 1.0f;

        [Header("Debug")]
        [Tooltip("Off = 正常输出；StructureTensor = 输出特征向量场（RG 方向, B 角度, A 各向异性）。"
                 + "仅 Anisotropic 模式的张量链会生成该中间纹理，Basic / Generalized 下不生效。")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int RadiusID        = Shader.PropertyToID("_Radius");
        internal static readonly int SampleQualityID = Shader.PropertyToID("_SampleQuality");
        internal static readonly int QID             = Shader.PropertyToID("_Q");
        internal static readonly int HardnessID      = Shader.PropertyToID("_Hardness");
        internal static readonly int WeightScaleID   = Shader.PropertyToID("_WeightScale");
        internal static readonly int AlphaID         = Shader.PropertyToID("_Alpha");
        internal static readonly int BlurScaleID     = Shader.PropertyToID("_BlurScale");
        internal static readonly int TensorTexID     = Shader.PropertyToID("_TFM");

        // ════════════════════════════════════════════════════════════
        //  档位表 — 纯函数展开，无共享可变状态
        //
        //  sampleQuality 是 shader 侧 _SampleQuality 的取值（0=9 点, 1=16 点, 2=25 点）
        // ════════════════════════════════════════════════════════════
        public static void ResolveQuality(QualityMode quality,
            out int sampleQuality, out int downsampleLevel)
        {
            switch (quality)
            {
                case QualityMode.Low:
                    sampleQuality = 0; downsampleLevel = 2;
                    break;
                case QualityMode.Medium:
                    sampleQuality = 1; downsampleLevel = 1;
                    break;
                default:
                    sampleQuality = 2; downsampleLevel = 0;
                    break;
            }
        }
    }

    sealed class KuwaharaPass : ScriptableRenderPass
    {
        // ── Basic / Generalized 共用数据 ──
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle tempRT;
            public int passIndex;
        }

        // ── Aniso 多 Pass 数据 ──
        sealed class AnisoPassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle tensorRT0;
            public TextureHandle tensorRT1;
            public TextureHandle tensorRT2;
            public TextureHandle outputRT;
            public DebugMode debug;
        }

        // Shader Pass 顺序：0 = Basic, 1 = Generalized, 2 = Tensor, 3 = BlurH, 4 = Eigen, 5 = AnisoFilter
        const int BasicPassIndex          = 0;
        const int GeneralizedPassIndex    = 1;
        const int StructureTensorPassIndex = 2;
        const int BlurHorizontalPassIndex  = 3;
        const int EigenDecompPassIndex     = 4;
        const int AnisoFilterPassIndex     = 5;

        // 单 Pass 模式下 Generalized 的权重陡度由 C# 固定
        const float GeneralizedQ = 2.0f;

        readonly Settings _settings;
        readonly Material _material;

        public KuwaharaPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.kuwaharaShader);
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        // ════════════════════════════════════════════════════════════
        //  RecordRenderGraph — 按模式分发到单 Pass 或张量链
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || _material == null) return;

            // ── 质量档展开（纯函数，不缓存状态） ──
            Settings.ResolveQuality(_settings.quality,
                out int sampleQuality, out int downsampleLevel);

            switch (_settings.kuwaharaType)
            {
                case KuwaharaType.Anisotropic:
                    RecordAnisotropic(renderGraph, source, cameraData, sampleQuality, downsampleLevel);
                    break;
                default:
                    RecordSinglePass(renderGraph, source, cameraData, sampleQuality);
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  单 Pass 路径 — Basic (Pass 0) / Generalized (Pass 1)
        //
        //  该路径没有独立中间结果，DebugMode.StructureTensor 不生效
        // ════════════════════════════════════════════════════════════
        void RecordSinglePass(RenderGraph renderGraph, TextureHandle source,
            UniversalCameraData cameraData, int sampleQuality)
        {
            int passIndex = _settings.kuwaharaType == KuwaharaType.Basic
                ? BasicPassIndex
                : GeneralizedPassIndex;

            _material.SetInt(Settings.RadiusID, _settings.Radius);
            _material.SetInt(Settings.SampleQualityID, sampleQuality);
            // 单 Pass（Basic/Generalized）：Q 仅 Generalized 生效
            // Q=2 使方差线性衰减；WeightScale 控制敏感度
            _material.SetFloat(Settings.QID, GeneralizedQ);
            _material.SetFloat(Settings.WeightScaleID, _settings.weightScale);
            _material.SetFloat(Settings.HardnessID, _settings.hardness);

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            TextureHandle tempRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_KuwaharaTempRT", false);

            using (var builder = renderGraph.AddUnsafePass<PassData>("Kuwahara Pass", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.tempRT = tempRT;
                passData.passIndex = passIndex;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(tempRT, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(cmd, data.source, data.tempRT, data.material, data.passIndex);
                    Blitter.BlitCameraTexture(cmd, data.tempRT, data.source);
                });
            }
        }

        // ════════════════════════════════════════════════════════════
        //  张量链路径 — StructureTensor → BlurH → Eigen → AnisoFilter
        // ════════════════════════════════════════════════════════════
        void RecordAnisotropic(RenderGraph renderGraph, TextureHandle source,
            UniversalCameraData cameraData, int sampleQuality, int downsampleLevel)
        {
            _material.SetFloat(Settings.BlurScaleID, downsampleLevel + 1.0f);
            _material.SetInt(Settings.RadiusID, _settings.Radius);
            _material.SetInt(Settings.SampleQualityID, sampleQuality);
            _material.SetFloat(Settings.QID, _settings.sharpness);
            _material.SetFloat(Settings.HardnessID, _settings.hardness);
            _material.SetFloat(Settings.AlphaID, _settings.alpha);
            _material.SetFloat(Settings.WeightScaleID, _settings.weightScale);

            RenderTextureDescriptor fullDesc = cameraData.cameraTargetDescriptor;
            fullDesc.depthBufferBits = 0;

            RenderTextureDescriptor lowDesc = fullDesc;
            int shift = Mathf.Clamp(downsampleLevel, 0, 4);
            lowDesc.width = Mathf.Max(1, fullDesc.width >> shift);
            lowDesc.height = Mathf.Max(1, fullDesc.height >> shift);

            TextureHandle t0 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fullDesc, "_TensorRT0", false);
            TextureHandle t1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, lowDesc, "_TensorRT1", false);
            TextureHandle t2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, lowDesc, "_TensorRT2", false);
            TextureHandle outRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fullDesc, "_KuwaharaOutput", false);

            using (var builder = renderGraph.AddUnsafePass<AnisoPassData>("KuwaharaAniso", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.tensorRT0 = t0;
                passData.tensorRT1 = t1;
                passData.tensorRT2 = t2;
                passData.outputRT = outRT;
                passData.debug = _settings.debug;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(t0, AccessFlags.Write);
                builder.UseTexture(t1, AccessFlags.Write);
                builder.UseTexture(t2, AccessFlags.ReadWrite);
                builder.UseTexture(outRT, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((AnisoPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // Pass 2: Structure Tensor (ddx/ddy)
                    Blitter.BlitCameraTexture(cmd, data.source, data.tensorRT0, data.material, StructureTensorPassIndex);
                    // Pass 3: Horizontal Blur
                    Blitter.BlitCameraTexture(cmd, data.tensorRT0, data.tensorRT1, data.material, BlurHorizontalPassIndex);
                    // Pass 4: Vertical Blur + Eigen
                    Blitter.BlitCameraTexture(cmd, data.tensorRT1, data.tensorRT2, data.material, EigenDecompPassIndex);

                    // Pass 5: Anisotropic Filter
                    cmd.SetGlobalTexture(Settings.TensorTexID, data.tensorRT2);
                    Blitter.BlitCameraTexture(cmd, data.source, data.outputRT, data.material, AnisoFilterPassIndex);

                    // Debug: 用特征向量场顶掉最终输出
                    if (data.debug == DebugMode.StructureTensor)
                    {
                        Blitter.BlitCameraTexture(cmd, data.tensorRT2, data.source);
                        return;
                    }

                    Blitter.BlitCameraTexture(cmd, data.outputRT, data.source);
                });
            }
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
        }
    }

    public Settings settings = new Settings();
    KuwaharaPass kuwaharaPass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 材质随 Pass 一起重建与释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        kuwaharaPass?.Release();
        kuwaharaPass = settings.kuwaharaShader != null ? new KuwaharaPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        kuwaharaPass?.Release();
        kuwaharaPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (kuwaharaPass == null) return;
        renderer.EnqueuePass(kuwaharaPass);
    }
}
