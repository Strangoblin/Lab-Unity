// ════════════════════════════════════════════════════════════════════════════
//  FGDLutBaker — FGD LUT 烘焙工具
//  将 GGX 镜面反射 BRDF + Disney Diffuse 的半球积分预烘焙到一张 2D LUT。
//  输入：NdotV × roughness 参数域
//  输出：(Fresnel scale, Fresnel bias, Diffuse response, 1) 四通道查找表
//  参考：Brian Karis, "Real Shading in Unreal Engine 4" (SIGGRAPH 2013)
// ════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.FGDLutBaker
{
    /// <summary>
    /// FGD LUT 烘焙工具 — 生成预积分 BRDF 响应查找表。
    /// 可通过 GPU pixel shader（调用 Unity 内置 IntegrateGGXAndDisneyDiffuseFGD）
    /// 烘焙，也可用作纯 CPU 计算验证。
    /// 全局纹理下发由 FGDLutManager（场景组件）负责，本类不管理运行时状态。
    /// </summary>
    ///
    /// <remarks>
    /// LUT 通道布局：
    ///   R → scale   — Schlick Fresnel 的 F0 系数（F0 = 0 时的响应）
    ///   G → bias    — Schlick Fresnel 的 (1-F0) 系数（F0 = 1 时的响应）
    ///   B → diffuse — Disney Diffuse 半球积分，存储时已 −0.5 映射到 [−0.5, 0.5]
    ///   A → 未使用  — 保留
    ///
    /// 运行时解码：
    ///   specularFGD = F0 * Lut.g + (1 − F0) * Lut.r
    ///   diffuseFGD  = Lut.b + 0.5
    /// </remarks>
    public static class FGDLutBaker
    {
        // ════════════════════════════════════════════════════════════════════
        //  公开 API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 使用 GPU pixel shader 烘焙 FGD LUT 到 Texture2D。
        /// 需要在主线程调用（使用 CommandBuffer.DrawProcedural + ReadPixels）。
        /// 返回的纹理格式为 RGBAHalf，linear 空间，Clamp 采样。
        /// </summary>
        ///
        /// <param name="resolution">LUT 分辨率，默认 128</param>
        /// <param name="sampleCount">蒙特卡洛采样数，默认 1024</param>
        /// <returns>烘焙完成的 Texture2D，调用方负责销毁</returns>
        public static Texture2D Bake(int resolution = 128, int sampleCount = 1024)
        {
            resolution  = Mathf.Clamp(resolution, 16, 512);
            sampleCount = Mathf.Clamp(sampleCount, 64, 8192);

            Material material = GetMaterial(sampleCount, resolution);
            if (material == null)
            {
                Debug.LogError("FGDLutBaker: Shader \"Hidden/Mine/FGDPacker\" not found. "
                    + "Ensure FGDPacker.shader exists in the project.");
                return null;
            }

            // ── GPU 渲染到 RenderTexture ──
            // 使用 CommandBuffer + DrawProcedural 替代 Graphics.Blit，
            // 确保 URP 兼容模式下顶点着色器的 texcoord 正确输出
            RenderTexture rt = RenderTexture.GetTemporary(
                resolution, resolution, 0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Point;
            rt.wrapMode   = TextureWrapMode.Clamp;

            var cmd = new CommandBuffer { name = "FGD Lut Bake" };
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            RenderTexture.active = rt;

            // ── Readback 到 CPU ──
            Texture2D result = new Texture2D(resolution, resolution, TextureFormat.RGBAHalf, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                name       = "FGD_LUT"
            };

            result.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            result.Apply(false, false);

            // ── 清理 ──
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(material);

            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        //  验证 / 诊断
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 打印 LUT 关键位置的像素值，用于快速验证烘焙结果。
        /// </summary>
        public static void LogDiagnostics(Texture2D lut)
        {
            if (lut == null) return;

            int w = lut.width, h = lut.height;

            // bottom-left  → NdotV≈0, roughness≈0（镜面最强）
            // top-left     → NdotV≈0, roughness≈1（粗糙表面）
            // bottom-right → NdotV≈1, roughness≈0（掠射镜面 → 全反射）
            // top-right    → NdotV≈1, roughness≈1（掠射粗糙 → 最小反射）
            Color cBl = lut.GetPixel(0, 0);
            Color cTl = lut.GetPixel(0, h - 1);
            Color cBr = lut.GetPixel(w - 1, 0);
            Color cTr = lut.GetPixel(w - 1, h - 1);

            Debug.Log(
                $"FGDLutBaker Diagnostics ({w}×{h}):\n"
                + $"  bottom-left (N≈0,R≈0): scale={cBl.r:F4} bias={cBl.g:F4} diff={cBl.b + 0.5f:F4}\n"
                + $"  top-left    (N≈0,R≈1): scale={cTl.r:F4} bias={cTl.g:F4} diff={cTl.b + 0.5f:F4}\n"
                + $"  bottom-right(N≈1,R≈0): scale={cBr.r:F4} bias={cBr.g:F4} diff={cBr.b + 0.5f:F4}\n"
                + $"  top-right   (N≈1,R≈1): scale={cTr.r:F4} bias={cTr.g:F4} diff={cTr.b + 0.5f:F4}\n"
                + $"  Expected: bias≈1 at R≈0; bias drops at R≈1; scale→0 at N≈1");
        }

        // ════════════════════════════════════════════════════════════════════
        //  私有辅助
        // ════════════════════════════════════════════════════════════════════

        private static Material GetMaterial(int sampleCount, int resolution)
        {
            Shader shader = Shader.Find("FGDLutBaker/FGDPacker");
            if (shader == null) return null;

            var mat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            mat.SetFloat("_SampleCount", sampleCount);
            mat.SetVector("_LutResolution", new Vector2(resolution, resolution));
            return mat;
        }
    }
}
