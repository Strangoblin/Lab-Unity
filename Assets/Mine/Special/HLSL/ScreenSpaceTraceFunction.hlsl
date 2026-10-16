// ═══════════════════════════════════════════════════════════════
//  ScreenSpaceTraceFunction — SSR-style RAY / incremental DDA / mip-stride HiZ queries.
//  Include Core and RayStepFunction first, or override both depth-load macros.
//  No color, BRDF, normal bias, confidence or temporal policy is applied here.
// ═══════════════════════════════════════════════════════════════
#ifndef SCREENSPACETRACEFUNCTION_HLSL_INCLUDED
#define SCREENSPACETRACEFUNCTION_HLSL_INCLUDED

#ifndef SCREEN_TRACE_LOAD_DEPTH
TEXTURE2D_X(_ScreenTraceDepth);
#define SCREEN_TRACE_LOAD_DEPTH(pixel) LOAD_TEXTURE2D_X(_ScreenTraceDepth, pixel).r
#endif

#ifndef SCREEN_TRACE_LOAD_PYRAMID
TEXTURE2D_X(_ScreenTraceDepthPyramid);
#define SCREEN_TRACE_LOAD_PYRAMID(pixel, mip) LOAD_TEXTURE2D_X_LOD(_ScreenTraceDepthPyramid, pixel, mip).r
#endif

static const uint SCREEN_TRACE_HIT = 0;
static const uint SCREEN_TRACE_MISS = 1;
static const uint SCREEN_TRACE_LEFT_VIEW = 2;
static const uint SCREEN_TRACE_BUDGET = 3;
static const uint SCREEN_TRACE_INVALID = 4;
static const uint SCREEN_TRACE_UNAVAILABLE = 5;

struct ScreenTraceContext
{
    float4x4 worldToView;
    float4x4 viewToClip;
    float4 uvScaleBias;
    float4 depthUvScaleBias;
    float4 zBufferParams;
    float2 clipPlanes;
    float2 depthSize;
    float2 depthTextureSize;
    uint depthIsLinear;
    uint orthographic;
    uint reversedZ;
    int pyramidMipCount;
};

struct ScreenTraceConfig
{
    int maxSteps;
    int refinementSteps;
    float thickness;
};

struct ScreenTraceHit
{
    float2 uv;
    float distance;
    float eyeDepth;
    uint status;
    uint steps;
    uint depthQueries;
};

bool ScreenTrace_Validate(RayStepRay ray, ScreenTraceContext context, ScreenTraceConfig config,
    out ScreenTraceHit hit)
{
    hit = (ScreenTraceHit)0;
    hit.status = SCREEN_TRACE_INVALID;
    return all(isfinite(ray.origin)) && all(isfinite(ray.direction)) && all(isfinite(ray.distanceRange))
        && abs(dot(ray.direction, ray.direction) - 1.0) < 0.001
        && ray.distanceRange.x >= 0.0 && ray.distanceRange.y > ray.distanceRange.x
        && all(isfinite(context.worldToView[0])) && all(isfinite(context.worldToView[1]))
        && all(isfinite(context.worldToView[2])) && all(isfinite(context.worldToView[3]))
        && all(isfinite(context.viewToClip[0])) && all(isfinite(context.viewToClip[1]))
        && all(isfinite(context.viewToClip[2])) && all(isfinite(context.viewToClip[3]))
        && all(isfinite(context.depthSize)) && all(isfinite(context.depthTextureSize))
        && all(context.depthSize == floor(context.depthSize))
        && all(context.depthTextureSize == floor(context.depthTextureSize))
        && all(isfinite(context.clipPlanes)) && all(isfinite(context.zBufferParams))
        && all(isfinite(context.uvScaleBias)) && all(isfinite(context.depthUvScaleBias))
        && all(abs(context.uvScaleBias.xy) > 0.0) && all(abs(context.depthUvScaleBias.xy) > 0.0)
        && all(context.depthSize >= 1.0) && all(context.depthTextureSize >= 1.0)
        && context.clipPlanes.x > 0.0 && context.clipPlanes.y > context.clipPlanes.x
        && config.maxSteps > 0 && config.refinementSteps >= 0 && config.refinementSteps <= 16
        && isfinite(config.thickness) && config.thickness >= 0.0;
}

