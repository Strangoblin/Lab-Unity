// ════════════════════════════════════════════════════════════
//  SSSFunction — SSS LUT 烘焙、曲率估计与响应采样。
//  调用方先引用 Core.hlsl；内置 _SSSLut 纹理，材质参数由调用方传入。
//  烘焙积分与几何计算保留 float 精度；LUT 最大 spread 为 2，响应已含余弦。
// ════════════════════════════════════════════════════════════

#ifndef SSSFUNCTION_HLSL_INCLUDED
#define SSSFUNCTION_HLSL_INCLUDED

TEXTURE2D(_SSSLut);
SAMPLER(sampler_SSSLut);

// ════════════════════════════════════════════════════════════
//  烘焙 — 环形表面的 RGB 高斯扩散积分
// ════════════════════════════════════════════════════════════

/// <summary>按弦长计算散射权重并逐通道归一化；sampleCount 必须大于零。</summary>
float3 SSS_Integrate(float NdotL, float spread, float3 scatterProfile, uint sampleCount)
{
    if (spread <= 1e-6)
        return saturate(NdotL).xxx;

    float3 profile = max(scatterProfile, 0.02);
    float maxRadius = max(profile.r, max(profile.g, profile.b));
    float angleLimit = min(PI, 6.0 * spread * maxRadius);
    float sinTheta = sqrt(saturate(1.0 - NdotL * NdotL));
    float3 totalWeight = 0.0;
    float3 totalLight = 0.0;

    [loop]
    for (uint i = 0u; i < sampleCount; i++)
    {
        float angle = (i + 0.5) * angleLimit / sampleCount;
        float distance = 2.0 * sin(angle * 0.5) / spread;
        float3 normalizedDistance = distance / profile;
        float3 weight = exp(-0.5 * normalizedDistance * normalizedDistance);
        float c = cos(angle);
        float s = sin(angle);
        float irradiance = 0.5 * (max(NdotL * c + sinTheta * s, 0.0)
                               + max(NdotL * c - sinTheta * s, 0.0));
        totalWeight += weight;
        totalLight += weight * irradiance;
    }
    return saturate(totalLight / max(totalWeight, 1e-8));
}

// ════════════════════════════════════════════════════════════
//  几何 — 屏幕导数自动估计曲率
// ════════════════════════════════════════════════════════════

/// <summary>用同一导数基底估计几何曲率；normalWS 为归一化几何法线，仅在片元阶段调用。</summary>
float SSS_Curvature(float3 positionWS, float3 normalWS)
{
    float3 normalX = ddx(normalWS);
    float3 normalY = ddy(normalWS);
    float3 positionX = ddx(positionWS);
    float3 positionY = ddy(positionWS);
    float normalChange = dot(normalX, normalX) + dot(normalY, normalY);
    float positionChange = dot(positionX, positionX) + dot(positionY, positionY);
    return sqrt(normalChange / max(positionChange, 1e-10));
}

// ════════════════════════════════════════════════════════════
//  采样 — 自动曲率、散射范围与 LUT 像素中心映射
// ════════════════════════════════════════════════════════════

/// <summary>按散射距离与自动曲率查表；输入归一化几何法线，仅在片元阶段调用，未赋 LUT 或零范围返回零贡献。</summary>
real3 SSS_SampleLut(real NdotL, float3 positionWS, float3 normalWS,
    real scatteringDistance, float4 texelSize)
{
    float curvature = SSS_Curvature(positionWS, normalWS);
    float range = saturate(max(scatteringDistance, 0.0) * curvature * 0.5);
    if (texelSize.z <= 2.0 || range <= 0.0)
        return 0.0;

    float2 uv = float2(NdotL * 0.5 + 0.5, saturate(range));
    uv = saturate(uv) * (1.0 - texelSize.xy) + 0.5 * texelSize.xy;
    return SAMPLE_TEXTURE2D_LOD(_SSSLut, sampler_SSSLut, uv, 0).rgb;
}

#endif // SSSFUNCTION_HLSL_INCLUDED
