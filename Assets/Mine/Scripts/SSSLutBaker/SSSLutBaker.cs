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

        /// <summary>默认皮肤轮廓：红色扩散最远。RGB 为线性相对散射半径，A 忽略。</summary>
        public static readonly Color DefaultProfile = new Color(1f, 0.35f, 0.2f, 1f);

        // ════════════════════════════════════════════════════════════
        //  Bake — 绘制全屏三角形并读取线性 RGBAHalf 纹理
        // ════════════════════════════════════════════════════════════

        public static Texture2D Bake(int resolution = 128, int sampleCount = 256)
        {
            return Bake(resolution, sampleCount, DefaultProfile);
        }

        public static Texture2D Bake(int resolution, int sampleCount, Color profile)
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
                material.SetVector("_ScatterProfile", new Vector4(profile.r, profile.g, profile.b, MaxSpread));

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

        private static void ValidateProfile(Color profile)
        {
            if (!IsValidRadius(profile.r) || !IsValidRadius(profile.g) || !IsValidRadius(profile.b))
                throw new ArgumentOutOfRangeException(nameof(profile), "Each profile radius must be between 0.02 and 2.");
        }

        private static bool IsValidRadius(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.02f && value <= 2f;
        }
    }
}
