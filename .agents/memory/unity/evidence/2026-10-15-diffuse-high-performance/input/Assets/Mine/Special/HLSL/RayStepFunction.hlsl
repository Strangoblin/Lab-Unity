// ═══════════════════════════════════════════════════════════════
//  RayStepFunction — ray projection and incremental homogeneous stepping.
//  Contract and units: Special/Rendering/ScreenSpaceTrace/README.md.
// ═══════════════════════════════════════════════════════════════
#ifndef RAYSTEPFUNCTION_HLSL_INCLUDED
#define RAYSTEPFUNCTION_HLSL_INCLUDED

struct RayStepRay
{
    float3 origin;
    float3 direction;
    float2 distanceRange;
};

struct RayStepProjectedRay
{
    float2 pixelStart;
    float2 pixelDelta;
    float3 weightedViewStart;
    float3 weightedViewDelta;
    float reciprocalWStart;
    float reciprocalWDelta;
    float weightedDistanceStart;
    float weightedDistanceDelta;
    float2 progressRange;
};

struct RayStepDDAState
{
    float2 pixel;
    float weightedEyeDepth;
    float weightedDistance;
    float reciprocalW;
    float progress;
};

bool RayStep_CreateRay(float3 origin, float3 direction, float2 distanceRange, out RayStepRay ray)
{
    ray = (RayStepRay)0;
    float lengthSquared = dot(direction, direction);
    if (!all(isfinite(origin)) || !all(isfinite(direction)) || !all(isfinite(distanceRange))
        || !isfinite(lengthSquared) || lengthSquared < 1e-12 || distanceRange.x < 0.0 || distanceRange.y <= distanceRange.x)
        return false;
    ray.origin = origin;
    ray.direction = direction * rsqrt(lengthSquared);
    ray.distanceRange = distanceRange;
    return true;
}

float3 RayStep_Position(RayStepRay ray, float distance)
{
    return ray.origin + ray.direction * distance;
}

float RayStep_UniformDistance(RayStepRay ray, int sampleIndex, int sampleCount)
{
    return ray.distanceRange.x + (ray.distanceRange.y - ray.distanceRange.x)
        / max(sampleCount, 1) * sampleIndex;
}

bool RayStep_ClipHalfspace(float originValue, float slope, inout float2 range)
{
    if (abs(slope) < 1e-12)
        return originValue >= 0.0;
    float boundary = -originValue / slope;
    if (slope > 0.0)
        range.x = max(range.x, boundary);
    else
        range.y = min(range.y, boundary);
    return range.x <= range.y;
}

bool RayStep_ClipInterval(float originValue, float slope, float2 limits, inout float2 range)
{
    return RayStep_ClipHalfspace(originValue - limits.x, slope, range)
        && RayStep_ClipHalfspace(limits.y - originValue, -slope, range);
}

float3 RayStep_ViewPosition(RayStepProjectedRay ray, float progress)
{
    return (ray.weightedViewStart + ray.weightedViewDelta * progress)
        / (ray.reciprocalWStart + ray.reciprocalWDelta * progress);
}

float RayStep_Distance(RayStepProjectedRay ray, float progress)
{
    return (ray.weightedDistanceStart + ray.weightedDistanceDelta * progress)
        / (ray.reciprocalWStart + ray.reciprocalWDelta * progress);
}