bool ScreenTrace_ProjectPoint(float3 positionWS, ScreenTraceContext context, out float2 uv, out float eyeDepth)
{
    float3 positionVS = mul(context.worldToView, float4(positionWS, 1.0)).xyz;
    float4 positionCS = mul(context.viewToClip, float4(positionVS, 1.0));
    eyeDepth = -positionVS.z;
    uv = 0.0;
    if (!all(isfinite(positionCS)) || positionCS.w <= 0.00001
        || eyeDepth < context.clipPlanes.x || eyeDepth > context.clipPlanes.y)
        return false;
    uv = positionCS.xy / positionCS.w * context.uvScaleBias.xy + context.uvScaleBias.zw;
    return all(uv > 0.0) && all(uv < 1.0);
}

bool ScreenTrace_LoadDepth(int2 pixel, ScreenTraceContext context, out float eyeDepth)
{
    float2 depthUV = ((float2)pixel + 0.5) / context.depthSize;
    depthUV = depthUV * context.depthUvScaleBias.xy + context.depthUvScaleBias.zw;
    int2 sourcePixel = (int2)floor(depthUV * context.depthTextureSize);
    eyeDepth = context.clipPlanes.y;
    if (any(sourcePixel < 0) || any(sourcePixel >= (int2)context.depthTextureSize))
        return false;
    float depth = SCREEN_TRACE_LOAD_DEPTH(sourcePixel);
    if (!isfinite(depth) || (context.depthIsLinear == 0 && (depth < 0.0 || depth > 1.0)))
        return false;
    if (context.depthIsLinear != 0)
    {
        eyeDepth = depth;
        return depth > 0.0 && depth < context.clipPlanes.y;
    }
    bool valid = context.reversedZ != 0 ? depth > 0.000001 : depth < 0.999999;
    if (!valid)
        return false;
    eyeDepth = context.orthographic != 0
        ? lerp(context.clipPlanes.x, context.clipPlanes.y, context.reversedZ != 0 ? 1.0 - depth : depth)
        : rcp(context.zBufferParams.z * depth + context.zBufferParams.w);
    return isfinite(eyeDepth) && eyeDepth > 0.0;
}

bool ScreenTrace_LoadUV(float2 uv, ScreenTraceContext context, out float eyeDepth)
{
    return ScreenTrace_LoadDepth(clamp((int2)floor(uv * context.depthSize), int2(0, 0),
        (int2)context.depthSize - 1), context, eyeDepth);
}

bool ScreenTrace_OriginInFront(RayStepRay ray, ScreenTraceContext context, inout ScreenTraceHit hit)
{
    float2 uv;
    float rayDepth;
    if (!ScreenTrace_ProjectPoint(RayStep_Position(ray, ray.distanceRange.x), context, uv, rayDepth))
        return true;
    float sceneDepth;
    hit.depthQueries++;
    return !ScreenTrace_LoadUV(uv, context, sceneDepth)
        || rayDepth < sceneDepth - max(1.0, sceneDepth) * 0.000001;
}

