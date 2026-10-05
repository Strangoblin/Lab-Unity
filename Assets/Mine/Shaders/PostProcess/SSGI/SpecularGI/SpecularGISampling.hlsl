// ═══════════════════════════════════════════════════════════════
//  SpecularGI Sampling — reflection rays and cubemap radiance.
// ═══════════════════════════════════════════════════════════════
#ifndef SPECULAR_GI_SAMPLING_INCLUDED
#define SPECULAR_GI_SAMPLING_INCLUDED

float2 SpecularGI_Hash22(float2 value)
{
    float3 p = frac(float3(value.xyx) * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yzx + 33.33);
    return frac((p.xx + p.yz) * p.zy);
}

float3 SpecularGI_SampleGGXVNDF(float2 randomValue, float roughness, float3 normalWS, float3 viewToCameraWS)
{
    float3 tangentWS = abs(normalWS.y) < 0.999
        ? normalize(cross(float3(0.0, 1.0, 0.0), normalWS))
        : float3(1.0, 0.0, 0.0);
    float3 bitangentWS = cross(normalWS, tangentWS);
    float3 viewLocal = float3(
        dot(viewToCameraWS, tangentWS),
        dot(viewToCameraWS, bitangentWS),
        dot(viewToCameraWS, normalWS));

    float alpha = max(roughness * roughness, 0.0025);
    float3 viewHemisphere = normalize(float3(alpha * viewLocal.xy, max(viewLocal.z, 0.0001)));
    float lensSquared = dot(viewHemisphere.xy, viewHemisphere.xy);
    float3 tangent1 = lensSquared > 0.0001
        ? float3(-viewHemisphere.y, viewHemisphere.x, 0.0) * rsqrt(lensSquared)
        : float3(1.0, 0.0, 0.0);
    float3 tangent2 = cross(viewHemisphere, tangent1);

    float radius = sqrt(randomValue.x);
    float phi = TWO_PI * randomValue.y;
    float sampleX = radius * cos(phi);
    float sampleY = radius * sin(phi);
    float interpolation = 0.5 * (1.0 + viewHemisphere.z);
    sampleY = lerp(sqrt(saturate(1.0 - sampleX * sampleX)), sampleY, interpolation);

    float3 normalHemisphere = sampleX * tangent1
        + sampleY * tangent2
        + sqrt(saturate(1.0 - sampleX * sampleX - sampleY * sampleY)) * viewHemisphere;
    float3 halfLocal = normalize(float3(alpha * normalHemisphere.xy, max(normalHemisphere.z, 0.0)));
    return normalize(
        tangentWS * halfLocal.x
        + bitangentWS * halfLocal.y
        + normalWS * halfLocal.z);
}

float3 SpecularGI_CreateRayDirection(float2 uv, float3 normalWS, float3 viewDirectionWS)
{
    if (_Roughness <= 0.02)
        return normalize(reflect(viewDirectionWS, normalWS));

    float2 randomValue = SpecularGI_Hash22(uv * _ScreenParams.xy + _FrameIndex * float2(0.754877, 0.569840));
    float3 halfVectorWS = SpecularGI_SampleGGXVNDF(randomValue, _Roughness, normalWS, -viewDirectionWS);
    return normalize(reflect(viewDirectionWS, halfVectorWS));
}

float3 SpecularGI_SampleSky(float3 directionWS)
{
    float mipLevel = saturate(_Roughness) * max(_SkyMaxMip, 0.0);
    return SAMPLE_TEXTURECUBE_LOD(_SkyCubemap, sampler_SkyCubemap, directionWS, mipLevel).rgb;
}

#endif
