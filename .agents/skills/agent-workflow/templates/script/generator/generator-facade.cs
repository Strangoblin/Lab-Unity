// ═══════════════════════════════════════════════════════════════
//  Generator Facade 模板（核心层）
//
//  家族定位：Generator 的「facade」层 —— 静态类，从参数/场景输入
//    生成**可再生**资产（纹理 / 曲线数据 / 网格）并提供采样 API。
//    改参数随时重出，结果通用；不持有状态、不做 UI。
//
//  历史实源（仅溯源；标准形示例）：
//    Assets/Mine/Scripts/NoiseGenerator/NoiseGenerator.cs
//    Assets/Mine/Scripts/CurveGenerator/CurveBake.cs
//  配套窗口：窗口瘦壳调本类公开 API；统一脊柱见实码
//    Assets/Mine/Scripts/NoiseGenerator/Editor/NoiseGeneratorWindow.cs
//
//  使用方式：
//    1. 复制到 <category.Script>/<YourTool>/，类名改为 <YourTool>Generator
//    2. 替换所有 ⚠️ 标记
//    3. 窗口 Generate 按钮调公开生成 API，Save 由窗口侧完成（本类不碰 AssetDatabase）
//    4. 多算法时定义 ⚠️ public enum YourType（NoiseGenerator.NoiseType 先例）
//
//  结构规范：公开 API 在上、私有辅助在下；仅跨文件共享工具设 public
//    （先例：Repeat01/Lerp 被 Noises/ 实现类调用）
// ═══════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>
/// 程序化生成 facade — 从参数生成 ⚠️(纹理/数据) 并暴露采样入口。
/// 可在 Editor 与 Runtime 调用。
/// </summary>
public static class YourGenerator
{
    /// <summary>
    /// 生成 ⚠️(体积/2D) 纹理。所有权归调用方（窗口/运行时组件）。
    /// </summary>
    public static Texture2D GenerateTexture(
        int resolution,        // ⚠️ 按实际范围 clamp（先例：[8, 2048]）
        float scale,           // ⚠️ 频率/密度倍率
        bool seamless,         // ⚠️ 是否需要无缝平铺
        float randomSeed = 0f) // ⚠️ 种子：决定 ⚠️(切片位置/图案相位)
    {
        resolution = Mathf.Clamp(resolution, 8, 2048);

        // ═══ 纹理创建约定：wrap/filter/name 一次设齐 ═══
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "<YourTool>_Generated" // ⚠️ 便于 Inspector/日志识别
        };

        Color[] colors = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            float wy = (float)y / resolution;
            for (int x = 0; x < resolution; x++)
            {
                float wx = (float)x / resolution;
                float value = Sample(wx, wy, scale, seamless); // ⚠️ 私有采样入口
                colors[x + y * resolution] = new Color(value, value, value, 1f);
            }
        }

        if (seamless)
        {
            CopySeamlessBorders(colors, resolution); // ⚠️ 第二层保障：边界拷贝闭合
        }

        texture.SetPixels(colors);
        texture.Apply();
        return texture;
    }

    // ════════════════════════════════════════════════════════════
    //  私有辅助 — 采样 / 无缝 / 共享小工具
    // ════════════════════════════════════════════════════════════

    /// <summary>⚠️ 单点采样入口。无缝时先做周期包装再采样。</summary>
    private static float Sample(float x, float y, float scale, bool seamless)
    {
        // ⚠️ 无缝第一层（NoiseGenerator 先例）：
        //   SampleTileable3D 对 {0,1}² 四个偏移采样做双线性插值消除接缝；
        //   坐标用 Repeat01 模运算回卷。
        if (seamless)
        {
            x = Repeat01(x);
            y = Repeat01(y);
            return Compute(x, y, scale); // ⚠️ 改为真正的四角插值实现
        }
        return Compute(x, y, scale);
    }

    private static float Compute(float x, float y, float scale)
    {
        // ⚠️ 算法实现体：如 PerlinNoise.Sample / VoronoiNoise.Sample（放独立 Noises/ 类时在此转发）
        return (x * y * scale) % 1f;
    }

    private static void CopySeamlessBorders(Color[] colors, int size)
    {
        // ⚠️ 把最后一列/行像素替换为第一列/行的对应值（NoiseGenerator.MakeSeamless2D 先例）
        if (size <= 1) return;
    }

    /// <summary>模 1 回卷，输出 ∈ [0, 1)。跨实现类共享时保持 public（先例）。</summary>
    public static float Repeat01(float value)
    {
        return value - Mathf.Floor(value);
    }
}