bool ScreenTrace_TraceRay(RayStepRay ray, ScreenTraceContext context, ScreenTraceConfig config,
    out ScreenTraceHit hit)
{
    if (!ScreenTrace_Validate(ray, context, config, hit))
        return false;
    hit.status = SCREEN_TRACE_MISS;
    float2 uv;
    float rayDepth;
    if (!ScreenTrace_ProjectPoint(RayStep_Position(ray, ray.distanceRange.x), context, uv, rayDepth))
    {
        hit.status = SCREEN_TRACE_LEFT_VIEW;
        return false;
    }
    float sceneDepth;
    hit.depthQueries++;
    bool surface = ScreenTrace_LoadUV(uv, context, sceneDepth);
    bool previousInFront = surface && rayDepth < sceneDepth;
    if (surface && !previousInFront)
        return false;
    float previousDistance = ray.distanceRange.x;
    float stepLength = (ray.distanceRange.y - ray.distanceRange.x) / config.maxSteps;
    [loop]
    for (int stepIndex = 1; stepIndex <= config.maxSteps; ++stepIndex)
    {
        hit.steps++;
        float distance = ray.distanceRange.x + stepLength * stepIndex;
        if (!ScreenTrace_ProjectPoint(RayStep_Position(ray, distance), context, uv, rayDepth))
        {
            hit.status = SCREEN_TRACE_LEFT_VIEW;
            return false;
        }
        hit.depthQueries++;
        surface = ScreenTrace_LoadUV(uv, context, sceneDepth);
        float delta = rayDepth - sceneDepth;
        if (surface && delta >= 0.0)
        {
            if (!previousInFront)
                return false;
            float lowDistance = previousDistance;
            float highDistance = distance;
            [loop]
            for (int refineIndex = 0; refineIndex < config.refinementSteps; ++refineIndex)
            {
                float middleDistance = (lowDistance + highDistance) * 0.5;
                float2 middleUV;
                float middleDepth;
                if (!ScreenTrace_ProjectPoint(RayStep_Position(ray, middleDistance), context, middleUV, middleDepth))
                {
                    hit.status = SCREEN_TRACE_LEFT_VIEW;
                    return false;
                }
                hit.depthQueries++;
                float middleSceneDepth;
                if (ScreenTrace_LoadUV(middleUV, context, middleSceneDepth) && middleDepth >= middleSceneDepth)
                    highDistance = middleDistance;
                else
                    lowDistance = middleDistance;
            }
            if (!ScreenTrace_ProjectPoint(RayStep_Position(ray, highDistance), context, uv, rayDepth))
            {
                hit.status = SCREEN_TRACE_LEFT_VIEW;
                return false;
            }
            hit.depthQueries++;
            surface = ScreenTrace_LoadUV(uv, context, sceneDepth);
            delta = rayDepth - sceneDepth;
            if (!surface || delta < 0.0 || delta > config.thickness)
                return false;
            hit.uv = uv;
            hit.distance = highDistance;
            hit.eyeDepth = rayDepth;
            hit.status = SCREEN_TRACE_HIT;
            return true;
        }
        previousInFront = surface && delta < 0.0;
        previousDistance = distance;
    }
    return false;
}

bool ScreenTrace_SetHit(RayStepDDAState state,
    ScreenTraceContext context, inout ScreenTraceHit hit)
{
    hit.uv = state.pixel / context.depthSize;
    hit.distance = state.weightedDistance / state.reciprocalW;
    hit.eyeDepth = state.weightedEyeDepth / state.reciprocalW;
    hit.status = SCREEN_TRACE_HIT;
    return true;
}

uint ScreenTrace_EndStatus(RayStepProjectedRay projected, RayStepRay ray)
{
    return RayStep_Distance(projected, projected.progressRange.y) < ray.distanceRange.y - 0.0001
        ? SCREEN_TRACE_LEFT_VIEW : SCREEN_TRACE_MISS;
}

bool ScreenTrace_PrepareDDA(RayStepRay ray, ScreenTraceContext context,
    out RayStepProjectedRay projected, inout ScreenTraceHit hit)
{
    projected = (RayStepProjectedRay)0;
    hit.status = SCREEN_TRACE_MISS;
    if (!ScreenTrace_OriginInFront(ray, context, hit))
        return false;
    if (!RayStep_ProjectRay(ray, context.worldToView, context.viewToClip, context.uvScaleBias,
        context.clipPlanes, context.depthSize, projected))
    {
        hit.status = SCREEN_TRACE_LEFT_VIEW;
        return false;
    }
    return true;
}

bool ScreenTrace_RefineDDA(RayStepDDAState low, RayStepDDAState high,
    ScreenTraceContext context, ScreenTraceConfig config, inout ScreenTraceHit hit)
{
    [loop]
    for (int refineIndex = 0; refineIndex < config.refinementSteps; ++refineIndex)
    {
        RayStepDDAState middle = RayStep_MidpointDDA(low, high);
        float sceneDepth;
        hit.depthQueries++;
        if (ScreenTrace_LoadUV(middle.pixel / context.depthSize, context, sceneDepth)
            && middle.weightedEyeDepth / middle.reciprocalW >= sceneDepth)
            high = middle;
        else
            low = middle;
    }
    RayStepDDAState candidate = high;
    float sceneDepth;
    hit.depthQueries++;
    if (!ScreenTrace_LoadUV(candidate.pixel / context.depthSize, context, sceneDepth))
        return false;
    float delta = candidate.weightedEyeDepth / candidate.reciprocalW - sceneDepth;
    if (delta < 0.0 || delta > config.thickness)
        return false;
    return ScreenTrace_SetHit(candidate, context, hit);
}

