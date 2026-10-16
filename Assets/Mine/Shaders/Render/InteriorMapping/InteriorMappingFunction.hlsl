// ════════════════════════════════════════════════════════════
//  InteriorMappingFunction — shared Box / Hemisphere functions
//  Both projections use the same direction convention as the baker.
// ════════════════════════════════════════════════════════════

TEXTURE2D(_InteriorMap);
SAMPLER(sampler_InteriorMap);

CBUFFER_START(UnityPerMaterial)
    float4 _RoomSize;
    float4 _WindowSize;
    float4 _RoomTint;
    float4 _FallbackColor;
    float4 _InteriorMap_ST;
    float _RoomBrightness;
    float _ProjectionType;
    float _MapProjection;
    float _MapCoverage;
CBUFFER_END

static const float INTERIOR_INV_PI = 0.31830988618;
static const float INTERIOR_HIT_EPSILON = 0.0001;
static const float INTERIOR_MAX_DISTANCE = 1e20;

// ════════════════════════════════════════════════════════════
//  RayIntersection — ray helpers and nearest-hit selection
// ════════════════════════════════════════════════════════════
float3 InteriorSafeNormalize(float3 value)
{
    return value * rsqrt(max(dot(value, value), 1e-8));
}

float2 InteriorIntersectAxis(
    float3 rayOrigin,
    float3 rayDirection,
    float3 axis,
    float positiveDistance,
    float negativeDistance,
    float positiveId,
    float negativeId)
{
    float directionWeight = dot(rayDirection, axis);
    float originWeight = dot(rayOrigin, axis);
    if (directionWeight > INTERIOR_HIT_EPSILON)
    {
        return float2((positiveDistance - originWeight) / directionWeight, positiveId);
    }
    if (directionWeight < -INTERIOR_HIT_EPSILON)
    {
        return float2((negativeDistance - originWeight) / directionWeight, negativeId);
    }
    return float2(INTERIOR_MAX_DISTANCE, 0.0);
}

float2 InteriorNearest(float2 first, float2 second, float2 third)
{
    float2 nearest = first.x < second.x ? first : second;
    return nearest.x < third.x ? nearest : third;
}

// ════════════════════════════════════════════════════════════
//  BoxIntersection — intersect a single six-sided room
// ════════════════════════════════════════════════════════════
void InteriorIntersectBox(
    float3 rayOrigin,
    float3 rayDirection,
    float2 roomSize,
    float roomDepth,
    out float3 hitPosition,
    out float wallId,
    out float hitMask)
{
    float halfWidth = max(roomSize.x * 0.5, 0.001);
    float halfHeight = max(roomSize.y * 0.5, 0.001);
    float depth = max(roomDepth, 0.001);
    float2 topBottom = InteriorIntersectAxis(
        rayOrigin,
        rayDirection,
        float3(0.0, 1.0, 0.0),
        halfHeight,
        -halfHeight,
        1.0,
        2.0);
    float2 rightLeft = InteriorIntersectAxis(
        rayOrigin,
        rayDirection,
        float3(1.0, 0.0, 0.0),
        halfWidth,
        -halfWidth,
        3.0,
        4.0);
    float2 frontBack = InteriorIntersectAxis(
        rayOrigin,
        rayDirection,
        float3(0.0, 0.0, 1.0),
        0.0,
        -depth,
        6.0,
        5.0);
    float2 nearest = InteriorNearest(topBottom, rightLeft, frontBack);
    float distance = max(nearest.x, 0.0);

    hitPosition = rayOrigin + rayDirection * distance;
    wallId = nearest.y;
    hitMask = step(INTERIOR_HIT_EPSILON, nearest.x)
        * step(nearest.x, INTERIOR_MAX_DISTANCE * 0.5);
}

