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
    float eyeDepth;
    uint status;
    uint steps;
    uint depthQueries;
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
//  Trace — shared-library adapter; callers include RayStep and ScreenTrace first.
// ════════════════════════════════════════════════════════════════
#if defined(SCREENSPACETRACEFUNCTION_HLSL_INCLUDED)
ScreenTraceContext SST_CreateTraceContext()
{
    ScreenTraceContext context = (ScreenTraceContext)0;
    context.worldToView = UNITY_MATRIX_V;
    context.viewToClip = UNITY_MATRIX_P;
    #if UNITY_UV_STARTS_AT_TOP
        context.uvScaleBias = float4(0.5, -0.5, 0.5, 0.5);
    #else
        context.uvScaleBias = float4(0.5, 0.5, 0.5, 0.5);
    #endif
    context.depthTextureSize = _CameraDepthTexture_TexelSize.zw;
    context.depthSize = max(round(context.depthTextureSize * _RTHandleScale.xy), 1.0);
    context.depthUvScaleBias = float4(_RTHandleScale.xy, 0.0, 0.0);
    context.zBufferParams = _ZBufferParams;
    context.clipPlanes = _ProjectionParams.yz;
    context.orthographic = unity_OrthoParams.w > 0.5 ? 1u : 0u;
    #if UNITY_REVERSED_Z
        context.reversedZ = 1u;
    #endif
    return context;
}

bool SST_Trace(float3 originWS, float3 directionWS, float maxDistance,
    float thickness, int stepCount, out ScreenSpaceTraceHit hit)
{
    hit = (ScreenSpaceTraceHit)0;
    hit.status = SCREEN_TRACE_INVALID;
    RayStepRay ray;
    if (!RayStep_CreateRay(originWS, directionWS, float2(0.0, maxDistance), ray))
        return false;
    ScreenTraceConfig config;
    config.maxSteps = stepCount;
    config.refinementSteps = 6;
    config.thickness = thickness;
    ScreenTraceContext context = SST_CreateTraceContext();
    ScreenTraceHit geometry;
    bool found;
    #if defined(SSGI_TRACE_RAY)
        found = ScreenTrace_TraceRay(ray, context, config, geometry);
    #else
        found = ScreenTrace_TraceDDA(ray, context, config, geometry);
    #endif
    hit.uv = geometry.uv;
    hit.distance = geometry.distance;
    hit.eyeDepth = geometry.eyeDepth;
    hit.status = geometry.status;
    hit.steps = geometry.steps;
    hit.depthQueries = geometry.depthQueries;
    return found;
}
#endif

#endif
