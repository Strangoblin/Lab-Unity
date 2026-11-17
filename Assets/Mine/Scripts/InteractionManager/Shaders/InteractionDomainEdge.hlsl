// ═══════════════════════════════════════════════════════════════
//  InteractionDomainEdge.hlsl — 交互域边界的统一淡出约定
//
//  目的：痕迹不许被硬裁在 RT 边缘那条直线上。域是跟着球走的，球走远的时候
//  旧痕迹会被域边缘一路扫出 RT；扫出去的过程必须是「渐隐到没有」，
//  而不是「显示上淡了、数据里还满着」（后者在域走回来时会露出硬断口）。
//
//  两个动作，缺一不可：
//
//  1) InteractionDomainFade —— 乘在**输入**上（捕获到的接触深度 / 冲量）。
//     贴在域边上的探针只能压出 ≈ 0，于是压出来的痕迹本身就带淡出形状。
//
//  2) InteractionDomainAbsorb —— 按**穿过淡出带的距离**吸收已存的痕迹。
//     `d` 是这一步域在世界里移动的距离，域不动时恒为 1（痕迹一点不变）。
//     域把痕迹扫向边界时按位移累积，穿过整条带约 e^-8，数据被真的吃掉。
//
//  为什么不是「每步把存值再乘一次窗口」：域不动、痕迹不动时也每步乘，
//  带内被反复相乘几十步后吃空，只剩内沿 w = 1 那道坎 —— 软边自己变成硬边
//  （实测雪地静置 3 s 带内台阶 11% → 48%）。
//
//  为什么不是「读时除、写时乘」的往返：那样存进去的原始值一直是满的，
//  淡出只是显示遮罩；域把痕迹扫出去时数据没被消耗，走回来就恢复成满强度，
//  紧挨着真正掉出 RT 的那一格（0）—— 域中间凭空多出一条 97% 的硬断口
//  （见 Validation~/CheckTraceSeam.cs）。吸收必须是真的消耗。
// ═══════════════════════════════════════════════════════════════

#ifndef INTERACTIONDOMAINEDGE_HLSL_INCLUDED
#define INTERACTIONDOMAINEDGE_HLSL_INCLUDED

// 淡出带宽度（占 RT 边长的比例）。12 m / 256 的 Demo 域上约 0.96 m、20 个纹素。
// C# 侧的同名常量在 InteractionProcessor.DomainFadeWidth，改这里要一起改。
#define INTERACTION_DOMAIN_FADE 0.08

// 每个轴的一维窗口：域内 1，靠近边界平滑降到 0
//
// 注意 smoothstep 只在 uv 恰好为 0 时取到 0，而纹素中心永远不会落在 uv = 0
// （最外圈是 0.5/resolution），所以最外圈拿到的是 window ≈ 1.8e-3，乘上去已经看不见了。
float2 InteractionDomainWindowAxis(float2 uv)
{
    float2 lo = smoothstep(0.0, INTERACTION_DOMAIN_FADE, uv);
    float2 hi = smoothstep(0.0, INTERACTION_DOMAIN_FADE, 1.0 - uv);
    return lo * hi;
}

float InteractionDomainWindow(float2 uv)
{
    float2 w = InteractionDomainWindowAxis(uv);
    return w.x * w.y;
}

float InteractionDomainWindowAt(int2 pixel, float resolution)
{
    return InteractionDomainWindow((float2(pixel) + 0.5) / max(resolution, 1.0));
}

// ── 1) 窗口化输入 ─────────────────────────────────────────────
// 只对**这一帧新进来的量**乘窗口（接触深度、冲量），不要对读回来的旧值乘。
float InteractionDomainFade(float value, int2 pixel, float resolution)
{
    return value * InteractionDomainWindowAt(pixel, resolution);
}

// ── 2) 边界吸收 ───────────────────────────────────────────────
// absorbPerBand：这一步域沿每个轴移动的距离占淡出带宽度的比例 × 吸收系数。
// 域不动 → 0 → 返回 1（痕迹完全不变）；穿过整条带 → 约 e^-8。
// 用位移而不是时间：域停住时痕迹不该继续掉，球慢慢走和快跑也不该衰减不一样。
float InteractionDomainAbsorb(int2 pixel, float resolution, float2 absorbPerBand)
{
    float2 w = InteractionDomainWindowAxis((float2(pixel) + 0.5) / max(resolution, 1.0));
    return exp(-dot(absorbPerBand, saturate(1.0 - w)));
}

#endif // INTERACTIONDOMAINEDGE_HLSL_INCLUDED
