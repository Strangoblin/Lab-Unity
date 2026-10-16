// ════════════════════════════════════════════════════════════
//  DDOF Feature — dynamic depth of field
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 动态景深：由场景深度生成 CoC 遮罩，按遮罩对全屏颜色做水平 / 垂直模糊后合成。
/// 管线为 CoC → BlurH → BlurV → Composite 四步，均在 DDOF.shader 的对应 Pass 中实现。
/// </summary>
public sealed class DDOFFeature : ScriptableRendererFeature
{
    public enum DebugMode
    {
        Off,
        CoC,
        Blur
    }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader ddofShader;

        [Header("Artistic")]
        [Range(0f, 50f)] public float focusRange = 2.0f;
        [Range(0f, 5f)] public float blurScale = 2.5f;

        [Header("Debug")]
        [Tooltip("Off = 正常合成；CoC = 输出失焦遮罩；Blur = 输出模糊结果。Debug 分支复用本 Pass 已生成的中间纹理。")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int FocusRangeID   = Shader.PropertyToID("_FocusRange");
        internal static readonly int BlurScaleID    = Shader.PropertyToID("_BlurScale");
        internal static readonly int TempMainTexID  = Shader.PropertyToID("_DDOFTempMainTex");
        internal static readonly int CoCTexID       = Shader.PropertyToID("_DDOFCoCTex");
    }

    sealed class DDOFPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle maskRT;
            public TextureHandle tempRT;
            public TextureHandle blur1RT;
            public TextureHandle blur2RT;
            public DebugMode debug;
        }

        // Shader Pass 顺序：0 = CoC, 1 = BlurH, 2 = BlurV, 3 = Composite
        const int CoCPassIndex          = 0;
        const int BlurHorizontalPassIndex = 1;
        const int BlurVerticalPassIndex   = 2;
        const int CompositePassIndex      = 3;

        readonly Settings _settings;
        readonly Material _material;

        public DDOFPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.ddofShader);
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        // ════════════════════════════════════════════════════════════
        //  RecordRenderGraph — CoC 遮罩、双向模糊、合成，RT 由 RenderGraph 管理
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || _material == null) return;

            _material.SetFloat(Settings.FocusRangeID, _settings.focusRange);
            _material.SetFloat(Settings.BlurScaleID, _settings.blurScale);

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            TextureHandle maskRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_DDOFMaskRT", false);
            TextureHandle tempRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_DDOFTempMainRT", false);
            TextureHandle blur1RT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_DDOFBlur1RT", false);
            TextureHandle blur2RT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_DDOFBlur2RT", false);

            using (var builder = renderGraph.AddUnsafePass<PassData>("DDOF", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.maskRT = maskRT;
                passData.tempRT = tempRT;
                passData.blur1RT = blur1RT;
                passData.blur2RT = blur2RT;
                passData.debug = _settings.debug;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(maskRT, AccessFlags.ReadWrite);
                builder.UseTexture(tempRT, AccessFlags.ReadWrite);
                builder.UseTexture(blur1RT, AccessFlags.ReadWrite);
                builder.UseTexture(blur2RT, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // 原图留档，供最终合成使用
                    Blitter.BlitCameraTexture(cmd, data.source, data.tempRT);
                    cmd.SetGlobalTexture(Settings.TempMainTexID, data.tempRT);

                    // Pass 0: CoC 遮罩
                    Blitter.BlitCameraTexture(cmd, data.source, data.maskRT, data.material, CoCPassIndex);
                    cmd.SetGlobalTexture(Settings.CoCTexID, data.maskRT);

                    // Debug: 遮罩就绪，省掉后续模糊
                    if (data.debug == DebugMode.CoC)
                    {
                        Blitter.BlitCameraTexture(cmd, data.maskRT, data.source);
                        return;
                    }

                    // Pass 1 & 2: 水平 → 垂直模糊
                    Blitter.BlitCameraTexture(cmd, data.source, data.blur1RT, data.material, BlurHorizontalPassIndex);
                    Blitter.BlitCameraTexture(cmd, data.blur1RT, data.blur2RT, data.material, BlurVerticalPassIndex);

                    // Debug: 模糊结果就绪，省掉合成
                    if (data.debug == DebugMode.Blur)
                    {
                        Blitter.BlitCameraTexture(cmd, data.blur2RT, data.source);
                        return;
                    }

                    // Pass 3: 合成到屏幕
                    Blitter.BlitCameraTexture(cmd, data.blur2RT, data.source, data.material, CompositePassIndex);
                });
            }
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
        }
    }

    public Settings settings = new Settings();
    DDOFPass ddofPass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 材质随 Pass 一起重建与释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        ddofPass?.Release();
        ddofPass = settings.ddofShader != null ? new DDOFPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        ddofPass?.Release();
        ddofPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ddofPass == null) return;
        renderer.EnqueuePass(ddofPass);
    }
}
