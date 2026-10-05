// ════════════════════════════════════════════════════════════════
//  AO — SSAO hemisphere kernel and HBAO horizon integration over depth/normals.
// ════════════════════════════════════════════════════════════════
#ifndef AO_FUNCTION_INCLUDED
#define AO_FUNCTION_INCLUDED

// ════════════════════════════════════════════════════════════════
//  Normal sample — explicit mip zero with URP stereo UVs and oct decoding.
// ════════════════════════════════════════════════════════════════
float3 AO_SampleNormal(float2 uv)
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
//  View position — raw depth reconstructed to world, then moved to eye space.
// ════════════════════════════════════════════════════════════════
float3 AO_ViewPosition(float2 uv, float rawDepth)
{
    return TransformWorldToView(SST_WorldPosition(uv, rawDepth));
}

// ════════════════════════════════════════════════════════════════
//  Guide fetch — single rejection test for surface validity and pose.
// ════════════════════════════════════════════════════════════════
bool AO_FetchGuide(float2 uv, out float3 positionWS, out float3 normalWS, out float centerEyeDepth)
{
    float rawDepth = SST_SampleDepth(uv);
    normalWS = AO_SampleNormal(uv);
    positionWS = 0.0;
    centerEyeDepth = 0.0;
    if (!SST_IsSurface(rawDepth) || dot(normalWS, normalWS) < 0.25)
        return false;
    normalWS = normalize(normalWS);
    positionWS = SST_WorldPosition(uv, rawDepth);
    centerEyeDepth = SST_EyeDepth(rawDepth);
    return true;
}

// ════════════════════════════════════════════════════════════════
//  Noise — per-pixel rotation with frame sequence when temporal history is active.
// ════════════════════════════════════════════════════════════════
float2 AO_Noise(float2 pixel)
{
    float3 value = frac(float3(pixel.xyx) * float3(0.1031, 0.1030, 0.0973));
    value += dot(value, value.yzx + 33.33);
    return frac((value.xx + value.yz) * value.zy
        + float2(_AOFrameIndex * 0.75487766, _AOFrameIndex * 0.5698403));
}

// ════════════════════════════════════════════════════════════════
//  Kernel direction — cosine-weighted world hemisphere around the normal.
// ════════════════════════════════════════════════════════════════
float3 AO_KernelDirection(float2 xi, float3 normalWS)
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
//  Distance falloff — inverse-square-weighted occluder contribution.
//  falloff = 0 disables it (factor is exactly 1.0, no change).
// ════════════════════════════════════════════════════════════════
float AO_DistanceFalloff(float occluderDistance, float falloffK)
{
    return rcp(1.0 + occluderDistance * occluderDistance * falloffK);
}

// ════════════════════════════════════════════════════════════════
//  SSAO — kernel points reprojected onto the depth buffer; near geometry occludes.
// ════════════════════════════════════════════════════════════════
float AO_SsaoOcclusion(float2 uv, float3 positionWS, float3 normalWS, float centerEyeDepth)
{
    float2 rotation = AO_Noise(floor(uv * _AOSourceSize.zw));
    int sampleCount = clamp(_AOSampleCount, 1, 32);
    float falloffK = max(_AOParams.z, 0.0);
    float occlusion = 0.0;
    [loop]
    for (int sampleIndex = 0; sampleIndex < sampleCount; ++sampleIndex)
    {
        float2 xi = frac(float2((sampleIndex + 0.5) / sampleCount, sampleIndex * 0.61803398875) + rotation);
        float3 sampleWS = positionWS + AO_KernelDirection(xi, normalWS) * _AOParams.x;
        float2 sampleUV;
        float sampleEyeDepth;
        if (!SST_Project(sampleWS, sampleUV, sampleEyeDepth))
            continue;
        float rawDepth = SST_SampleDepth(sampleUV);
        if (!SST_IsSurface(rawDepth))
            continue;
        float occluderEyeDepth = SST_EyeDepth(rawDepth);
        if (sampleEyeDepth - occluderEyeDepth > _AOParams.y && centerEyeDepth - occluderEyeDepth < _AOParams.x)
        {
            float occluderDistance = distance(positionWS, SST_WorldPosition(sampleUV, rawDepth));
            occlusion += AO_DistanceFalloff(occluderDistance, falloffK);
        }
    }
    return 1.0 - occlusion / sampleCount;
}

