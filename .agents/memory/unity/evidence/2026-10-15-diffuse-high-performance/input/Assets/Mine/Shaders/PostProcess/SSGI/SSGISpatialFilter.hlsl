// ═══════════════════════════════════════════════════════════════
//  SSGISpatialFilter — shared geometry-guided radiance reconstruction.
//  Caller supplies Core, Blit, scene normals and SST depth helpers.
// ═══════════════════════════════════════════════════════════════
#ifndef SSGI_SPATIAL_FILTER_INCLUDED
#define SSGI_SPATIAL_FILTER_INCLUDED

float4 SSGISpatial_Resolve(float2 uv, float2 sourceTexelSize, float radius,
    float centerDepth, float3 centerNormal, float inverseDepthSigma, float normalPower,
    bool normalizeSampleNormals, float4 fallbackValue)
{
    float4 accumulated = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int sampleY = -2; sampleY <= 2; ++sampleY)
    {
        [unroll]
        for (int sampleX = -2; sampleX <= 2; ++sampleX)
        {
            float2 offset = float2(sampleX, sampleY);
            float2 sampleUV = saturate(uv + offset * sourceTexelSize * radius);
            float sampleRawDepth = SST_SampleDepth(sampleUV);
            if (!SST_IsSurface(sampleRawDepth))
                continue;
            float4 sampleValue = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sampleUV, 0);
            float sampleDepth = SST_EyeDepth(sampleRawDepth);
            float3 sampleNormal = SampleSceneNormals(sampleUV);
            if (normalizeSampleNormals)
            {
                if (dot(sampleNormal, sampleNormal) < 0.25)
                    continue;
                sampleNormal = normalize(sampleNormal);
            }
            float depthWeight = exp2(-abs(sampleDepth - centerDepth) * inverseDepthSigma);
            float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), normalPower);
            float spatialWeight = rcp(1.0 + dot(offset, offset));
            float weight = depthWeight * normalWeight * spatialWeight;
            accumulated += sampleValue * weight;
            totalWeight += weight;
        }
    }
    return totalWeight > 0.0001 ? accumulated / totalWeight : fallbackValue;
}

#endif
