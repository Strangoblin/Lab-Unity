// ═══════════════════════════════════════════════════════════════
//  SpecularGI Filter — geometry-aware spatial and temporal reconstruction.
// ═══════════════════════════════════════════════════════════════
#ifndef SPECULAR_GI_FILTER_INCLUDED
#define SPECULAR_GI_FILTER_INCLUDED

float4 SpecularGI_SpatialResolve(float2 uv)
{
    float rawDepth = SST_SampleDepth(uv);
    if (!SST_IsSurface(rawDepth))
        return 0.0;

    float centerDepth = SST_EyeDepth(rawDepth);
    float3 centerNormal = SampleSceneNormals(uv);
    float4 center = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    float4 accumulated = 0.0;
    float totalWeight = 0.0;

    [unroll]
    for (int sampleY = -2; sampleY <= 2; ++sampleY)
    {
        [unroll]
        for (int sampleX = -2; sampleX <= 2; ++sampleX)
        {
            float2 offset = float2(sampleX, sampleY);
            float2 sampleUV = saturate(
                uv + offset * _BlitTexture_TexelSize.xy * _SpatialRadius);
            float sampleRawDepth = SST_SampleDepth(sampleUV);
            if (!SST_IsSurface(sampleRawDepth))
                continue;

            float4 sampleValue = SAMPLE_TEXTURE2D_X_LOD(
                _BlitTexture,
                sampler_LinearClamp,
                sampleUV,
                0);
            float sampleDepth = SST_EyeDepth(sampleRawDepth);
            float3 sampleNormal = SampleSceneNormals(sampleUV);
            float depthWeight = exp2(-abs(sampleDepth - centerDepth) * 2.0);
            float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), 32.0);
            float spatialWeight = rcp(1.0 + dot(offset, offset));
            float weight = depthWeight * normalWeight * spatialWeight;
            accumulated += sampleValue * weight;
            totalWeight += weight;
        }
    }

    float4 resolved = totalWeight > 0.0001 ? accumulated / totalWeight : center;
    resolved.a = center.a;
    return resolved;
}

float4 SpecularGI_Upsample(float2 uv)
{
    float rawDepth = SST_SampleDepth(uv);
    if (!SST_IsSurface(rawDepth))
        return 0.0;

    float centerDepth = SST_EyeDepth(rawDepth);
    float3 centerNormal = normalize(SampleSceneNormals(uv));
    float2 pixel = uv * _BlitTexture_TexelSize.zw - 0.5;
    float2 basePixel = floor(pixel);
    float2 fraction = frac(pixel);
    float4 accumulated = 0.0;
    float totalWeight = 0.0;

    [unroll]
    for (int tap = 0; tap < 4; ++tap)
    {
        float2 offset = float2(tap & 1, tap >> 1);
        float2 sampleUV = (basePixel + offset + 0.5) * _BlitTexture_TexelSize.xy;
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            continue;

        float sampleRawDepth = SST_SampleDepth(sampleUV);
        if (!SST_IsSurface(sampleRawDepth))
            continue;

        float depthWeight = exp2(-abs(SST_EyeDepth(sampleRawDepth) - centerDepth) * 2.0);
        float normalWeight = pow(saturate(dot(centerNormal, normalize(SampleSceneNormals(sampleUV)))), 32.0);
        float2 bilinear = lerp(1.0 - fraction, fraction, offset);
        float weight = bilinear.x * bilinear.y * depthWeight * normalWeight;
        accumulated += SAMPLE_TEXTURE2D_X_LOD(
            _BlitTexture, sampler_PointClamp, sampleUV, 0) * weight;
        totalWeight += weight;
    }

    return totalWeight > 0.0001
        ? accumulated / totalWeight
        : SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
}

#endif
