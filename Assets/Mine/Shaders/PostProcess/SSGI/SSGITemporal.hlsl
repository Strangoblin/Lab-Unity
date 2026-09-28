// ════════════════════════════════════════════════════════════════
//  SSGI temporal reconstruction — motion reprojection and depth rejection.
// ════════════════════════════════════════════════════════════════
#ifndef SSGI_TEMPORAL_INCLUDED
#define SSGI_TEMPORAL_INCLUDED

float4 SSGITemporal_Resolve(float2 uv)
{
    float4 current = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    if (_SSGIHistoryValid < 0.5)
    {
        if (_SSGIStoreWeight > 0.5)
            current.a = 0.0;
        return current;
    }

    float2 motion = SAMPLE_TEXTURE2D_X_LOD(_SSGIMotionTexture, sampler_LinearClamp, uv, 0).xy;
    float2 historyUV = uv - motion;
    if (any(historyUV <= 0.0) || any(historyUV >= 1.0))
    {
        if (_SSGIStoreWeight > 0.5)
            current.a = 0.0;
        return current;
    }

    float rawDepth = SST_SampleDepth(uv);
    if (!SST_IsSurface(rawDepth))
    {
        if (_SSGIStoreWeight > 0.5)
            current.a = 0.0;
        return current;
    }

    float3 positionWS = SST_WorldPosition(uv, rawDepth);
    float expectedDepth = -mul(_SSGIPreviousViewMatrix, float4(positionWS, 1.0)).z;
    float historyDepth = SAMPLE_TEXTURE2D_X_LOD(
        _SSGIHistoryDepth, sampler_PointClamp, historyUV, 0).r;
    float4 history = SAMPLE_TEXTURE2D_X_LOD(
        _SSGIHistoryColor, sampler_LinearClamp, historyUV, 0);

    float threshold = max(0.05, expectedDepth * 0.05);
    float confidence = 1.0 - saturate(abs(expectedDepth - historyDepth) / threshold);
    float weight = _SSGITemporalBlend * confidence;
    float4 result = lerp(current, history, weight);
    if (_SSGIStoreWeight > 0.5)
        result.a = weight;
    return result;
}

#endif
