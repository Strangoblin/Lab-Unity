// ════════════════════════════════════════════════════════════
//  TransparentShadowFunction — 光空间透射图的接收端
//
//  _TransparentShadowTex      RGB = 玻璃透射色，A = 归一化光空间深度
//  _TransparentShadowParams   xy = 光空间包围盒中心，zw = 包围盒尺寸
//  _TransparentShadowDepth    x = zMin，y = zMax，z = enabled，w = 深度 bias
//
//  世界点投到光空间后与玻璃深度比较：在玻璃后取透射色，在玻璃前/无玻璃回白。
//  循环内使用 LOD0，避免隐式导数在变长循环中的未定义值。
// ════════════════════════════════════════════════════════════
#ifndef TRANSPARENT_SHADOW_FUNCTION_INCLUDED
#define TRANSPARENT_SHADOW_FUNCTION_INCLUDED

TEXTURE2D(_TransparentShadowTex);
SAMPLER(sampler_TransparentShadowTex);

float4x4 _TransparentShadowWorldToLight;
float4 _TransparentShadowParams;
float4 _TransparentShadowDepth;

float3 TransparentShadow_Transmission(float3 positionWS)
{
    if (_TransparentShadowDepth.z < 0.5)
        return float3(1.0, 1.0, 1.0);

    float3 pLS = mul(_TransparentShadowWorldToLight, float4(positionWS, 1.0)).xyz;
    float2 uv = (pLS.xy - _TransparentShadowParams.xy) / _TransparentShadowParams.zw + 0.5;

    // 透射图由自定义 clip 渲染，D3D 的 RT V 方向与相机 projection 不同；
    // 采样前按平台翻转 Y，颜色与深度一起对齐。
    #if UNITY_UV_STARTS_AT_TOP
        uv.y = 1.0 - uv.y;
    #endif

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float3(1.0, 1.0, 1.0);

    float4 glass = SAMPLE_TEXTURE2D_LOD(_TransparentShadowTex, sampler_TransparentShadowTex, uv, 0);

    // 深度是原始归一化光空间 z，不是 reversed-Z。
    float pointDepth = saturate(
        (pLS.z - _TransparentShadowDepth.x) / (_TransparentShadowDepth.y - _TransparentShadowDepth.x));

    // 玻璃后（沿光方向更远，pLS.z 更大）才取透射色；玻璃前或无玻璃保持白色
    return pointDepth > glass.a + _TransparentShadowDepth.w ? glass.rgb : float3(1.0, 1.0, 1.0);
}

#endif // TRANSPARENT_SHADOW_FUNCTION_INCLUDED
