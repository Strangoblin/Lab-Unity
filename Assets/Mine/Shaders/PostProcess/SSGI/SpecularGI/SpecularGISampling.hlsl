// ═══════════════════════════════════════════════════════════════
//  SpecularGI Sampling — reflection rays, planar fallback and cubemap radiance.
// ═══════════════════════════════════════════════════════════════
#ifndef SPECULAR_GI_SAMPLING_INCLUDED
#define SPECULAR_GI_SAMPLING_INCLUDED

struct SpecularPlanarResult
{
    float3 radiance;
    float confidence;
};

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

SpecularPlanarResult SpecularGI_EvaluatePlanar(
    float3 positionWS,
    float3 normalWS,
    float3 viewDirectionWS,
    float viewDistance)
{
    SpecularPlanarResult result = (SpecularPlanarResult)0;
    if (unity_OrthoParams.w > 0.5)
        return result;

    float planarity = max(max(abs(normalWS.x), abs(normalWS.y)), abs(normalWS.z));
    float planarMask = smoothstep(min(_PlanarParams.x, 0.9999), 1.0, planarity);
    float distanceMask = smoothstep(_PlanarParams.y, max(_PlanarParams.z, _PlanarParams.y + 0.0001), viewDistance);
    if (planarMask * distanceMask <= 0.0001 || _PlanarParams.w <= 0.0)
        return result;

    float3 viewDirectionVS = mul((float3x3)_CameraViewMatrix, viewDirectionWS);
    float3 normalVS = normalize(mul((float3x3)_CameraViewMatrix, normalWS));
    float3 reflectedVS = reflect(viewDirectionVS, normalVS);
    float4 clip = mul(_CameraProjectionMatrix, float4(reflectedVS, 0.0));
    if (clip.w <= 0.00001)
        return result;

    float2 sampleUV = float2(clip.x, clip.y * _ProjectionParams.x) / clip.w * 0.5 + 0.5;
    float2 edge = abs(sampleUV * 2.0 - 1.0);
    float boundsConfidence = 1.0 - smoothstep(0.85, 1.0, max(edge.x, edge.y));
    result.radiance = SAMPLE_TEXTURE2D_X_LOD(
        _BlitTexture,
        sampler_LinearClamp,
        saturate(sampleUV),
        0).rgb;
    result.confidence = saturate(planarMask * distanceMask * boundsConfidence * _PlanarParams.w);
    return result;
}

float3 SpecularGI_SampleSky(float3 directionWS)
{
    float mipLevel = saturate(_Roughness) * max(_SkyMaxMip, 0.0);
    return SAMPLE_TEXTURECUBE_LOD(_SkyCubemap, sampler_SkyCubemap, directionWS, mipLevel).rgb;
}

#endif
