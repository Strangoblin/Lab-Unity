// ════════════════════════════════════════════════════════════════
//  ScreenSpaceTrace — geometric first-hit tracing, independent of BRDF.
// ════════════════════════════════════════════════════════════════
#ifndef SCREEN_SPACE_TRACE_INCLUDED
#define SCREEN_SPACE_TRACE_INCLUDED

// ════════════════════════════════════════════════════════════════
//  Depth sample — explicit mip zero, preserving URP stereo and RTHandle UVs.
// ════════════════════════════════════════════════════════════════
float SST_SampleDepth(float2 uv)
{
    uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraDepthTexture_TexelSize.xy);
    return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r;
}

struct ScreenSpaceTraceHit
{
    float2 uv;
    float distance;
};

// ════════════════════════════════════════════════════════════════
//  Depth helpers — raw depth, orthographic depth and world reconstruction.
// ════════════════════════════════════════════════════════════════
bool SST_IsSurface(float rawDepth)
{
    #if UNITY_REVERSED_Z
        return rawDepth > 0.000001;
    #else
        return rawDepth < 0.999999;
    #endif
}

float SST_EyeDepth(float rawDepth)
{
    return unity_OrthoParams.w > 0.5
        ? LinearDepthToEyeDepth(rawDepth)
        : LinearEyeDepth(rawDepth, _ZBufferParams);
}

float3 SST_WorldPosition(float2 uv, float rawDepth)
{
    #if !UNITY_REVERSED_Z
        rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
    #endif
    return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
}

// ════════════════════════════════════════════════════════════════
//  Project — reject near/far planes and unsafe homogeneous division.
// ════════════════════════════════════════════════════════════════
bool SST_Project(float3 positionWS, out float2 uv, out float eyeDepth)
{
    float4 positionCS = TransformWorldToHClip(positionWS);
    eyeDepth = -TransformWorldToView(positionWS).z;
    uv = 0.0;
    if (positionCS.w <= 0.00001 || eyeDepth < _ProjectionParams.y || eyeDepth > _ProjectionParams.z)
        return false;
    #if UNITY_UV_STARTS_AT_TOP
        positionCS.y = -positionCS.y;
    #endif
    uv = positionCS.xy / positionCS.w * 0.5 + 0.5;
    return all(uv > 0.0) && all(uv < 1.0);
}

// ════════════════════════════════════════════════════════════════
//  Trace — fixed world steps, sign-crossing bracket and binary refinement.
// ════════════════════════════════════════════════════════════════
bool SST_Trace(float3 originWS, float3 directionWS, float maxDistance,
    float thickness, int stepCount, out ScreenSpaceTraceHit hit)
{
    hit = (ScreenSpaceTraceHit)0;
    float2 uv;
    float eyeDepth;
    if (!SST_Project(originWS, uv, eyeDepth))
        return false;
    float rawDepth = SST_SampleDepth(uv);
    bool previousInFront = SST_IsSurface(rawDepth) && eyeDepth < SST_EyeDepth(rawDepth);
    if (SST_IsSurface(rawDepth) && !previousInFront)
        return false;
    float previousDistance = 0.0;
    float stepLength = maxDistance / max(stepCount, 1);
    [loop]
    for (int stepIndex = 1; stepIndex <= stepCount; ++stepIndex)
    {
        float rayDistance = stepLength * stepIndex;
        if (!SST_Project(originWS + directionWS * rayDistance, uv, eyeDepth))
            return false;
        rawDepth = SST_SampleDepth(uv);
        bool surface = SST_IsSurface(rawDepth);
        float delta = eyeDepth - SST_EyeDepth(rawDepth);
        if (surface && delta >= 0.0)
        {
            if (!previousInFront)
                return false;
            float lowDistance = previousDistance;
            float highDistance = rayDistance;
            [unroll]
            for (int refine = 0; refine < 6; ++refine)
            {
                float middleDistance = (lowDistance + highDistance) * 0.5;
                float2 middleUV;
                float middleEyeDepth;
                if (!SST_Project(originWS + directionWS * middleDistance, middleUV, middleEyeDepth))
                    return false;
                float middleRawDepth = SST_SampleDepth(middleUV);
                if (SST_IsSurface(middleRawDepth) && middleEyeDepth >= SST_EyeDepth(middleRawDepth))
                    highDistance = middleDistance;
                else
                    lowDistance = middleDistance;
            }
            if (!SST_Project(originWS + directionWS * highDistance, uv, eyeDepth))
                return false;
            rawDepth = SST_SampleDepth(uv);
            delta = eyeDepth - SST_EyeDepth(rawDepth);
            if (!SST_IsSurface(rawDepth) || delta < 0.0 || delta > thickness)
                return false;
            hit.uv = uv;
            hit.distance = highDistance;
            return true;
        }
        previousInFront = surface && delta < 0.0;
        previousDistance = rayDistance;
    }
    return false;
}

#endif