bool RayStep_ProjectRay(RayStepRay ray, float4x4 worldToView, float4x4 viewToClip,
    float4 uvScaleBias, float2 clipPlanes, float2 pixelSize, out RayStepProjectedRay projected)
{
    projected = (RayStepProjectedRay)0;
    float3 originVS = mul(worldToView, float4(ray.origin, 1.0)).xyz;
    float3 directionVS = mul(worldToView, float4(ray.direction, 0.0)).xyz;
    float2 distanceRange = ray.distanceRange;
    if (!RayStep_ClipInterval(-originVS.z, -directionVS.z, clipPlanes, distanceRange))
        return false;
    float3 startVS = originVS + directionVS * distanceRange.x;
    float3 endVS = originVS + directionVS * distanceRange.y;
    float4 startCS = mul(viewToClip, float4(startVS, 1.0));
    float4 endCS = mul(viewToClip, float4(endVS, 1.0));
    if (!all(isfinite(startCS)) || !all(isfinite(endCS)) || min(startCS.w, endCS.w) <= 1e-8)
        return false;
    float startReciprocalW = rcp(startCS.w);
    float endReciprocalW = rcp(endCS.w);
    projected.pixelStart = (startCS.xy * startReciprocalW * uvScaleBias.xy + uvScaleBias.zw) * pixelSize;
    float2 endPixel = (endCS.xy * endReciprocalW * uvScaleBias.xy + uvScaleBias.zw) * pixelSize;
    projected.pixelDelta = endPixel - projected.pixelStart;
    projected.weightedViewStart = startVS * startReciprocalW;
    projected.weightedViewDelta = endVS * endReciprocalW - projected.weightedViewStart;
    projected.reciprocalWStart = startReciprocalW;
    projected.reciprocalWDelta = endReciprocalW - startReciprocalW;
    projected.weightedDistanceStart = distanceRange.x * startReciprocalW;
    projected.weightedDistanceDelta = distanceRange.y * endReciprocalW - projected.weightedDistanceStart;
    projected.progressRange = float2(0.0, 1.0);
    if (!RayStep_ClipInterval(projected.pixelStart.x, projected.pixelDelta.x,
            float2(0.0, pixelSize.x), projected.progressRange)
        || !RayStep_ClipInterval(projected.pixelStart.y, projected.pixelDelta.y,
            float2(0.0, pixelSize.y), projected.progressRange))
        return false;
    return all(isfinite(projected.pixelStart)) && all(isfinite(projected.pixelDelta))
        && projected.progressRange.x < projected.progressRange.y;
}

RayStepDDAState RayStep_DDAAt(RayStepProjectedRay ray, float progress)
{
    RayStepDDAState state;
    state.pixel = ray.pixelStart + ray.pixelDelta * progress;
    state.weightedEyeDepth = -ray.weightedViewStart.z - ray.weightedViewDelta.z * progress;
    state.weightedDistance = ray.weightedDistanceStart + ray.weightedDistanceDelta * progress;
    state.reciprocalW = ray.reciprocalWStart + ray.reciprocalWDelta * progress;
    state.progress = progress;
    return state;
}

void RayStep_CreateDDA(RayStepProjectedRay ray, int sampleCount,
    out RayStepDDAState state, out RayStepDDAState increment)
{
    state = RayStep_DDAAt(ray, ray.progressRange.x);
    float progressStep = (ray.progressRange.y - ray.progressRange.x) / max(sampleCount, 1);
    increment.pixel = ray.pixelDelta * progressStep;
    increment.weightedEyeDepth = -ray.weightedViewDelta.z * progressStep;
    increment.weightedDistance = ray.weightedDistanceDelta * progressStep;
    increment.reciprocalW = ray.reciprocalWDelta * progressStep;
    increment.progress = progressStep;
}

void RayStep_AdvanceDDA(inout RayStepDDAState state, RayStepDDAState increment, float stride)
{
    state.pixel += increment.pixel * stride;
    state.weightedEyeDepth += increment.weightedEyeDepth * stride;
    state.weightedDistance += increment.weightedDistance * stride;
    state.reciprocalW += increment.reciprocalW * stride;
    state.progress += increment.progress * stride;
}

RayStepDDAState RayStep_MidpointDDA(RayStepDDAState low, RayStepDDAState high)
{
    RayStepDDAState middle;
    middle.pixel = (low.pixel + high.pixel) * 0.5;
    middle.weightedEyeDepth = (low.weightedEyeDepth + high.weightedEyeDepth) * 0.5;
    middle.weightedDistance = (low.weightedDistance + high.weightedDistance) * 0.5;
    middle.reciprocalW = (low.reciprocalW + high.reciprocalW) * 0.5;
    middle.progress = (low.progress + high.progress) * 0.5;
    return middle;
}

#endif
