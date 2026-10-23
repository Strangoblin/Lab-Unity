// ════════════════════════════════════════════════════════════
//  SDF — 归一化覆盖度有符号距离函数
//  position 与 radius/bounds 使用同一空间单位。输出限制在 [0,1]：
//  图元中心为 1、边界为 0、外部为 0。盒/矩形逐轴按各自半尺寸
//  归一化，长短轴各自半程均为 0.5；不作为世界单位欧氏距离。
//  极小半尺寸按 1e-6 处理。平滑运算的 k 使用 [0,1] 覆盖度单位。
// ════════════════════════════════════════════════════════════
#ifndef SDF_HLSL_INCLUDED
#define SDF_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  SDF_NormalizeCoverage — 有符号距离转中心 1、边界 0 的覆盖度
// ════════════════════════════════════════════════════════════
float SDF_NormalizeCoverage(float signedDistance, float centerDistance)
{
    return saturate(-signedDistance / max(abs(centerDistance), 1e-6));
}

// ════════════════════════════════════════════════════════════
//  SDF_Sphere — 归一化三维球体，中心 1、边界 0
// ════════════════════════════════════════════════════════════
float SDF_Sphere(float3 position, float radius)
{
    float safeRadius = max(abs(radius), 1e-6);
    return SDF_NormalizeCoverage(length(position) - safeRadius, safeRadius);
}

// ════════════════════════════════════════════════════════════
//  SDF_Box — 逐轴归一化三维盒体，中心 1、边界 0
// ════════════════════════════════════════════════════════════
float SDF_Box(float3 position, float3 bounds)
{
    float3 normalizedPosition = abs(position) / max(abs(bounds), 1e-6);
    return saturate(1.0 - max(normalizedPosition.x, max(normalizedPosition.y, normalizedPosition.z)));
}

// ════════════════════════════════════════════════════════════
//  SDF_Circle — 归一化二维圆，中心 1、边界 0
// ════════════════════════════════════════════════════════════
float SDF_Circle(float2 position, float radius)
{
    float safeRadius = max(abs(radius), 1e-6);
    return SDF_NormalizeCoverage(length(position) - safeRadius, safeRadius);
}

// ════════════════════════════════════════════════════════════
//  SDF_Square — 逐轴归一化二维方形/矩形，中心 1、边界 0
// ════════════════════════════════════════════════════════════
float SDF_Square(float2 position, float2 bounds)
{
    float2 normalizedPosition = abs(position) / max(abs(bounds), 1e-6);
    return saturate(1.0 - max(normalizedPosition.x, normalizedPosition.y));
}

// ════════════════════════════════════════════════════════════
//  平滑并集 — [0,1] 覆盖度平滑最大值
// ════════════════════════════════════════════════════════════
float opSmoothUnion(float d1, float d2, float k)
{
    float smoothK = max(k, 1e-6);
    float h = saturate(0.5 + 0.5 * (d1 - d2) / smoothK);
    return saturate(lerp(d2, d1, h) + smoothK * h * (1.0 - h));
}

// ════════════════════════════════════════════════════════════
//  平滑差集 — 从 d2 中平滑扣除 d1
// ════════════════════════════════════════════════════════════
float opSmoothSubtraction(float d1, float d2, float k)
{
    return saturate(d2 * (1.0 - smoothstep(0.0, max(k, 1e-6), saturate(d1))));
}

// ════════════════════════════════════════════════════════════
//  平滑交集 — [0,1] 覆盖度平滑最小值
// ════════════════════════════════════════════════════════════
float opSmoothIntersection(float d1, float d2, float k)
{
    float smoothK = max(k, 1e-6);
    float h = saturate(0.5 - 0.5 * (d1 - d2) / smoothK);
    return saturate(lerp(d2, d1, h) - smoothK * h * (1.0 - h));
}

#endif // SDF_HLSL_INCLUDED