// ════════════════════════════════════════════════════════════
//  HemisphereIntersection — intersect only the rear half of a sphere
// ════════════════════════════════════════════════════════════
void InteriorIntersectHemisphere(
    float3 rayOrigin,
    float3 rayDirection,
    float2 roomSize,
    float roomDepth,
    out float3 hitPosition,
    out float wallId,
    out float hitMask)
{
    float radius = max(max(roomSize.x, roomSize.y) * 0.5, max(roomDepth * 0.5, 0.001));
    float b = dot(rayOrigin, rayDirection);
    float c = dot(rayOrigin, rayOrigin) - radius * radius;
    float discriminant = b * b - c;
    float hasIntersection = step(0.0, discriminant);
    float root = sqrt(max(discriminant, 0.0));
    float nearDistance = -b - root;
    float farDistance = -b + root;
    float3 nearCandidate = rayOrigin + rayDirection * nearDistance;
    float3 farCandidate = rayOrigin + rayDirection * farDistance;
    float distance = INTERIOR_MAX_DISTANCE;
    float3 candidate = rayOrigin;

    if (nearDistance > INTERIOR_HIT_EPSILON && nearCandidate.z <= 0.0)
    {
        distance = nearDistance;
        candidate = nearCandidate;
    }
    if (farDistance > INTERIOR_HIT_EPSILON
        && farCandidate.z <= 0.0
        && farDistance < distance)
    {
        distance = farDistance;
        candidate = farCandidate;
    }

    hitPosition = candidate;
    wallId = 0.0;
    hitMask = hasIntersection
        * step(INTERIOR_HIT_EPSILON, distance)
        * step(distance, INTERIOR_MAX_DISTANCE * 0.5);
}

// ════════════════════════════════════════════════════════════
//  MapUV — projection-aware direction mapping
// ════════════════════════════════════════════════════════════
float2 InteriorMapUV(float3 hitPosition, int projectionType)
{
    float3 direction = InteriorSafeNormalize(hitPosition);
    if (projectionType == 1)
    {
        return saturate(direction.xy * 0.5 + 0.5);
    }

    float longitude = atan2(direction.x, -direction.z) * (INTERIOR_INV_PI * 0.5) + 0.5;
    float latitude = direction.y * 0.5 + 0.5;
    return saturate(float2(longitude, latitude));
}

// ════════════════════════════════════════════════════════════
//  RoomRender — baked texture validity and Room Tint layer
// ════════════════════════════════════════════════════════════
float3 InteriorSampleRoom(float2 mapUV, out float textureMask)
{
    float2 sampleUV = saturate(mapUV * _InteriorMap_ST.xy + _InteriorMap_ST.zw);
    float4 textureSample = SAMPLE_TEXTURE2D(_InteriorMap, sampler_InteriorMap, sampleUV);
    float alphaMask = step(0.0001, textureSample.a);
    textureMask = alphaMask;
    return textureSample.rgb;
}

float3 InteriorApplyRoomTint(float3 interiorColor)
{
    float tintWeight = saturate(_RoomTint.a);
    float3 tintColor = max(_RoomTint.rgb, 0.0);
    return lerp(interiorColor, interiorColor * tintColor, tintWeight);
}

