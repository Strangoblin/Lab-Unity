// ════════════════════════════════════════════════════════════
//  TheStarryNightSDF.hlsl — 星夜 SDF 功能库
//  内部依赖：HashFunction.hlsl（通用随机映射）
//  径向：min 距离并集（逐星半径缩放）；周向：2NN 平滑 Voronoi 角场
//  山水：纵向（V 渐变 + U 波）+ 横向（U 渐变 + 逐层位移）
//  麦田：纵向转置调用（uv.yx）→ 竖笔触扭摆
//  依赖调用方定义（三分类 SKY_/MOUNT_/WHEAT_；共享项无前缀）：
//    SKY_POINT_COUNT / SKY_POINTS / SKY_POINT_RADII / SKY_ANGLE_BLEND（星空环扇）
//    ROW_WAVE_AMP / ROW_WAVE_FREQ（共享行波：山水行带波，麦田转置扭摆）
//    MOUNT_COL_STEP（山水横向逐层位移）
//  依赖调用方 CBUFFER：_Random（色彩扰动）/ _SkyColorA/B/C（星空三段配色）
//  注：裁剪区间数组（MOUNT_ROWS 等）仅调用方 frag 使用，库函数不引用
// ════════════════════════════════════════════════════════════

#ifndef THE_STARRY_NIGHT_SDF_HLSL
#define THE_STARRY_NIGHT_SDF_HLSL

#include "Assets/Mine/Special/HLSL/HashFunction.hlsl"

// ════════════════════════════════════════════════════════════
//  Hash / Hash2 — 1D/2D 哈希（±0.5）；置于最前（Metal 不支持下向引用）
// ════════════════════════════════════════════════════════════
float Hash(float x)
{
    return Hash_Sine21(float2(x, 0.0)) - 0.5;
}

float Hash2(float x, float y)
{
    return Hash_Sine21(float2(x, y)) - 0.5;
}

// ════════════════════════════════════════════════════════════
//  ComputeRadialSDF — 点集圆形 SDF 并集（min 距离，逐星半径缩放）
// ════════════════════════════════════════════════════════════
float ComputeRadialSDF(float2 uv)
{
    float distance = 1e10;
    for (int i = 0; i < SKY_POINT_COUNT; i++)
    {
        float dist = length(uv - SKY_POINTS[i]) / SKY_POINT_RADII[i];
        distance = min(distance, dist);
    }
    return distance;
}

// ════════════════════════════════════════════════════════════
//  ComputeAngularSDF — 2NN 平滑 Voronoi 角场（圆插值，无 0/1 缝）
// ════════════════════════════════════════════════════════════
float ComputeAngularSDF(float2 uv)
{
    float bestDist = 1e10;
    float secondDist = 1e10;
    int best = 0;
    int second = 0;
    for (int i = 0; i < SKY_POINT_COUNT; i++)
    {
        float dist = length(uv - SKY_POINTS[i]);
        if (dist < bestDist)
        {
            secondDist = bestDist;
            second = best;
            bestDist = dist;
            best = i;
        }
        else if (dist < secondDist)
        {
            secondDist = dist;
            second = i;
        }
    }

    float2 deltaA = uv - SKY_POINTS[best];
    float2 deltaB = uv - SKY_POINTS[second];

    // 边界带权重：平分线 → 0.5，远离 → 纯最近源
    float t = saturate(0.5 + 0.5 * (bestDist - secondDist) / max(SKY_ANGLE_BLEND, 1e-4));

    // 单位向量加权 → atan2
    float2 v = deltaA / max(length(deltaA), 1e-4) * (1.0 - t)
             + deltaB / max(length(deltaB), 1e-4) * t;
    v = (length(v) > 1e-4) ? v : deltaA;
    return (atan2(v.y, v.x) + PI) / (2.0 * PI);
}

// ════════════════════════════════════════════════════════════
//  ComputeCell — 灰度切 cell，返回 (边界光, 单元索引)；count × scale 就近取整
// ════════════════════════════════════════════════════════════
float2 ComputeCell(float gray, float count, float scale, float softness)
{
    scale = max(scale, 1);
    float n = max(1.0, round(count * scale));
    float t = gray * n;
    float cellColor = frac(t);
    float cellIndex = floor(t);
    cellColor = smoothstep(0.0, softness, cellColor) * smoothstep(0.0, softness, 1.0 - cellColor);
    return float2(cellColor, cellIndex);
}

// ════════════════════════════════════════════════════════════
//  ComputeLongitudinalSDF — 纵向 SDF：V 渐变 + U 波函数（phase 错层）
//  山水：行带波（原坐标系）；麦田：转置调用（uv.yx）即竖笔触扭摆
// ════════════════════════════════════════════════════════════
float ComputeLongitudinalSDF(float2 uv, float time, float phase)
{
    return uv.y + ROW_WAVE_AMP * sin(uv.x * ROW_WAVE_FREQ * (2.0 * PI) + time + phase);
}

// ════════════════════════════════════════════════════════════
//  ComputeHorizontalSDF — 横向 SDF：U 渐变 + 层序 × MOUNT_COL_STEP 位移（山水专用）
// ════════════════════════════════════════════════════════════
float ComputeHorizontalSDF(float2 uv, float rowLayer)
{
    return uv.x + rowLayer * MOUNT_COL_STEP;
}

// ════════════════════════════════════════════════════════════
//  ComputeRowColor — 带内着色：裁剪区间归一 colorA→colorB + Hash2 扰动
//  （扰动幅度 _Random；山水传 _MountColorA/B，麦田传 _WheatColorA/B）
// ════════════════════════════════════════════════════════════
float3 ComputeRowColor(float rowIndex, float colIndex, float rowCount,
                       float startIndex, float endIndex,
                       float4 colorA, float4 colorB)
{
    float t = saturate((rowIndex - startIndex) / max(endIndex - startIndex, 1e-4));
    float r = Hash2(rowIndex, colIndex) * _Random;
    t = saturate(t + r);
    return lerp(colorA.rgb, colorB.rgb, t);
}

// ════════════════════════════════════════════════════════════
//  GaussPeak — 高斯波峰 exp(-((t-pos)/width)²) × height
// ════════════════════════════════════════════════════════════
float GaussPeak(float t, float3 peak)
{
    float d = (t - peak.x) / peak.y;
    return exp(-(d * d)) * peak.z;
}

// ════════════════════════════════════════════════════════════
//  ComputeSkyColor — 环/扇索引 → 双波峰 C→B→A 配色 + Hash2 扰动
// ════════════════════════════════════════════════════════════
float3 ComputeSkyColor(float ringIndex, float sectorIndex, float ringCount)
{
    float t = ringIndex / ringCount;
    float r = Hash2(ringIndex, sectorIndex) * _Random;
    t = saturate(t + r);

    // 双波峰：tz=高度（1=A, 0.5=B, 0=C）
    float3 peak1 = float3(0.0, 0.25, 1.0);
    float3 peak2 = float3(0.4, 0.025, 0.5);
    float height = max(GaussPeak(t, peak1), GaussPeak(t, peak2));

    // 高度 [0,1] → C→B→A 三段映射
    float tBC = saturate(height * 2.0);
    float3 colCtoB = lerp(_SkyColorC.rgb, _SkyColorB.rgb, tBC);
    float tBA = saturate((height - 0.5) * 2.0);
    return lerp(colCtoB, _SkyColorA.rgb, tBA);
}

#endif // THE_STARRY_NIGHT_SDF_HLSL