// ════════════════════════════════════════════════════════════════
//  HBAO — tangent-plane horizon integration along rotated screen directions.
// ════════════════════════════════════════════════════════════════
float AO_HbaoOcclusion(float2 uv, float3 positionWS, float3 normalWS, float centerEyeDepth)
{
    float3 positionVS = TransformWorldToView(positionWS);
    float3 normalVS = TransformWorldToViewDir(normalWS);
    int directionCount = clamp(_AOSampleCount, 2, 32);
    int stepCount = clamp(_AOStepCount, 1, 32);
    float rotation = AO_Noise(floor(uv * _AOSourceSize.zw)).x * TWO_PI;
    float pixelsPerUnit = unity_OrthoParams.w > 0.5
        ? 0.5 * _AOSourceSize.w * unity_CameraProjection[1][1]
        : 0.5 * _AOSourceSize.w * unity_CameraProjection[1][1] / max(centerEyeDepth, 0.0001);
    float stepPixels = clamp(_AOParams.x * pixelsPerUnit / stepCount, 1.0, 64.0);
    float sineBias = _AOFilterParams.z;
    float falloffK = max(_AOParams.z, 0.0);
    float occlusion = 0.0;
    [loop]
    for (int directionIndex = 0; directionIndex < directionCount; ++directionIndex)
    {
        float angle = TWO_PI * directionIndex / directionCount + rotation;
        float2 directionUV = float2(cos(angle), sin(angle));
        float3 axisVS = float3(directionUV, 0.0);
        float3 upVS = normalVS - axisVS * dot(normalVS, axisVS);
        if (dot(upVS, upVS) < 0.0001)
            continue;
        upVS = normalize(upVS);
        float horizon = sineBias;
        float occluded = 0.0;
        [loop]
        for (int stepIndex = 1; stepIndex <= stepCount; ++stepIndex)
        {
            float2 sampleUV = uv + directionUV * stepPixels * _AOSourceSize.xy * stepIndex;
            if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
                break;
            float rawDepth = SST_SampleDepth(sampleUV);
            if (!SST_IsSurface(rawDepth))
                break;
            float3 delta = AO_ViewPosition(sampleUV, rawDepth) - positionVS;
            float lengthVS = length(delta);
            if (lengthVS < 0.0001)
                continue;
            float tangentDistance = length(delta - upVS * dot(delta, upVS));
            horizon = max(horizon, dot(delta, upVS) / lengthVS);
            occluded = max(occluded, saturate(horizon - sineBias)
                * saturate(1.0 - tangentDistance / _AOParams.x)
                * AO_DistanceFalloff(lengthVS, falloffK));
        }
        occlusion += occluded;
    }
    return saturate(1.0 - occlusion / directionCount);
}

// ════════════════════════════════════════════════════════════════
//  Guide weight — eye-depth and world-normal rejection for the blur taps.
// ════════════════════════════════════════════════════════════════
float AO_GuideWeight(float2 sampleUV, float centerEyeDepth, float3 centerNormalWS)
{
    float rawDepth = SST_SampleDepth(sampleUV);
    float3 sampleNormal = AO_SampleNormal(sampleUV);
    if (!SST_IsSurface(rawDepth) || dot(sampleNormal, sampleNormal) < 0.25)
        return 0.0;
    float depthWeight = exp2(-abs(SST_EyeDepth(rawDepth) - centerEyeDepth) / max(_AOFilterParams.x, 0.0001));
    float normalWeight = pow(saturate(dot(centerNormalWS, normalize(sampleNormal))), max(_AOFilterParams.y, 1.0));
    return depthWeight * normalWeight;
}

// ════════════════════════════════════════════════════════════════
//  Bilateral blur — five point taps; sky and rejected guides stay unoccluded.
// ════════════════════════════════════════════════════════════════
float AO_Blur(float2 uv, float2 axis)
{
    float3 positionWS;
    float3 normalWS;
    float centerEyeDepth;
    if (!AO_FetchGuide(uv, positionWS, normalWS, centerEyeDepth))
        return 1.0;
    float sum = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int tap = -2; tap <= 2; ++tap)
    {
        float2 sampleUV = uv + axis * _AOSourceSize.xy * tap;
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            continue;
        float weight = exp2(-0.5 * tap * tap) * AO_GuideWeight(sampleUV, centerEyeDepth, normalWS);
        sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, sampleUV, 0).r * weight;
        totalWeight += weight;
    }
    return totalWeight > 0.0001 ? sum / totalWeight : 1.0;
}

// ════════════════════════════════════════════════════════════════
//  Resolve — depth/normal-guided four-tap upsample, unoccluded on rejection.
// ════════════════════════════════════════════════════════════════
float AO_Resolve(float2 uv)
{
    float rawDepth = SST_SampleDepth(uv);
    float3 normalWS = AO_SampleNormal(uv);
    if (!SST_IsSurface(rawDepth) || dot(normalWS, normalWS) < 0.25)
        return 1.0;
    normalWS = normalize(normalWS);
    float centerEyeDepth = SST_EyeDepth(rawDepth);
    float2 pixel = uv * _AOSourceSize.zw - 0.5;
    float2 basePixel = floor(pixel);
    float2 fraction = frac(pixel);
    float sum = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int tap = 0; tap < 4; ++tap)
    {
        float2 offset = float2(tap & 1, tap >> 1);
        float2 sampleUV = (basePixel + offset + 0.5) * _AOSourceSize.xy;
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            continue;
        float2 bilinear = lerp(1.0 - fraction, fraction, offset);
        float weight = bilinear.x * bilinear.y * AO_GuideWeight(sampleUV, centerEyeDepth, normalWS);
        sum += SAMPLE_TEXTURE2D_X_LOD(_AOTexture, sampler_PointClamp, sampleUV, 0).r * weight;
        totalWeight += weight;
    }
    return totalWeight > 0.0001 ? sum / totalWeight : 1.0;
}

// ════════════════════════════════════════════════════════════════
//  Trace — mode dispatch; sky and invalid guides report full visibility.
// ════════════════════════════════════════════════════════════════
float4 AO_Trace(float2 uv)
{
    float3 positionWS;
    float3 normalWS;
    float centerEyeDepth;
    if (!AO_FetchGuide(uv, positionWS, normalWS, centerEyeDepth))
        return 1.0;
    float visibility = _AOMode == 1
        ? AO_HbaoOcclusion(uv, positionWS, normalWS, centerEyeDepth)
        : AO_SsaoOcclusion(uv, positionWS, normalWS, centerEyeDepth);
    return float4(saturate(visibility).xxx, 1.0);
}

#endif
