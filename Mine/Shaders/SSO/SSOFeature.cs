using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSOFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader ssoShader;
        [System.Serializable]
        public class Params
        {        
            [Range(0f, 1f)] public float intensity = 1.0f;
            [Range(0f, 5f)] public float thickness = 1;
            public Vector2 threshold = new Vector2(0.1f, 0.2f);
        }

        public Params colorParams  = new Params();
        public Params depthParams  = new Params();
        public Params normalParams = new Params();

        public enum SSOType
        {
            Basic,
            Sobel
        }
        public SSOType ssoType = SSOType.Basic;
        [Range(0f, 1f)] public float jitter = 1.0f;
        public bool SSOFeature = true;

        internal static readonly int ColorIntensityID = Shader.PropertyToID("_ColorIntensity");
        internal static readonly int ColorThicknessID = Shader.PropertyToID("_ColorThickness");
        internal static readonly int ColorThresholdID = Shader.PropertyToID("_ColorThreshold");

        internal static readonly int DepthIntensityID = Shader.PropertyToID("_DepthIntensity");
        internal static readonly int DepthThicknessID = Shader.PropertyToID("_DepthThickness");
        internal static readonly int DepthThresholdID = Shader.PropertyToID("_DepthThreshold");

        internal static readonly int NormalIntensityID = Shader.PropertyToID("_NormalIntensity");
        internal static readonly int NormalThicknessID = Shader.PropertyToID("_NormalThickness");
        internal static readonly int NormalThresholdID = Shader.PropertyToID("_NormalThreshold");

        internal static readonly int JitterID = Shader.PropertyToID("_Jitter");
    }

    class SSORenderPass : ScriptableRenderPass
    {
        private Material ssoMaterial;
        private Settings settings;
        private RenderTargetHandle ssoRT;

        public SSORenderPass(Shader shader, Settings s)
        {
            settings = s;
            ssoRT.Init("_SSOResultRT");
            ssoMaterial = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents; 
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ssoMaterial == null) return;
            var cmd = CommandBufferPool.Get("SSO");
            var cameraData = renderingData.cameraData;

            ssoMaterial.SetFloat(Settings.ColorIntensityID, settings.colorParams.intensity);
            ssoMaterial.SetFloat(Settings.ColorThicknessID, settings.colorParams.thickness);
            ssoMaterial.SetVector(Settings.ColorThresholdID, settings.colorParams.threshold);

            ssoMaterial.SetFloat(Settings.DepthIntensityID, settings.depthParams.intensity);
            ssoMaterial.SetFloat(Settings.DepthThicknessID, settings.depthParams.thickness);
            ssoMaterial.SetVector(Settings.DepthThresholdID, settings.depthParams.threshold);

            ssoMaterial.SetFloat(Settings.NormalIntensityID, settings.normalParams.intensity);
            ssoMaterial.SetFloat(Settings.NormalThicknessID, settings.normalParams.thickness);
            ssoMaterial.SetVector(Settings.NormalThresholdID, settings.normalParams.threshold);
            ssoMaterial.SetFloat(Settings.JitterID, settings.jitter);

            ssoMaterial.DisableKeyword("SSO_Basic");
            ssoMaterial.DisableKeyword("SSO_Sobel");
            if (settings.ssoType == Settings.SSOType.Basic)
            {
                ssoMaterial.EnableKeyword("SSO_Basic");
            }
            else if (settings.ssoType == Settings.SSOType.Sobel)
            {
                ssoMaterial.EnableKeyword("SSO_Sobel");
            }

            Render(cmd, ref renderingData);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void Render(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            var source = renderer.cameraColorTarget;
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            cmd.GetTemporaryRT(ssoRT.id, desc, FilterMode.Bilinear);

            cmd.Blit(null, ssoRT.Identifier(), ssoMaterial, 0);

            if (settings.SSOFeature)
            {    
                cmd.Blit(ssoRT.Identifier(), renderer.cameraColorTargetHandle.nameID);
            }
            cmd.ReleaseTemporaryRT(ssoRT.id);
        }
    }

    public Settings settings = new Settings();
    SSORenderPass ssoPass;

    public override void Create()
    {
        if (settings.ssoShader != null)
        {
            ssoPass = new SSORenderPass(settings.ssoShader, settings);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ssoPass != null)
        {
            renderer.EnqueuePass(ssoPass);
        }
    }
}