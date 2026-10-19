// ════════════════════════════════════════════════════════════
//  ScreenWarpFunction — 功能私有的逆向屏幕映射；无相机、深度或纹理依赖。
//  调用方先include TRS.hlsl。参数已由Feature入口限定合法范围。
// ════════════════════════════════════════════════════════════
#ifndef SCREENWARP_FUNCTION_INCLUDED
#define SCREENWARP_FUNCTION_INCLUDED

// ════════════════════════════════════════════════════════════
//  LensMap — 单位圆覆盖有效画面，四种映射均连续且保持中心不动。
// ════════════════════════════════════════════════════════════
float2 ScreenWarp_LensMap(float2 position, float mode)
{
    float radiusSquared = dot(position, position);
    if (mode < 0.5)
        return position * (1.0 + 0.6 * radiusSquared);
    if (mode < 1.5)
        return position / (1.0 + 0.6 * radiusSquared);
    if (mode < 2.5)
    {
        float angle = 1.2 * sqrt(radiusSquared);
        float scale = angle > 1e-4 ? tan(angle) / angle : 1.0;
        return position * scale;
    }
    // URP Panini_UnitDistance的解析简式，d=1；垂直线保持笔直。
    return position / (1.0 - 0.25 * position.x * position.x);
}

// ════════════════════════════════════════════════════════════
//  SourceUV — 输出像素反查源图；插值坐标而非叠加两张图。
// ════════════════════════════════════════════════════════════
float2 ScreenWarp_SourceUV(float2 uv, float aspect, float mode,
    float strength, float2 center, float zoom)
{
    float2 farCorner = max(center, 1.0 - center);
    float radius = length(TRS2D_UVToIsotropic(farCorner, aspect));
    float2 position = TRS2D_UVToIsotropic(uv - center, aspect) / (radius * zoom);
    float2 mapped = center + TRS2D_IsotropicToUV(ScreenWarp_LensMap(position, mode) * radius, aspect);
    return lerp(uv, mapped, strength);
}

float ScreenWarp_Inside(float2 uv)
{
    return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
}

#endif
