// ═══════════════════════════════════════════════════════════════
//  面板参数分层骨架（函数无关 · 可编译）
//
//  规范: references/standard/script/parameter-panel.md
//
//  判据 —— 两个正交问题，而不是「属于技术还是美术」：
//    ① 成本占比够大吗？   ② 需要微调吗？
//    两者都「是」→ 划档位（枚举）；否则连续可调。
//
//  使用方式:
//    1. 把本类内容并入你的 Feature.Settings / MonoBehaviour / VolumeComponent
//    2. 替换所有 YourXxx 占位名，删掉用不到的 Header 组
//    3. Header 顺序保持 Technical → Performance → Artistic → Debug
//    4. 档位表的默认档必须逐字段复现整理前的资产值
// ═══════════════════════════════════════════════════════════════

using UnityEngine;

[System.Serializable]
public class YourEffectSettings
{
    // ════════════════════ Technical ════════════════════
    // 调这些是为了正确性（漏光 / 痤疮 / 覆盖范围），不是为了观感，也不是为了帧率。
    // 取值为连续区间 → 连续可调 + [Range]。

    [Header("Technical · Resources")]
    public ComputeShader yourComputeShader;

    [Header("Technical · Bias & Split")]
    [Tooltip("⚠️ 一句话说明：调大 / 调小各会怎样")]
    [Range(0f, 1f)] public float yourSplitLambda = 0.75f;

    // ════════════════════ Performance ════════════════════
    // 纯成本旋钮：对帧率影响大、不需要微调。
    // 一档应当同时决定所有**同源的量**（分辨率 / 采样数 / 级数 / 覆盖距离…），
    // 否则档位名会骗人 —— 比如「High」只提分辨率、不提采样数。
    //
    // 注意：决定「分布方式」而非总成本的量（如 PSSM 的 lambda）不进档位。

    [Header("Performance")]
    [Tooltip("Low    1024 /  8 采样 / 2 级\n" +
             "Medium 2048 / 16 采样 / 3 级\n" +
             "High   2048 / 32 采样 / 4 级")]
    public Performance performance = Performance.High;

    public enum Performance { Low, Medium, High }

    /// <summary>档位展开结果。只读结构体 —— 构造后不可变。</summary>
    public readonly struct PerformanceTier
    {
        public readonly int   yourAtlasRes;
        public readonly int   yourCount;
        public readonly float yourDistance;

        public PerformanceTier(int yourAtlasRes, int yourCount, float yourDistance)
        {
            this.yourAtlasRes = yourAtlasRes;
            this.yourCount    = yourCount;
            this.yourDistance = yourDistance;
        }
    }

    /// <summary>
    /// 档位表。顺序必须与 Performance 枚举一致（靠 (int)p 直接索引）。
    /// 默认档 = 当前资产值 —— 默认行为不变是这类重构唯一的安全保证。
    /// </summary>
    static readonly PerformanceTier[] k_Tiers =
    {
        new PerformanceTier(1024, 2, 30f),   // Low
        new PerformanceTier(2048, 3, 40f),   // Medium
        new PerformanceTier(2048, 4, 50f),   // High
    };

    /// <summary>
    /// 纯函数展开。多个 pass 需要同一份档位结果时，**各自调用本函数重新展开**，
    /// 而不是把某一侧的私有字段提升为跨 pass 共享状态 ——
    /// 纯函数无状态 ⇒ 不存在不同步的可能，比「传一份、信它没被改」更强。
    /// </summary>
    public static PerformanceTier GetTier(Performance p) => k_Tiers[(int)p];

    // ════════════════════ Artistic ════════════════════
    // 一个参数对应一个「观感维度」，调它是为了好看与否。
    // 开关类参数一律「0 = 关闭」，不用 bool + 强度两个字段（省一个字段，语义只剩一种）。

    [Header("Artistic")]
    [Tooltip("半影尺度：0 = 硬边，越大越软")]
    [Range(0f, 4f)] public float softness = 1f;

    [Tooltip("保边模糊强度：0 = 关闭")]
    [Range(0f, 2f)] public float blur = 1f;

    // ════════════════════ Debug ════════════════════
    // 与前三组正交：不调效果，只改「显示什么」。每个 Feature 必须有一个。

    [Header("Debug")]
    [Tooltip("中间结果直出，人工在 Game View 观察")]
    [SerializeField] private bool showIntermediate;

    // ════════════════════ 实现细节 —— 不进面板 ════════════════════
    // 时域钳制半径、置信度幂次、采样偏移乘数这类「调了只会更糟」的量，
    // 写成 HLSL 顶部的 #define，而不是 uniform + 滑条。
    // 省下的不只是面板格子，还有配套的 PropertyToID 与每次 SetCompute*Param。
    //
    // 先例: PCSSFunction.hlsl 的 TEMPORAL_CLAMP_RADIUS / SIGMA / DEPTH_SCALE /
    //       NORMAL_POWER —— 合并了原 5 个 uniform + 5 个 nameID + 5 次绑定。
    //
    // 升格尺寸类参数为档位前，先查「参数半接线」：
    //   grep 数值字面量，不要 grep 参数名。
    //   详见 references/standard/script/parameter-panel.md
}
