// ═══════════════════════════════════════════════════════════════
//  SurfaceLinesFunction — 解析式表面线条，无纹理、无分形层级
//
//  调用方需先 include Core.hlsl。本文件只做坐标投影、线场与
//  导数抗锯齿；颜色、光照和 CBUFFER 由 SurfaceLines.shader 持有。
// ═══════════════════════════════════════════════════════════════
#ifndef SURFACELINESFUNCTION_HLSL_INCLUDED
#define SURFACELINESFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════════
//  Luminance — 光照亮度，用于把线条明暗收在可读范围
// ════════════════════════════════════════════════════════════════
float SurfaceLines_Luminance(float3 color)
{
    return saturate(dot(color, float3(0.299, 0.587, 0.114)));
}

// ════════════════════════════════════════════════════════════════
//  Rotate2D — 把表面坐标旋转到线条方向
// ════════════════════════════════════════════════════════════════
float2 SurfaceLines_Rotate2D(float2 value, float angleRad)
{
    float sine;
    float cosine;
    sincos(angleRad, sine, cosine);
    return float2(
        value.x * cosine - value.y * sine,
        value.x * sine + value.y * cosine);
}

// ════════════════════════════════════════════════════════════════
//  ProjectDominant — 按法线主轴把 3D 坐标压成 2D 平面坐标
// ════════════════════════════════════════════════════════════════
float2 SurfaceLines_ProjectDominant(float3 position, float3 normal)
{
    float3 absNormal = abs(normal);
    if (absNormal.z >= absNormal.x && absNormal.z >= absNormal.y)
    {
        return position.xy;
    }
    if (absNormal.x >= absNormal.y)
    {
        return position.zy;
    }
    return position.xz;
}

// ════════════════════════════════════════════════════════════════
//  LineMask — 单组平行线，宽度以 spacing 为单位
//
//  spacing：线条间距（表面单位）
//  width：线宽占 spacing 的比例
//  softness：软边占 spacing 的比例
//  minPixelWidth：最小屏幕线宽（像素），0 = 关闭
//  phase：线条相位
// ════════════════════════════════════════════════════════════════
float SurfaceLines_Mask(
    float2 surfaceCoord,
    float angleRad,
    float spacing,
    float width,
    float softness,
    float minPixelWidth,
    float phase)
{
    float2 rotated = SurfaceLines_Rotate2D(surfaceCoord, angleRad);
    float lineCoord = rotated.y / max(spacing, 1e-4) + phase;
    float distanceToCenter = abs(frac(lineCoord) - 0.5);

    float footprint = max(length(float2(ddx(lineCoord), ddy(lineCoord))), 1e-6);
    float halfWidth = max(width * 0.5, footprint * max(minPixelWidth, 0.0) * 0.5);
    float antialias = max(softness * 0.5, footprint * 0.5);
    return 1.0 - smoothstep(halfWidth - antialias, halfWidth + antialias, distanceToCenter);
}

// ════════════════════════════════════════════════════════════════
//  Combine — 主线 + 可选交叉线；crossIntensity=0 时只有主线
// ════════════════════════════════════════════════════════════════
float SurfaceLines_Combine(
    float2 surfaceCoord,
    float angleRad,
    float crossAngleRad,
    float spacing,
    float width,
    float softness,
    float minPixelWidth,
    float phase,
    float crossIntensity)
{
    float primary = SurfaceLines_Mask(
        surfaceCoord, angleRad, spacing, width, softness, minPixelWidth, phase);
    float cross = SurfaceLines_Mask(
        surfaceCoord, angleRad + crossAngleRad, spacing, width, softness, minPixelWidth, phase);
    return max(primary, cross * saturate(crossIntensity));
}

#endif // SURFACELINESFUNCTION_HLSL_INCLUDED
