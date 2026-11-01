// ═══════════════════════════════════════════════════════════════
//  SurfaceDitherFunction — surface-stable fractal Bayer dithering
//
//  调用方需先 include Core.hlsl；本文件声明 3D 图案、亮度 ramp 与
//  texel size，所有可调参数由 SurfaceDither_Evaluate 逐项传入。
//
//  算法参考：Rune Skovbo Johansen, Surface-Stable Fractal Dithering
//  https://github.com/runevision/Dither3D （MPL-2.0，URP 重写）
// ═══════════════════════════════════════════════════════════════
#ifndef SURFACEDITHERFUNCTION_HLSL_INCLUDED
#define SURFACEDITHERFUNCTION_HLSL_INCLUDED

TEXTURE3D(_DitherTex);
SAMPLER(sampler_DitherTex);
TEXTURE2D(_DitherRampTex);
SAMPLER(sampler_DitherRampTex);

float4 _DitherTex_TexelSize;
float4 _DitherRampTex_TexelSize;

// ════════════════════════════════════════════════════════════════
//  Luminance — 输入亮度按 Rec.601 权重转灰度
// ════════════════════════════════════════════════════════════════
float SurfaceDither_Luminance(float3 color)
{
    return saturate(dot(color, float3(0.299, 0.587, 0.114)));
}

// ════════════════════════════════════════════════════════════════
//  Frequency — 用 UV Jacobian 的奇异值求屏幕上的最小/最大变化率
// ════════════════════════════════════════════════════════════════
float2 SurfaceDither_Frequency(float2 dx, float2 dy)
{
    float2x2 jacobian = float2x2(dx, dy);
    float q = dot(dx, dx) + dot(dy, dy);
    float r = determinant(jacobian);
    float discriminant = sqrt(max(0.0, q * q - 4.0 * r * r));
    return sqrt(max(0.0, 0.5 * float2(q + discriminant, q - discriminant)));
}

// ════════════════════════════════════════════════════════════════
//  Evaluate — 按屏幕尺度选择分形层级并采样 3D 图案
//
//  uv / dx / dy：表面坐标及其屏幕导数
//  brightness：输入亮度（已乘曝光并加偏移）
//  scaleExp：Dot Scale 的指数形式
//  debugData：调试用层级 UV 与子层坐标
// ════════════════════════════════════════════════════════════════
float SurfaceDither_Evaluate(
    float2 uv,
    float2 dx,
    float2 dy,
    float4 screenPos,
    float brightness,
    float scaleExp,
    float sizeVariability,
    float contrast,
    float stretchSmoothness,
    out float3 debugData)
{
    debugData = float3(0.0, 0.0, 0.0);
    contrast = max(contrast, 0.0);
    stretchSmoothness = max(stretchSmoothness, 0.0);

    // 图案纹理未绑定时退化为原亮度，避免除零与全白输出。
    if (_DitherTex_TexelSize.z < 1.0)
    {
        return saturate(brightness);
    }

    // 纹理横向分辨率 = TexelsPerDot × dotsPerSide；Z 分辨率由脚本按同一规则生成。
    float xRes = _DitherTex_TexelSize.z;
    float invXres = _DitherTex_TexelSize.x;
    float dotsPerSide = xRes / 16.0;
    float dotsTotal = max(dotsPerSide * dotsPerSide, 1.0);
    float invZres = 1.0 / dotsTotal;

    float inputBrightness = saturate(brightness);
    #if defined(_INVERSE_DOTS)
        inputBrightness = 1.0 - inputBrightness;
    #endif

    // 亮度 ramp 把目标亮度映射到阈值，使输出平均亮度与输入一致。
    float2 rampUV = float2(0.5 * invXres + (1.0 - invXres) * inputBrightness, 0.5);
    float brightnessCurve = _DitherRampTex_TexelSize.z >= 1.0
        ? SAMPLE_TEXTURE2D(_DitherRampTex, sampler_DitherRampTex, rampUV).r
        : inputBrightness;

    #if defined(_RADIAL_COMPENSATION)
        // 透视相机下屏幕边缘需要略大的点，才能在相机旋转时保持稳定。
        float2 screenP = (screenPos.xy / max(screenPos.w, 1e-6) - 0.5) * 2.0;
        float2 viewDirProj = float2(
            screenP.x / max(UNITY_MATRIX_P[0][0], 1e-6),
            screenP.y / max(-UNITY_MATRIX_P[1][1], 1e-6));
        float radialCompensation = dot(viewDirProj, viewDirProj) + 1.0;
        dx *= radialCompensation;
        dy *= radialCompensation;
    #endif

    float2 frequency = SurfaceDither_Frequency(dx, dy);

    // 以最小频率定义点间距：斜视/拉伸时压缩一个方向，而不是放大另一个方向。
    float spacing = max(frequency.y, 1e-6);
    spacing *= scaleExp;
    spacing *= dotsPerSide * 0.125;

    // SizeVariability=0 时点大小近似固定，亮度只改变点数量（Bayer 风格）；
    // =1 时亮度直接改变点大小（半调风格）。
    float brightnessSpacing = pow(
        max(brightnessCurve * 2.0 + 0.001, 1e-6),
        -(1.0 - saturate(sizeVariability)));
    spacing *= brightnessSpacing;

    // 最近的 2 的幂层级；小数部分用于在两个子层之间插值。
    float spacingLog = log2(max(spacing, 1e-6));
    float patternScaleLevel = floor(spacingLog);
    float fractionalLevel = spacingLog - patternScaleLevel;

    float2 levelUV = uv / exp2(patternScaleLevel);
    float subLayer = lerp(0.25 * dotsTotal, dotsTotal, 1.0 - fractionalLevel);
    float subLayerNormalized = subLayer * invZres;

    #if defined(_QUANTIZE_LAYERS)
        float originalSubLayer = subLayer;
        subLayer = floor(subLayer + 0.5);
        float thresholdTweak = sqrt(subLayer / max(originalSubLayer, 1e-6));
    #endif

    // 3D 纹理的 Z 轴每层多一个点；采样点位于 texel 中心。
    float patternZ = (subLayer - 0.5) * invZres;
    float pattern = SAMPLE_TEXTURE3D(
        _DitherTex, sampler_DitherTex, float3(levelUV, patternZ)).r;

    // 对比度按最高频率补偿，避免斜视时点边缘走样。
    float contrastScale = contrast * scaleExp * brightnessSpacing * 0.1;
    contrastScale *= pow(
        max(frequency.y, 1e-6) / max(frequency.x, 1e-6),
        stretchSmoothness);

    float baseValue = lerp(
        0.5,
        inputBrightness,
        saturate(1.05 / (1.0 + max(contrastScale, 1e-6))));

    #if defined(_QUANTIZE_LAYERS)
        float threshold = 1.0 - brightnessCurve * thresholdTweak;
    #else
        float threshold = 1.0 - brightnessCurve;
    #endif

    float ditherValue = saturate((pattern - threshold) * contrastScale + baseValue);
    #if defined(_INVERSE_DOTS)
        ditherValue = 1.0 - ditherValue;
    #endif

    debugData = float3(frac(levelUV), saturate(subLayerNormalized));
    return ditherValue;
}

#endif // SURFACEDITHERFUNCTION_HLSL_INCLUDED
