// ═══════════════════════════════════════════════════════════════
//  Baker Service 模板（核心层）
//
//  家族定位：Baker 的「烘焙服务」层 —— 无状态静态类，只负责把
//    输入（场景框架 / 参数）产出为一张 Texture2D。不做 UI、不保存资产。
//
//  实源：
//    Assets/Mine/Scripts/InteriorMapBaker/InteriorMapTextureBaker.cs
//      —— 场景框架输入 + 临时 Camera 渲染 + CPU 像素转换
//    Assets/Mine/Scripts/FGDLutBaker/FGDLutBaker.cs
//      —— 纯参数输入 + GPU 烘焙（RenderTexture + CommandBuffer）
//  配套窗口壳：通用 window 家族（../window/editor-window.cs，Baker 消费形态见其 README）
//
//  使用方式：
//    1. 复制到 Assets/Mine/Scripts/<YourTool>/，类名改为 <YourTool>Baker
//    2. 替换所有 ⚠️ 标记
//    3. 窗口的 Bake 按钮调用本类的公开 Bake()；窗口负责预览与保存
//    4. 需要命名空间时加 ⚠️ namespace（FGDLutBaker 先例：Mine.<YourTool>）
//
//  所有权约定：本类创建的纹理返回给调用方（窗口），由窗口在其
//    生命周期内销毁；本类不持有任何静态纹理。
//
//  结构规范：公开 API 在上、私有辅助在下（references/standard/script/script-structure.md）
// ═══════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 烘焙服务 — 从 ⚠️(场景框架 / 参数) 生成一张烘焙纹理。
/// 返回的纹理由调用方负责销毁；本类不管理运行时状态。
/// </summary>
public static class YourBaker
{
    /// <summary>
    /// 烘焙入口。主线程调用；结果纹理所有权移交调用方。
    /// </summary>
    /// <param name="resolution">输出分辨率</param>
    /// <returns>烘焙完成的 Texture2D，调用方负责销毁；失败返回 null</returns>
    public static Texture2D Bake(int resolution) // ⚠️ 按需加 framework 输入 / 更多参数
    {
        resolution = Mathf.Clamp(resolution, 16, 2048); // ⚠️ 按实际范围 clamp

        // ═══ 输入校验：Shader.Find 材质校验（FGD 先例：找不到 → null）═══
        Material material = GetMaterial();
        if (material == null)
        {
            return null;
        }
        // ⚠️ 另一种形态 — 场景框架校验（InteriorMap 先例：null / 未初始化 → throw）：
        //   if (framework == null) throw new UnityException("YourBaker: No framework selected.");

        // ═══ 临时资源 + 渲染 + 回读：try/finally 保证清理 ═══
        RenderTexture rt = null;
        try
        {
            rt = RenderTexture.GetTemporary(
                resolution, resolution, 0,
                RenderTextureFormat.ARGBHalf, // ⚠️ 按输出精度选择
                RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Point;
            rt.wrapMode = TextureWrapMode.Clamp;

            // ⚠️ URP：用 CommandBuffer + DrawProcedural，不用 Graphics.Blit
            //   （FGD 实坑：Blit 在 URP 兼容模式下 texcoord 输出不正确）
            var cmd = new CommandBuffer { name = "<YourTool> Bake" }; // ⚠️
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            return Readback(rt); // ⚠️ 私有辅助：ReadPixels → 结果纹理
        }
        finally
        {
            if (rt != null)
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            Object.DestroyImmediate(material);
            // ⚠️ 其他临时资源同样在 finally 清理（InteriorMap 先例：临时烘焙 Camera、
            //   Cubemap 均 Object.DestroyImmediate(...)）
        }
    }

    /// <summary>
    /// 结果自检 — 打印关键位置像素值，供窗口人工验收（可选）。
    /// </summary>
    public static void LogDiagnostics(Texture2D texture)
    {
        if (texture == null) return;
        // ⚠️ 打印有物理/视觉意义的位置（LUT 角点、图集分区等）与期望值
    }

    // ════════════════════════════════════════════════════════════
    //  私有辅助 — 材质获取 / Readback 等
    // ════════════════════════════════════════════════════════════

    private static Material GetMaterial()
    {
        Shader shader = Shader.Find("<YourTool>/<YourPacker>"); // ⚠️ 与工具 Shader 的菜单路径一致
        if (shader == null)
        {
            Debug.LogError("YourBaker: Shader not found. Ensure it exists in the project.");
            return null;
        }

        var material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        // ⚠️ 材质的 shader 参数在烘焙前 SetFloat / SetVector
        return material;
    }

    private static Texture2D Readback(RenderTexture rt)
    {
        int width = rt.width;
        int height = rt.height;

        RenderTexture.active = rt;
        var result = new Texture2D(width, height, TextureFormat.RGBAHalf, false) // ⚠️ 与 RT 格式对应
        {
            name = "<YourTool>_Baked", // ⚠️
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(false, false);
        RenderTexture.active = null;
        return result;
    }
}
