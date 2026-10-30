// ════════════════════════════════════════════════════════════
//  FabricLutBaker — 预积分 KM 双流薄层，生成 R / T / R∞ 三维通道 LUT。
//  U = sqrt(K/S / MaxK)；V = sqrt(S*d / MaxSd)；RGBAHalf 线性、Clamp、点采样。
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.FabricLutBaker
{
    /// <summary>无状态 GPU 烘焙服务；返回纹理由调用方保存或销毁。</summary>
    public static class FabricLutBaker
    {
        public const float DefaultMaxAbsorption = 16f;
        public const float DefaultMaxOpticalDepth = 8f;

        // ════════════════════════════════════════════════════════════
        //  Bake — 绘制全屏三角形并读取线性 RGBAHalf 纹理
        // ════════════════════════════════════════════════════════════

        public static Texture2D Bake(int resolution = 128)
        {
            return Bake(resolution, DefaultMaxAbsorption, DefaultMaxOpticalDepth);
        }

        public static Texture2D Bake(int resolution, float maxAbsorption, float maxOpticalDepth)
        {
            resolution = Mathf.Clamp(resolution, 16, 512);
            ValidateDomain(maxAbsorption, maxOpticalDepth);

            Shader shader = Shader.Find("FabricLutBaker/FabricLutPacker");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("FabricLutPacker shader is missing or unsupported.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new NotSupportedException("Fabric LUT baking requires an ARGBHalf render target.");

            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Material material = null;
            CommandBuffer commands = null;
            Texture2D result = null;
            try
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetVector("_LutResolution", new Vector4(resolution, resolution, 0f, 0f));
                material.SetFloat("_MaxAbsorption", maxAbsorption);
                material.SetFloat("_MaxOpticalDepth", maxOpticalDepth);

                target = RenderTexture.GetTemporary(resolution, resolution, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                target.filterMode = FilterMode.Point;
                target.wrapMode = TextureWrapMode.Clamp;
                commands = new CommandBuffer { name = "Fabric LUT Bake" };
                commands.SetRenderTarget(target);
                commands.SetViewport(new Rect(0f, 0f, resolution, resolution));
                commands.ClearRenderTarget(false, true, Color.clear);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);

                RenderTexture.active = target;
                result = new Texture2D(resolution, resolution, TextureFormat.RGBAHalf, false, true)
                {
                    name = "Fabric_LUT",
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
        //  参数验证 — 参数域必须有限且为正
        // ════════════════════════════════════════════════════════════

        private static void ValidateDomain(float maxAbsorption, float maxOpticalDepth)
        {
            if (!(maxAbsorption > 0f) || float.IsInfinity(maxAbsorption))
                throw new ArgumentOutOfRangeException(nameof(maxAbsorption), "Max K/S must be positive and finite.");
            if (!(maxOpticalDepth > 0f) || float.IsInfinity(maxOpticalDepth))
                throw new ArgumentOutOfRangeException(nameof(maxOpticalDepth), "Max S*d must be positive and finite.");
        }
    }
}
