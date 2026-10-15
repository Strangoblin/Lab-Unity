using System.Data;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSLFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader sslShader;
        [Range(1, 256)] public int   maxSteps = 32;
        [Range(0.1f, 100f)] public float maxDistance = 10f;
        [Range(0f, 5f)] public float intensity = 1f;
        [Range(0f, 2f)] public float sslScale = 0.5f;
        [Range(0f, 1f)] public float jitterScale = 0.5f;
        [Range(0f, 5f)] public float blurScale = 0.5f;
        [Range(0, 4)]   public int blurLevels = 1;
        [Range(0, 4)]   public int blurIterations = 1;
        public bool SSLFeature = true;
    }

    class SSLRenderPass : ScriptableRenderPass
    {
        private Material sslMaterial;
        private Settings settings;
        private RenderTargetHandle sslBlurRT1;
        private RenderTargetHandle sslBlurRT2;
        private RenderTargetHandle tempMainRT;
        public SSLRenderPass(Shader shader, Settings s)
        {
            settings = s;
            sslBlurRT1.Init("_SSLBlurRT1");
            sslBlurRT2.Init("_SSLBlurRT2");
            tempMainRT.Init("_SSLTempMainRT");
            sslMaterial = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (sslMaterial == null) return;
            var cmd = CommandBufferPool.Get("SSL");
            var stack = VolumeManager.instance.stack;
            var vol = stack.GetComponent<SSLVolume>();
            if (vol == null || !vol.IsActive() || !renderingData.cameraData.postProcessEnabled)
            {
                CommandBufferPool.Release(cmd);
                return;
            }

            sslMaterial.SetInt("_MaxSteps", vol.maxSteps.value);
            sslMaterial.SetFloat("_MaxDistance", vol.maxDistance.value);
            sslMaterial.SetFloat("_Intensity", vol.intensity.value);
            sslMaterial.SetFloat("_SSLScale", vol.sslScale.value);
            sslMaterial.SetFloat("_BlurScale", vol.blurScale.value);
            sslMaterial.SetFloat("_JitterScale", vol.jitterScale.value);

            sslMaterial.DisableKeyword("SSL_FOG");
            sslMaterial.DisableKeyword("SSL_LIGHT");
            if (vol.sslType == SSLVolume.SSLType.Fog)
            {
                sslMaterial.EnableKeyword("SSL_FOG");
            }
            else if (vol.sslType == SSLVolume.SSLType.Light)
            {
                sslMaterial.EnableKeyword("SSL_LIGHT");
            }

            Render(cmd, ref renderingData);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void Render(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            var desc = renderingData.cameraData.cameraTargetDescriptor;

            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;

            cmd.GetTemporaryRT(sslBlurRT1.id, desc.width >>1, desc.height >>1);
            cmd.GetTemporaryRT(sslBlurRT2.id, desc.width >>1, desc.height >>1);
            cmd.GetTemporaryRT(tempMainRT.id, desc.width, desc.height, 0, FilterMode.Trilinear, RenderTextureFormat.Default);

            cmd.Blit(renderer.cameraColorTargetHandle.nameID, tempMainRT.id);
            cmd.Blit(null, sslBlurRT1.Identifier(), sslMaterial, 0);

            // downsampling blur
            for (int i = 0; i < settings.blurLevels; i++)
            {
                int downsampledWidth = Mathf.Max(1, desc.width >> i);
                int downsampledHeight = Mathf.Max(1, desc.height >> i);
                for (int j = 0; j < settings.blurIterations; j++)
                {
                    cmd.Blit(sslBlurRT1.Identifier(), sslBlurRT2.Identifier(), sslMaterial, 1);
                    cmd.ReleaseTemporaryRT(sslBlurRT1.id);
                    cmd.GetTemporaryRT(sslBlurRT1.id, downsampledWidth, downsampledHeight);
                    cmd.Blit(sslBlurRT2.Identifier(), sslBlurRT1.Identifier(), sslMaterial, 2);
                    cmd.ReleaseTemporaryRT(sslBlurRT2.id);
                    cmd.GetTemporaryRT(sslBlurRT2.id, downsampledWidth, downsampledHeight);
                }
            }
            
            // upsampling blur
            for (int i = settings.blurLevels - 1; i >= 0; i--)
            {
                int upsampledWidth = Mathf.Max(1, desc.width >> i);
                int upsampledHeight = Mathf.Max(1, desc.height >> i);
                for (int j = 0; j < settings.blurIterations; j++)
                {
                    cmd.Blit(sslBlurRT1.Identifier(), sslBlurRT2.Identifier(), sslMaterial, 1);
                    cmd.ReleaseTemporaryRT(sslBlurRT1.id);
                    cmd.GetTemporaryRT(sslBlurRT1.id, upsampledWidth, upsampledHeight);
                    cmd.Blit(sslBlurRT2.Identifier(), sslBlurRT1.Identifier(), sslMaterial, 2);
                    cmd.ReleaseTemporaryRT(sslBlurRT2.id);
                    cmd.GetTemporaryRT(sslBlurRT2.id, upsampledWidth, upsampledHeight);
                }
            }

            // Texture Output
            cmd.SetGlobalTexture("_SSLResultTex", sslBlurRT1.id);

            cmd.Blit(tempMainRT.id, renderer.cameraColorTargetHandle.nameID, sslMaterial, 3);

            if (settings.SSLFeature)
            {
                cmd.Blit(sslBlurRT1.Identifier(), renderer.cameraColorTargetHandle.nameID);
            }

            cmd.ReleaseTemporaryRT(sslBlurRT1.id);
            cmd.ReleaseTemporaryRT(sslBlurRT2.id);
            cmd.ReleaseTemporaryRT(tempMainRT.id);
        }
    }

    public Settings settings = new Settings();
    SSLRenderPass sslPass;

    public override void Create()
    {
        if (settings.sslShader != null)
        {
            sslPass = new SSLRenderPass(settings.sslShader, settings);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (sslPass != null)
        {
            renderer.EnqueuePass(sslPass);
        }
    }
}