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

float4 SpecularGI_TemporalResolve(float2 uv)
{
    float4 current = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    if (_HistoryValid < 0.5)
        return current;

    float2 motion = _HasMotionVectors > 0.5
        ? SAMPLE_TEXTURE2D_X_LOD(_SpecularMotionTexture, sampler_LinearClamp, uv, 0).xy
        : 0.0;
    float2 historyUV = uv - motion;
    if (any(historyUV <= 0.0) || any(historyUV >= 1.0))
        return current;

    float rawDepth = SST_SampleDepth(uv);
    if (!SST_IsSurface(rawDepth))
        return current;

    float3 positionWS = SST_WorldPosition(uv, rawDepth);
    float expectedHistoryDepth = -mul(_PreviousViewMatrix, float4(positionWS, 1.0)).z;
    float historyDepth = SAMPLE_TEXTURE2D_X_LOD(
        _SpecularHistoryDepth,
        sampler_PointClamp,
        historyUV,
        0).r;
    float4 history = SAMPLE_TEXTURE2D_X_LOD(
        _SpecularHistoryColor,
        sampler_LinearClamp,
        historyUV,
        0);

    float depthThreshold = max(0.05, expectedHistoryDepth * 0.05);
    float depthConfidence = 1.0 - saturate(
        abs(expectedHistoryDepth - historyDepth) / depthThreshold);
    float historyWeight = _TemporalBlend * depthConfidence;
    float3 resolvedColor = lerp(current.rgb, history.rgb, historyWeight);
    return float4(resolvedColor, historyWeight);
}

#endif
