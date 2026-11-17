// ════════════════════════════════════════════════════════════
//  ColoredShadowFunction — 光空间彩色透射图的接收端
//
//  _ColoredShadowTex      RGB = 玻璃透射色，A = 归一化光空间深度
//  _ColoredShadowParams   xy = 光空间包围盒中心，zw = 包围盒尺寸
//  _ColoredShadowDepth    x = zMin，y = zMax，z = enabled，w = 深度 bias
//
//  世界点投到光空间后与玻璃深度比较：在玻璃后取透射色，在玻璃前/无玻璃回白。
//  循环内使用 LOD0，避免隐式导数在变长循环中的未定义值。
// ════════════════════════════════════════════════════════════
#ifndef COLORED_SHADOW_FUNCTION_INCLUDED
#define COLORED_SHADOW_FUNCTION_INCLUDED

TEXTURE2D(_ColoredShadowTex);
SAMPLER(sampler_ColoredShadowTex);

float4x4 _ColoredShadowWorldToLight;
float4 _ColoredShadowParams;
float4 _ColoredShadowDepth;

float3 ColoredShadow_Transmission(float3 positionWS)
{
    if (_ColoredShadowDepth.z < 0.5)
        return float3(1.0, 1.0, 1.0);

    float3 pLS = mul(_ColoredShadowWorldToLight, float4(positionWS, 1.0)).xyz;
    float2 uv = (pLS.xy - _ColoredShadowParams.xy) / _ColoredShadowParams.zw + 0.5;

    // 彩色图由自定义 clip 渲染，D3D 的 RT V 方向与相机 projection 不同；
    // 采样前按平台翻转 Y，颜色与深度一起对齐。
    #if UNITY_UV_STARTS_AT_TOP
        uv.y = 1.0 - uv.y;
    #endif

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float3(1.0, 1.0, 1.0);

    float4 glass = SAMPLE_TEXTURE2D_LOD(_ColoredShadowTex, sampler_ColoredShadowTex, uv, 0);

    // 深度是原始归一化光空间 z，不是 reversed-Z。
    float pointDepth = saturate(
        (pLS.z - _ColoredShadowDepth.x) / (_ColoredShadowDepth.y - _ColoredShadowDepth.x));

    // 玻璃后（沿光方向更远，pLS.z 更大）才取透射色；玻璃前或无玻璃保持白色
    return pointDepth > glass.a + _ColoredShadowDepth.w ? glass.rgb : float3(1.0, 1.0, 1.0);
}

#endif // COLORED_SHADOW_FUNCTION_INCLUDED
