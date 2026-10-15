using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSCFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader sscShader;
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

        [Range(0f, 100.0f)] public float scale = 1.0f;
        [Range(0f, 100.0f)] public float speed = 1.0f;
        [Range(0f, 100.0f)] public float height = 100.0f;
        [Range(0f, 1.000f)] public float middle = 0.5f;
        [Range(0f, 360.0f)] public float rotation = 0.0f;
        [Range(0f, 1.000f)] public float thickness = 0.5f;
        [Range(0f, 1.000f)] public float threshold = 0.2f;
        public Vector3 size = new Vector3(100, 50, 100);

        [Range(0f, 1f)] public float jitter = 1.0f;
        [Range(0 , 4 )] public int downsample = 0;
        [Range(-1f, 1f)] public float phaseG = 0.8f;
        [Range(0f, 2f)] public float ambientIntensity = 1.0f;
        public bool SSCFeature = true;

        internal static readonly int BaseColorAID = Shader.PropertyToID("_BaseColorA");
        internal static readonly int BaseColorBID = Shader.PropertyToID("_BaseColorB");
        internal static readonly int MainTex2DID = Shader.PropertyToID("_MainTex2D");
        internal static readonly int CloudSizeID = Shader.PropertyToID("_CloudSize");
        internal static readonly int CloudScaleID = Shader.PropertyToID("_CloudScale");
        internal static readonly int CloudSpeedID = Shader.PropertyToID("_CloudSpeed");
        internal static readonly int CloudRotationID = Shader.PropertyToID("_CloudRotation");
        internal static readonly int CloudHeightID = Shader.PropertyToID("_CloudHeight");
        internal static readonly int CloudMiddleID = Shader.PropertyToID("_CloudMiddle");
        internal static readonly int CloudThicknessID = Shader.PropertyToID("_CloudThickness");
        internal static readonly int CloudThresholdID = Shader.PropertyToID("_CloudThreshold");
        internal static readonly int JitterID = Shader.PropertyToID("_Jitter");
        internal static readonly int PhaseGID = Shader.PropertyToID("_PhaseG");
        internal static readonly int AmbientIntensityID = Shader.PropertyToID("_AmbientIntensity");
    }

    class SSCRenderPass : ScriptableRenderPass
    {
        private Material sscMaterial;
        private Settings settings;
        private RTHandle sscRT;
        private RTHandle tempMainRT;

        public void ReleaseRT()
        {
            sscRT?.Release();
            tempMainRT?.Release();

            sscRT = null;
            tempMainRT = null;
        }

        public SSCRenderPass(Shader shader, Settings s)
        {
            settings = s;
            sscMaterial = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents; 
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (sscMaterial == null) return;
            var cmd = CommandBufferPool.Get("SSC");
            var cameraData = renderingData.cameraData;

            sscMaterial.SetColor(Settings.BaseColorAID, settings.baseColorA);
            sscMaterial.SetColor(Settings.BaseColorBID, settings.baseColorB);
            sscMaterial.SetTexture(Settings.MainTex2DID, settings.mainTex2D);
            sscMaterial.SetVector(Settings.CloudSizeID, settings.size);
            sscMaterial.SetFloat(Settings.CloudScaleID, settings.scale);
            sscMaterial.SetFloat(Settings.CloudSpeedID, settings.speed);
            sscMaterial.SetFloat(Settings.CloudRotationID, settings.rotation);
            sscMaterial.SetFloat(Settings.CloudHeightID, settings.height);
            sscMaterial.SetFloat(Settings.CloudMiddleID, settings.middle);
            sscMaterial.SetFloat(Settings.CloudThicknessID, settings.thickness);
            sscMaterial.SetFloat(Settings.CloudThresholdID, settings.threshold);
            sscMaterial.SetFloat(Settings.JitterID, settings.jitter);
            sscMaterial.SetFloat(Settings.PhaseGID, settings.phaseG);
            sscMaterial.SetFloat(Settings.AmbientIntensityID, settings.ambientIntensity);

            sscMaterial.DisableKeyword("SSC_RAY_COUNT_64");
            sscMaterial.DisableKeyword("SSC_RAY_COUNT_128");
            sscMaterial.DisableKeyword("SSC_RAY_COUNT_256");
            
            if (settings.rayCount == Settings.RayCount.COUNT64)
            {
                sscMaterial.EnableKeyword("SSC_RAY_COUNT_64");
            }
            else if (settings.rayCount == Settings.RayCount.COUNT128)
            {
                sscMaterial.EnableKeyword("SSC_RAY_COUNT_128");
            }
            else if (settings.rayCount == Settings.RayCount.COUNT256)
            {
                sscMaterial.EnableKeyword("SSC_RAY_COUNT_256");
            }

            Render(cmd, ref renderingData);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void Render(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            var source = renderer.cameraColorTargetHandle;
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref tempMainRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSCTempMainRT");
            sscMaterial.SetTexture("_MainTex", tempMainRT);
            desc.width  >>= settings.downsample;
            desc.height >>= settings.downsample;
            RenderingUtils.ReAllocateIfNeeded(ref sscRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSCResultRT");
            sscMaterial.SetTexture("_SSCTex", sscRT);

            cmd.Blit(source, tempMainRT);
            cmd.Blit(null, sscRT, sscMaterial, 0);
            cmd.Blit(tempMainRT, renderer.cameraColorTargetHandle.nameID, sscMaterial, 1);

            if (settings.SSCFeature)
            {    
                cmd.Blit(sscRT, renderer.cameraColorTargetHandle.nameID);
            }
        }
    }

    public Settings settings = new Settings();
    SSCRenderPass sscPass;

    public override void Create()
    {

        if (sscPass != null)
        {
            sscPass.ReleaseRT();
            sscPass = null;
        }

        if (settings.sscShader != null)
        {
            sscPass = new SSCRenderPass(settings.sscShader, settings);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (sscPass != null)
        {
            renderer.EnqueuePass(sscPass);
        }
    }
}
