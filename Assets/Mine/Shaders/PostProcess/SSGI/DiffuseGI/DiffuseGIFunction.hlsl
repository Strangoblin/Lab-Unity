// ════════════════════════════════════════════════════════════════
//  DiffuseGI — cosine hemisphere gather and geometry-guided spatial filtering.
// ════════════════════════════════════════════════════════════════
#ifndef DIFFUSE_GI_FUNCTION_INCLUDED
#define DIFFUSE_GI_FUNCTION_INCLUDED

// ════════════════════════════════════════════════════════════════
//  Normal sample — explicit mip zero with URP stereo UVs and oct decoding.
// ════════════════════════════════════════════════════════════════
float3 DiffuseGI_SampleNormal(float2 uv)
{
    uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraNormalsTexture_TexelSize.xy);
    float3 normalWS = SAMPLE_TEXTURE2D_X_LOD(_CameraNormalsTexture, sampler_PointClamp, uv, 0).xyz;
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 octNormalWS = Unpack888ToFloat2(normalWS) * 2.0 - 1.0;
        normalWS = UnpackNormalOctQuadEncode(octNormalWS);
    #endif
    return normalWS;
}

// ════════════════════════════════════════════════════════════════
//  Pixel rotation with frame sequence when temporal history is active.
// ════════════════════════════════════════════════════════════════
float2 DiffuseGI_Noise(float2 pixel)
{
    float3 value = frac(float3(pixel.xyx) * float3(0.1031, 0.1030, 0.0973));
    value += dot(value, value.yzx + 33.33);
    return frac((value.xx + value.yz) * value.zy
        + float2(_DiffuseGIFrameIndex * 0.75487766, _DiffuseGIFrameIndex * 0.5698403));
}

// ════════════════════════════════════════════════════════════════
//  Cosine hemisphere — orthonormal world frame around the receiver normal.
// ════════════════════════════════════════════════════════════════
float3 DiffuseGI_Direction(float2 xi, float3 normalWS)
{
    float3 referenceAxis = abs(normalWS.z) < 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
    float3 tangentWS = normalize(cross(referenceAxis, normalWS));
    float3 bitangentWS = cross(normalWS, tangentWS);
    float radius = sqrt(xi.x);
    float angle = TWO_PI * xi.y;
    return tangentWS * (radius * cos(angle)) + bitangentWS * (radius * sin(angle))
        + normalWS * sqrt(max(1.0 - xi.x, 0.0));
}

// ════════════════════════════════════════════════════════════════
//  Gather — average all rays (including misses); alpha stores confidence only.
// ════════════════════════════════════════════════════════════════
float4 DiffuseGI_Gather(float2 uv)
{
    float rawDepth = SST_SampleDepth(uv);
    float3 normalWS = DiffuseGI_SampleNormal(uv);
    if (!SST_IsSurface(rawDepth) || dot(normalWS, normalWS) < 0.25)
        return 0.0;
    normalWS = normalize(normalWS);
    float3 originWS = SST_WorldPosition(uv, rawDepth) + normalWS * _GITraceParams.z;
    float2 rotation = DiffuseGI_Noise(floor(uv * _GISourceSize.zw));
    float4 sum = 0.0;
    int rayCount = clamp(_GIRayCount, 1, 8);
    [loop]
    for (int rayIndex = 0; rayIndex < rayCount; ++rayIndex)
    {
        float2 xi = frac(float2((rayIndex + 0.5) / rayCount, rayIndex * 0.61803398875) + rotation);
        float3 directionWS = DiffuseGI_Direction(xi, normalWS);
        ScreenSpaceTraceHit hit;
        if (!SST_Trace(originWS, directionWS, _GITraceParams.x, _GITraceParams.y, _GIStepCount, hit))
            continue;
        float3 hitNormalWS = DiffuseGI_SampleNormal(hit.uv);
        if (dot(hitNormalWS, -directionWS) <= 0.0 || hit.distance < _GITraceParams.z)
            continue;
        float2 edge = abs(hit.uv * 2.0 - 1.0);
        float confidence = saturate(1.0 - pow(max(edge.x, edge.y), 4.0));
        float attenuation = rcp(1.0 + hit.distance * hit.distance * max(_GITraceParams.w, 0.0));
        float3 radiance = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, hit.uv, 0).rgb;
        sum += float4(max(radiance, 0.0) * confidence * attenuation, confidence);
    }
    return sum / rayCount;
}

