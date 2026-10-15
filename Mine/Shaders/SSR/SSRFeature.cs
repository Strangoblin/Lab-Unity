using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSRFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader ssrShader;
        [Range(0.1f, 2f)] public float stepSize = 0.2f;
        [Range(1f, 200f)] public float maxDistance = 50f;

        [Range(8, 256)] public int stepCount = 64;
        [Range(0, 32)] public int binaryCount = 6;
        [Range(1, 8)] public int mipCount = 4;

        [Range(0.001f, 0.5f)] public float thickness = 0.05f;
        [Range(0f, 1f)] public float smoothness = 1f;
        [Range(0f, 0.1f)] public float jitterScale = 0.01f;
        [Range(0f, 5f)] public float blurScale = 0.5f;


        public bool SSRFeature = true;

        public enum SSRType
        {
            HiZ2D,
            DDA2D,
            Ray3D
        }
        public SSRType ssrType = SSRType.DDA2D;
    }

    class SSRRenderPass : ScriptableRenderPass
    {
        private Material ssrMaterial;
        private Settings settings;
        private RenderTargetHandle ssrRT;
        private RenderTargetHandle blur1RT;
        private RenderTargetHandle blur2RT;
        private RTHandle mHiZRT;
        private RTHandle[] mHiZRTs;
        private RenderTextureDescriptor mHiZDesc;

        public SSRRenderPass(Shader shader, Settings s)
        {
            settings = s;
            ssrRT.Init("_SSRResultRT");
            blur1RT.Init("_SSRBlur1RT");
            blur2RT.Init("_SSRBlur2RT");
            mHiZRTs = new RTHandle[settings.mipCount];
            ssrMaterial = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents; 
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            // mHiZ Setup
            var renderer = renderingData.cameraData.renderer;
            var desc     = renderingData.cameraData.cameraTargetDescriptor;

            var width  = Mathf.Max((int)Mathf.Ceil(Mathf.Log(desc.width, 2) - 1.0f), 1);
            var height = Mathf.Max((int)Mathf.Ceil(Mathf.Log(desc.height, 2) - 1.0f), 1);
            width  = 1 << width;
            height = 1 << height;
            // mip 0
            mHiZDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RFloat, 0, settings.mipCount);
            mHiZDesc.sRGB = false;
            mHiZDesc.useMipMap = true;
            mHiZDesc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref mHiZRT, mHiZDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSRmHiZRT");
            // other mips
            RenderTextureDescriptor[] mHiZDescs = new RenderTextureDescriptor[settings.mipCount];
            for (int i = 0; i < settings.mipCount; i++)
            {
                mHiZDescs[i] = new RenderTextureDescriptor(Mathf.Max(1, width >> i), Mathf.Max(1, height >> i), RenderTextureFormat.RFloat, 0, 1);
                mHiZDescs[i].sRGB = false;
                mHiZDescs[i].useMipMap = false;
                mHiZDescs[i].msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref mHiZRTs[i], mHiZDescs[i], FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSRmHiZRT_Mip" + i);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ssrMaterial == null) return;
            var cmd = CommandBufferPool.Get("SSR");
            var cameraData = renderingData.cameraData;

            ssrMaterial.SetFloat("_StepSize", settings.stepSize);
            ssrMaterial.SetFloat("_MaxDistance", settings.maxDistance);

            ssrMaterial.SetFloat("_Thickness", settings.thickness);
            ssrMaterial.SetFloat("_Smoothness", settings.smoothness);
            ssrMaterial.SetFloat("_JitterScale", settings.jitterScale);
            ssrMaterial.SetFloat("_BlurScale", settings.blurScale);

            ssrMaterial.SetInt("_StepCount", settings.stepCount);
            ssrMaterial.SetInt("_BinaryCount", settings.binaryCount);

            ssrMaterial.DisableKeyword("SSR_DDA2D");
            ssrMaterial.DisableKeyword("SSR_RAY3D");
            ssrMaterial.DisableKeyword("SSR_HIZ2D");
            if (settings.ssrType == Settings.SSRType.DDA2D)
            {
                ssrMaterial.EnableKeyword("SSR_DDA2D");
            }
            else if (settings.ssrType == Settings.SSRType.Ray3D)
            {
                ssrMaterial.EnableKeyword("SSR_RAY3D");
            }
            else if (settings.ssrType == Settings.SSRType.HiZ2D)
            {
                ssrMaterial.EnableKeyword("SSR_HIZ2D");
            }

            Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
            Matrix4x4 projectionMatrix = cameraData.GetGPUProjectionMatrix();
            
            ssrMaterial.SetMatrix("_CameraViewMatrix", viewMatrix);
            ssrMaterial.SetMatrix("_CameraProjectionMatrix", projectionMatrix);
            
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

            cmd.GetTemporaryRT(ssrRT.id, desc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(blur1RT.id, desc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(blur2RT.id, desc, FilterMode.Bilinear);

            // mHiz generation
            cmd.Blit(renderer.cameraDepthTargetHandle.nameID, mHiZRTs[0].nameID);
            cmd.CopyTexture(mHiZRTs[0].nameID, 0, 0, mHiZRT.nameID, 0, 0);

            for (int i = 1; i < settings.mipCount; i++)
            {
                ssrMaterial.SetFloat("_FromMipLevel", i - 1);
                ssrMaterial.SetVector("_TexelSize", new Vector4(
                    1.0f / mHiZRTs[i - 1].rt.width, 
                    1.0f / mHiZRTs[i - 1].rt.height, 
                    mHiZRTs[i - 1].rt.width, 
                    mHiZRTs[i - 1].rt.height));

                cmd.Blit(mHiZRTs[i - 1].nameID, mHiZRTs[i].nameID, ssrMaterial, 3);
                cmd.CopyTexture(mHiZRTs[i].nameID, 0, 0, mHiZRT.nameID, 0, i);
            }
            ssrMaterial.SetFloat("_MaxMipLevel", settings.mipCount);
            cmd.SetGlobalTexture("_HiZTex", mHiZRT.nameID);
            // mHiz generation end

            cmd.Blit(null, blur1RT.Identifier(), ssrMaterial, 0);
            cmd.Blit(blur1RT.Identifier(), blur2RT.Identifier(), ssrMaterial, 1);
            cmd.Blit(blur2RT.Identifier(), ssrRT.Identifier(), ssrMaterial, 2);
            cmd.SetGlobalTexture("_SSRTexture", ssrRT.id);

            if (settings.SSRFeature)
            {
                cmd.Blit(ssrRT.id, renderer.cameraColorTargetHandle.nameID);
            }
            cmd.ReleaseTemporaryRT(ssrRT.id);
            cmd.ReleaseTemporaryRT(blur1RT.id);
            cmd.ReleaseTemporaryRT(blur2RT.id);
        }
    }

    public Settings settings = new Settings();
    SSRRenderPass ssrPass;

    public override void Create()
    {
        if (settings.ssrShader != null)
        {
            ssrPass = new SSRRenderPass(settings.ssrShader, settings);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ssrPass != null)
        {
            renderer.EnqueuePass(ssrPass);
        }
    }
}
