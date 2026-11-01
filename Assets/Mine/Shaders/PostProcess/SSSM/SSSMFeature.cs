using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class SSSMFeature : ScriptableRendererFeature
{
    public enum BilateralIntensity { Low, Medium, High }

    [System.Serializable]
    public class Settings
    {
        public Shader sssmShader;

        [Header("Ray March")]
        [Range(0.1f, 2.0f)]  public float stepSize     = 0.5f;
        [Range(1f, 200f)]    public float maxDistance   = 50.0f;
        [Range(4, 128)]      public int   stepCount     = 32;
        [Range(0.001f, 0.5f)] public float thickness    = 0.05f;

        [Header("Blur")]
        public bool                enableBlur         = false;
        [Range(0.0f, 5.0f)]       public float blurScale           = 1.0f;
        public BilateralIntensity  bilateralIntensity = BilateralIntensity.Medium;
        public bool                bilateralNormal    = false;

        [Header("Event")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Debug")]
        public bool SSSMFeature = true;

        internal static readonly int StepSizeID      = Shader.PropertyToID("_StepSize");
        internal static readonly int MaxDistanceID   = Shader.PropertyToID("_MaxDistance");
        internal static readonly int StepCountID     = Shader.PropertyToID("_StepCount");
        internal static readonly int ThicknessID        = Shader.PropertyToID("_Thickness");
        internal static readonly int BlurScaleID     = Shader.PropertyToID("_BlurScale");
        internal static readonly int ShadowMaskTexID = Shader.PropertyToID("_SSSMTexture");
    }

    class SSSMPass : ScriptableRenderPass
    {
        private Material material;
        private Settings settings;

        class PassData
        {
            public Material     material;
            public TextureHandle source;
            public TextureHandle shadowRT;
            public TextureHandle blurRT;
            public bool         enableBlur;
            public bool         showShadow;
        }

        public SSSMPass(Shader shader, Settings s)
        {
            settings = s;
            if (shader != null)
                material = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = s.passEvent;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData   cameraData   = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || material == null) return;

            // ── 传递参数 ──
            material.SetFloat(Settings.StepSizeID,      settings.stepSize);
            material.SetFloat(Settings.MaxDistanceID,   settings.maxDistance);
            material.SetInt(Settings.StepCountID,       settings.stepCount);
            material.SetFloat(Settings.ThicknessID,        settings.thickness);
            material.SetFloat(Settings.BlurScaleID,     settings.blurScale);

            // ── 双边模糊关键字 ──
            material.DisableKeyword("BLUR_BILATERAL_LOW");
            material.DisableKeyword("BLUR_BILATERAL_MEDIUM");
            material.DisableKeyword("BLUR_BILATERAL_HIGH");
            switch (settings.bilateralIntensity)
            {
                case BilateralIntensity.Low:  material.EnableKeyword("BLUR_BILATERAL_LOW");    break;
                case BilateralIntensity.High: material.EnableKeyword("BLUR_BILATERAL_HIGH");   break;
                default:                      material.EnableKeyword("BLUR_BILATERAL_MEDIUM"); break;
            }
            if (settings.bilateralNormal)
                material.EnableKeyword("BLUR_BILATERAL_NORMAL");
            else
                material.DisableKeyword("BLUR_BILATERAL_NORMAL");

            // ── 创建 RT ──
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;

            TextureHandle shadowRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSSM_ShadowMask", false);
            TextureHandle blurRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSSM_BlurTemp", false);

            using (var builder = renderGraph.AddUnsafePass<PassData>("SSSM", out var passData))
            {
                passData.material    = material;
                passData.source     = source;
                passData.shadowRT   = shadowRT;
                passData.blurRT     = blurRT;
                passData.enableBlur = settings.enableBlur;
                passData.showShadow = settings.SSSMFeature;

                builder.UseTexture(source,   AccessFlags.ReadWrite);
                builder.UseTexture(shadowRT, AccessFlags.Write);
                builder.UseTexture(blurRT,   AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // ── Pass 0: DDA Ray March → 阴影遮罩 ──
                    Blitter.BlitCameraTexture(cmd, data.source, data.shadowRT, data.material, 0);

                    // ── Pass 1 & 2: 可选模糊 ──
                    if (data.enableBlur)
                    {
                        Blitter.BlitCameraTexture(cmd, data.shadowRT, data.blurRT, data.material, 1);
                        Blitter.BlitCameraTexture(cmd, data.blurRT, data.shadowRT, data.material, 2);
                    }

                    // ── 始终设置全局纹理，供其他 Shader 使用 ──
                    cmd.SetGlobalTexture(Settings.ShadowMaskTexID, data.shadowRT);

                    // ── Debug：显示阴影图 ──
                    if (data.showShadow)
                    {
                        Blitter.BlitCameraTexture(cmd, data.shadowRT, data.source);
                    }
                });
            }
        }
    }

    public Settings settings = new Settings();
    private SSSMPass sssmPass;

    public override void Create()
    {
        if (sssmPass != null)
            sssmPass = null;

        if (settings.sssmShader == null) return;
        sssmPass = new SSSMPass(settings.sssmShader, settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (sssmPass == null) return;
        sssmPass.renderPassEvent = settings.passEvent;
        renderer.EnqueuePass(sssmPass);
    }
}
