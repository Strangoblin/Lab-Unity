// ════════════════════════════════════════════════════════════
//  SSSLutBaker — 环形高斯扩散预积分，生成线性 RGB 漫射响应 LUT。
//  U = NdotL * 0.5 + 0.5；V = scatteringDistance * curvature / 2。
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.SSSLutBaker
{
    /// <summary>无状态 GPU 烘焙服务；返回纹理由调用方保存或销毁。</summary>
    public static class SSSLutBaker
    {
        public const float MaxSpread = 2f;
        public static readonly Vector3 DefaultProfile = new Vector3(1f, 0.35f, 0.2f);

        // ════════════════════════════════════════════════════════════
        //  Bake — 绘制全屏三角形并读取线性 RGBAHalf 纹理
        // ════════════════════════════════════════════════════════════

        public static Texture2D Bake(int resolution = 128, int sampleCount = 256)
        {
            return Bake(resolution, sampleCount, DefaultProfile);
        }

        public static Texture2D Bake(int resolution, int sampleCount, Vector3 profile)
        {
            resolution = Mathf.Clamp(resolution, 16, 512);
            sampleCount = Mathf.Clamp(sampleCount, 32, 1024);
            ValidateProfile(profile);

            Shader shader = Shader.Find("SSSLutBaker/SSSLutPacker");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("SSSLutPacker shader is missing or unsupported.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new NotSupportedException("SSS LUT baking requires an ARGBHalf render target.");

            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Material material = null;
            CommandBuffer commands = null;
            Texture2D result = null;
            try
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetFloat("_SampleCount", sampleCount);
                material.SetVector("_LutResolution", new Vector4(resolution, resolution, 0f, 0f));
                material.SetVector("_ScatterProfile", new Vector4(profile.x, profile.y, profile.z, MaxSpread));

                target = RenderTexture.GetTemporary(resolution, resolution, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                target.filterMode = FilterMode.Point;
                target.wrapMode = TextureWrapMode.Clamp;
                commands = new CommandBuffer { name = "SSS LUT Bake" };
                commands.SetRenderTarget(target);
                commands.SetViewport(new Rect(0f, 0f, resolution, resolution));
                commands.ClearRenderTarget(false, true, Color.clear);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);

                RenderTexture.active = target;
                result = new Texture2D(resolution, resolution, TextureFormat.RGBAHalf, false, true)
                {
                    name = "SSS_LUT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };
                result.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                commands?.Dispose();
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  参数验证 — 有限、正值的 RGB 相对散射半径
        // ════════════════════════════════════════════════════════════

        private static void ValidateProfile(Vector3 profile)
        {
            for (int i = 0; i < 3; i++)
            {
                if (float.IsNaN(profile[i]) || float.IsInfinity(profile[i]) || profile[i] < 0.02f || profile[i] > 2f)
                    throw new ArgumentOutOfRangeException(nameof(profile), "Each profile radius must be between 0.02 and 2.");
            }
        }
    }
}