// ════════════════════════════════════════════════════════════
//  Render — cast a Box / Hemisphere ray from the window
// ════════════════════════════════════════════════════════════
float3 InteriorMappingRenderWithWindowSize(
    float2 meshUV,
    float3 cameraPositionOS,
    int projectionType,
    float2 windowSize,
    out float hitMask)
{
    float2 roomSize = max(_RoomSize.xy, 0.01);
    float roomDepth = max(_RoomSize.z, 0.01);
    windowSize = max(windowSize, 0.01);
    float3 windowPosition = float3((meshUV - 0.5) * windowSize, 0.0);
    float3 rayDirection = InteriorSafeNormalize(windowPosition - cameraPositionOS);
    float3 rayOrigin = windowPosition + rayDirection * INTERIOR_HIT_EPSILON;
    float3 hitPosition;
    float wallId;
    hitMask = 0.0;

    if (projectionType == 0)
    {
        InteriorIntersectBox(rayOrigin, rayDirection, roomSize, roomDepth, hitPosition, wallId, hitMask);
    }
    else
    {
        InteriorIntersectHemisphere(rayOrigin, rayDirection, roomSize, roomDepth, hitPosition, wallId, hitMask);
    }

    float textureMask;
    float3 sampledColor;
    if (_MapProjection > 3.5 || _MapProjection < -0.5)
    {
        float2 mapUV = InteriorMapUV(hitPosition, projectionType);
        sampledColor = InteriorSampleRoom(mapUV, textureMask);
    }
    else
    {
        float3 mapDirection = float3(hitPosition.xy, -hitPosition.z);
        float4 mapSample = HDRMapSample(TEXTURE2D_ARGS(_InteriorMap, sampler_InteriorMap),
            mapDirection, (int)round(_MapProjection), (int)round(_MapCoverage));
        sampledColor = mapSample.rgb;
        textureMask = step(0.0001, mapSample.a);
    }
    float3 interiorLayer = InteriorApplyRoomTint(sampledColor);
    interiorLayer = lerp(_FallbackColor.rgb, interiorLayer, textureMask);
    return lerp(_FallbackColor.rgb, interiorLayer, hitMask) * _RoomBrightness;
}

float3 InteriorMappingRender(float2 meshUV, float3 cameraPositionOS, int projectionType, out float hitMask)
{
    return InteriorMappingRenderWithWindowSize(
        meshUV,
        cameraPositionOS,
        projectionType,
        _WindowSize.xy,
        hitMask);
}

// ════════════════════════════════════════════════════════════
//  SurfaceRender — normalize UVs for an arbitrarily rotated plane
// ════════════════════════════════════════════════════════════
float3 InteriorMappingRenderSurface(
    float2 meshUV,
    float3 surfacePositionWS,
    float3 cameraPositionWS,
    float3 surfaceNormalWS,
    float3 positionDerivativeX,
    float3 positionDerivativeY,
    float2 uvDerivativeX,
    float2 uvDerivativeY,
    int projectionType,
    out float hitMask)
{
    float determinant = uvDerivativeX.x * uvDerivativeY.y
        - uvDerivativeX.y * uvDerivativeY.x;
    float safeDeterminant = abs(determinant) > 1e-6
        ? determinant
        : (determinant < 0.0 ? -1e-6 : 1e-6);
    float3 positionDerivativeU =
        (positionDerivativeX * uvDerivativeY.y
        - positionDerivativeY * uvDerivativeX.y) / safeDeterminant;
    float3 positionDerivativeV =
        (-positionDerivativeX * uvDerivativeY.x
        + positionDerivativeY * uvDerivativeX.x) / safeDeterminant;
    float windowWidth = max(length(positionDerivativeU), 0.01);
    float windowHeight = max(length(positionDerivativeV), 0.01);
    float3 tangentWS = positionDerivativeU / windowWidth;
    float3 bitangentWS = positionDerivativeV / windowHeight;
    float3 normalWS = InteriorSafeNormalize(surfaceNormalWS);

    float3 windowCenterWS = surfacePositionWS
        - tangentWS * ((meshUV.x - 0.5) * windowWidth)
        - bitangentWS * ((meshUV.y - 0.5) * windowHeight);
    float3 cameraOffsetWS = cameraPositionWS - windowCenterWS;
    if (dot(cameraOffsetWS, normalWS) < 0.0)
    {
        normalWS = -normalWS;
    }
    float depthScale = max(sqrt(windowWidth * windowHeight), 0.01);
    float3 cameraPositionWindow = float3(
        dot(cameraOffsetWS, tangentWS) / windowWidth,
        dot(cameraOffsetWS, bitangentWS) / windowHeight,
        dot(cameraOffsetWS, normalWS) / depthScale);

    return InteriorMappingRenderWithWindowSize(
        meshUV,
        cameraPositionWindow,
        projectionType,
        float2(1.0, 1.0),
        hitMask);
}