// ════════════════════════════════════════════════════════════════
//  Guide weight — eye-depth and world-normal rejection, independent of alpha.
// ════════════════════════════════════════════════════════════════
float DiffuseGI_GuideWeight(float2 sampleUV, float centerDepth, float3 centerNormal)
{
    float rawDepth = SST_SampleDepth(sampleUV);
    float3 sampleNormal = DiffuseGI_SampleNormal(sampleUV);
    if (!SST_IsSurface(rawDepth) || dot(sampleNormal, sampleNormal) < 0.25)
        return 0.0;
    float depthDifference = abs(SST_EyeDepth(rawDepth) - centerDepth);
    float depthWeight = exp2(-depthDifference / max(_GIFilterParams.x, 0.0001));
    float normalWeight = pow(saturate(dot(centerNormal, normalize(sampleNormal))), max(_GIFilterParams.y, 1.0));
    return depthWeight * normalWeight;
}

// ════════════════════════════════════════════════════════════════
//  Bilateral blur — five point taps, normalized by geometry and spatial weights.
// ════════════════════════════════════════════════════════════════
float4 DiffuseGI_Blur(float2 uv, float2 axis)
{
    float rawDepth = SST_SampleDepth(uv);
    float3 normalWS = DiffuseGI_SampleNormal(uv);
    if (!SST_IsSurface(rawDepth) || dot(normalWS, normalWS) < 0.25)
        return 0.0;
    normalWS = normalize(normalWS);
    float centerDepth = SST_EyeDepth(rawDepth);
    float4 sum = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int tap = -2; tap <= 2; ++tap)
    {
        float2 sampleUV = uv + axis * _GISourceSize.xy * tap;
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            continue;
        float weight = exp2(-0.5 * tap * tap) * DiffuseGI_GuideWeight(sampleUV, centerDepth, normalWS);
        sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, sampleUV, 0) * weight;
        totalWeight += weight;
    }
    float4 blurred = sum / max(totalWeight, 0.000001);
    if (_GIFilterParams.z >= 0.999)
        return blurred;
    float4 center = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
    return lerp(center, blurred, saturate(_GIFilterParams.z));
}

// ════════════════════════════════════════════════════════════════
//  Resolve — depth/normal-guided four-tap upsample, zero if no compatible guide.
// ════════════════════════════════════════════════════════════════
float4 DiffuseGI_Resolve(float2 uv)
{
    float rawDepth = SST_SampleDepth(uv);
    float3 normalWS = DiffuseGI_SampleNormal(uv);
    if (!SST_IsSurface(rawDepth) || dot(normalWS, normalWS) < 0.25)
        return 0.0;
    normalWS = normalize(normalWS);
    float centerDepth = SST_EyeDepth(rawDepth);
    float2 pixel = uv * _GISourceSize.zw - 0.5;
    float2 basePixel = floor(pixel);
    float2 fraction = frac(pixel);
    float4 sum = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int tap = 0; tap < 4; ++tap)
    {
        float2 offset = float2(tap & 1, tap >> 1);
        float2 sampleUV = (basePixel + offset + 0.5) * _GISourceSize.xy;
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            continue;
        float2 bilinear = lerp(1.0 - fraction, fraction, offset);
        float weight = bilinear.x * bilinear.y * DiffuseGI_GuideWeight(sampleUV, centerDepth, normalWS);
        sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, sampleUV, 0) * weight;
        totalWeight += weight;
    }
    return totalWeight > 0.0001 ? sum / totalWeight : 0.0;
}

#endif