bool ScreenTrace_TraceDDA(RayStepRay ray, ScreenTraceContext context, ScreenTraceConfig config,
    out ScreenTraceHit hit)
{
    hit = (ScreenTraceHit)0;
    if (!ScreenTrace_Validate(ray, context, config, hit))
        return false;
    RayStepProjectedRay projected;
    if (!ScreenTrace_PrepareDDA(ray, context, projected, hit))
        return false;
    RayStepDDAState state;
    RayStepDDAState increment;
    RayStep_CreateDDA(projected, config.maxSteps, state, increment);
    float sceneDepth;
    hit.depthQueries++;
    bool surface = ScreenTrace_LoadUV(state.pixel / context.depthSize, context, sceneDepth);
    bool previousInFront = surface && state.weightedEyeDepth / state.reciprocalW < sceneDepth;
    if (surface && !previousInFront)
        return false;
    [loop]
    for (int stepIndex = 0; stepIndex < config.maxSteps; ++stepIndex)
    {
        RayStepDDAState previous = state;
        RayStep_AdvanceDDA(state, increment, 1.0);
        if (stepIndex == config.maxSteps - 1)
            state = RayStep_DDAAt(projected, projected.progressRange.y);
        hit.steps++;
        hit.depthQueries++;
        surface = ScreenTrace_LoadUV(state.pixel / context.depthSize, context, sceneDepth);
        float delta = state.weightedEyeDepth / state.reciprocalW - sceneDepth;
        if (surface && delta >= 0.0)
        {
            if (!previousInFront)
                return false;
            return ScreenTrace_RefineDDA(previous, state, context, config, hit);
        }
        previousInFront = surface && delta < 0.0;
    }
    hit.status = ScreenTrace_EndStatus(projected, ray);
    return false;
}

bool ScreenTrace_TraceHiZ(RayStepRay ray, ScreenTraceContext context, ScreenTraceConfig config,
    out ScreenTraceHit hit)
{
    hit = (ScreenTraceHit)0;
    if (!ScreenTrace_Validate(ray, context, config, hit))
        return false;
    if (context.pyramidMipCount <= 0 || context.pyramidMipCount > 1 + (int)floor(log2(max(context.depthSize.x, context.depthSize.y))))
    {
        hit.status = SCREEN_TRACE_UNAVAILABLE;
        return false;
    }
    RayStepProjectedRay projected;
    if (!ScreenTrace_PrepareDDA(ray, context, projected, hit))
        return false;
    RayStepDDAState state;
    RayStepDDAState increment;
    RayStep_CreateDDA(projected, config.maxSteps, state, increment);
    float sceneDepth;
    hit.depthQueries++;
    bool surface = ScreenTrace_LoadUV(state.pixel / context.depthSize, context, sceneDepth);
    bool previousInFront = surface && state.weightedEyeDepth / state.reciprocalW < sceneDepth;
    if (surface && !previousInFront)
        return false;
    int mip = 0;
    float cursor = 0.0;
    [loop]
    for (int stepIndex = 0; stepIndex < config.maxSteps; ++stepIndex)
    {
        RayStepDDAState previous = state;
        float stride = min(exp2((float)mip), (float)config.maxSteps - cursor);
        RayStep_AdvanceDDA(state, increment, stride);
        if (cursor + stride >= config.maxSteps)
            state = RayStep_DDAAt(projected, projected.progressRange.y);
        hit.steps++;
        hit.depthQueries++;
        if (mip == 0)
            surface = ScreenTrace_LoadUV(state.pixel / context.depthSize, context, sceneDepth);
        else
        {
            int2 mipSize = max(int2(1, 1), (int2)context.depthSize >> mip);
            int2 pixel = clamp((int2)floor(state.pixel / context.depthSize * mipSize), int2(0, 0), mipSize - 1);
            sceneDepth = SCREEN_TRACE_LOAD_PYRAMID(pixel, mip);
            surface = isfinite(sceneDepth) && sceneDepth > 0.0 && sceneDepth < context.clipPlanes.y;
        }
        float delta = state.weightedEyeDepth / state.reciprocalW - sceneDepth;
        if (surface && delta >= 0.0)
        {
            if (mip > 0)
            {
                state = previous;
                mip--;
                continue;
            }
            if (!previousInFront)
                return false;
            return ScreenTrace_RefineDDA(previous, state, context, config, hit);
        }
        previousInFront = surface && delta < 0.0;
        cursor += stride;
        if (cursor >= config.maxSteps)
        {
            hit.status = ScreenTrace_EndStatus(projected, ray);
            return false;
        }
        mip = min(mip + 1, context.pyramidMipCount - 1);
    }
    hit.status = SCREEN_TRACE_BUDGET;
    return false;
}

#endif
