using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class SSCFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader sscShader;
        public enum NoiseTex
        {
            TEX2DR,
            TEX2DRG,
            TEX3DXYZ
        }
        public NoiseTex noiseTex = NoiseTex.TEX2DR;

        public enum RayCount
        {
            COUNT64,
            COUNT128,
            COUNT256
        }
        public RayCount rayCount = RayCount.COUNT128;

        public Color baseColorA = Color.white;
        public Color baseColorB = Color.black;
        public Texture2D mainTex2D;
        public Texture3D mainTex3D;

        [Header("Cloud Shape")]
        [Range(0f, 100.0f)] public float scale = 1.0f;
        [Range(0f, 100.0f)] public float speed = 1.0f;
        [Range(0f, 100.0f)] public float height = 100.0f;
        [Range(0f, 360.0f)] public float rotation = 0.0f;
        [Range(0f, 1.000f)] public float thickness = 0.5f;
        [Range(0f, 1f)] public float middle = 0.5f;
        [Range(0f, 1f)] public float threshold = 0.2f;
        public Vector3 size = new Vector3(100, 100, 10);

        [Header("Scattering")]
        [Range(0f, 1f)] public float scatteringWeight = 0.0f;
        [Range(-1f, 1f)] public float phaseG = 0.8f;
        [Range(0f, 2f)] public float ambientIntensity = 1.0f;

        [Range(0f, 1f)] public float jitter = 1.0f;
        [Range(0, 4)] public int downsample = 0;
        public bool SSCFeature = true;

        internal static readonly int BaseColorAID = Shader.PropertyToID("_BaseColorA");
        internal static readonly int BaseColorBID = Shader.PropertyToID("_BaseColorB");
        internal static readonly int MainTex2DID = Shader.PropertyToID("_MainTex2D");
        internal static readonly int MainTex3DID = Shader.PropertyToID("_MainTex3D");
        internal static readonly int ParamAID = Shader.PropertyToID("_CloudParamA");
        internal static readonly int ParamBID = Shader.PropertyToID("_CloudParamB");
        internal static readonly int CloudMiddleID = Shader.PropertyToID("_CloudMiddle");
        internal static readonly int CloudThresholdID = Shader.PropertyToID("_CloudThreshold");
        internal static readonly int ScatteringWeightID = Shader.PropertyToID("_ScatteringWeight");
        internal static readonly int PhaseGID = Shader.PropertyToID("_PhaseG");
        internal static readonly int AmbientIntensityID = Shader.PropertyToID("_AmbientIntensity");
        internal static readonly int JitterID = Shader.PropertyToID("_Jitter");
    }

    class SSCRenderPass : ScriptableRenderPass
    {
        private Material sscMaterial;
        private Settings settings;

        class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle sscRT;
            public TextureHandle tempMainRT;
            public bool showSSC;
        }

        public SSCRenderPass(Shader shader, Settings s)
        {
            settings = s;
            if (shader != null)
                sscMaterial = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || sscMaterial == null) return;

            // ── 材质参数 ──
            sscMaterial.SetColor(Settings.BaseColorAID, settings.baseColorA);
            sscMaterial.SetColor(Settings.BaseColorBID, settings.baseColorB);
            sscMaterial.SetTexture(Settings.MainTex2DID, settings.mainTex2D);
            sscMaterial.SetTexture(Settings.MainTex3DID, settings.mainTex3D);
            sscMaterial.SetVector(Settings.ParamAID, new Vector4(settings.scale, settings.speed, settings.rotation, settings.height));
            sscMaterial.SetVector(Settings.ParamBID, new Vector4(settings.size.x, settings.size.y, settings.size.z, settings.thickness));
            sscMaterial.SetFloat(Settings.CloudMiddleID, settings.middle);
            sscMaterial.SetFloat(Settings.CloudThresholdID, settings.threshold);
            sscMaterial.SetFloat(Settings.ScatteringWeightID, settings.scatteringWeight);
            sscMaterial.SetFloat(Settings.PhaseGID, settings.phaseG);
            sscMaterial.SetFloat(Settings.AmbientIntensityID, settings.ambientIntensity);
            sscMaterial.SetFloat(Settings.JitterID, settings.jitter);

            // ── 关键字 ──
            sscMaterial.DisableKeyword("SSC_NOISE_TEX2DR");
            sscMaterial.DisableKeyword("SSC_NOISE_TEX2DRG");
            sscMaterial.DisableKeyword("SSC_NOISE_TEX3DXYZ");
            sscMaterial.DisableKeyword("SSC_RAY_COUNT_64");
            sscMaterial.DisableKeyword("SSC_RAY_COUNT_128");
            sscMaterial.DisableKeyword("SSC_RAY_COUNT_256");

            switch (settings.noiseTex)
            {
                case Settings.NoiseTex.TEX2DR: sscMaterial.EnableKeyword("SSC_NOISE_TEX2DR"); break;
                case Settings.NoiseTex.TEX2DRG: sscMaterial.EnableKeyword("SSC_NOISE_TEX2DRG"); break;
                case Settings.NoiseTex.TEX3DXYZ: sscMaterial.EnableKeyword("SSC_NOISE_TEX3DXYZ"); break;
            }
            switch (settings.rayCount)
            {
                case Settings.RayCount.COUNT64: sscMaterial.EnableKeyword("SSC_RAY_COUNT_64"); break;
                case Settings.RayCount.COUNT128: sscMaterial.EnableKeyword("SSC_RAY_COUNT_128"); break;
                case Settings.RayCount.COUNT256: sscMaterial.EnableKeyword("SSC_RAY_COUNT_256"); break;
            }

            // ── RT ──
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            TextureHandle tempMainRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSCTempMainRT", false);

            desc.width >>= settings.downsample;
            desc.height >>= settings.downsample;
            TextureHandle sscRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSCResultRT", false);

            sscMaterial.SetTexture("_SSCTex", sscRT);

            using (var builder = renderGraph.AddUnsafePass<PassData>("SSC", out var passData))
            {
                passData.material = sscMaterial;
                passData.source = source;
                passData.sscRT = sscRT;
                passData.tempMainRT = tempMainRT;
                passData.showSSC = settings.SSCFeature;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(sscRT, AccessFlags.ReadWrite);
                builder.UseTexture(tempMainRT, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // Copy source → tempMain for the composite input.
                    Blitter.BlitCameraTexture(cmd, data.source, data.tempMainRT);

                    // Pass 0: Ray march → sscRT
                    Blitter.BlitCameraTexture(cmd, data.source, data.sscRT, data.material, 0);

                    // Pass 1: Composite → screen
                    Blitter.BlitCameraTexture(cmd, data.tempMainRT, data.source, data.material, 1);

                    // Debug: show SSC result
                    if (data.showSSC)
                    {
                        Blitter.BlitCameraTexture(cmd, data.sscRT, data.source);
                    }
                });
            }
        }
    }

    public Settings settings = new Settings();
    SSCRenderPass sscPass;

    public override void Create()
    {
        sscPass = null;
        if (settings.sscShader != null)
            sscPass = new SSCRenderPass(settings.sscShader, settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (sscPass != null)
            renderer.EnqueuePass(sscPass);
    }
}
