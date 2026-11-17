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
//  Hit-surface closure — 命中面朝向判定的前提检验。
//
//  「命中面朝背向射线 ⇒ 假命中」只在 G-buffer 法线真的代表几何朝向时成立，
//  也就是只在**不透明闭合物**上成立。薄壳（草叶、植被、双面卡片）存的是
//  着色法线而非几何法线：草把地形法线写进 _CameraNormalsTexture，
//  竖直的叶片报告自己是地面。拿它做背面剔除，会把整片草地判成假命中。
//
//  这条前提无法从深度缓冲反推：草冠的深度场本身就是一张准地形高度场，
//  由它重建的几何法线同样接近 ±up，与地面不可分（实测草 70.0% 判闭合、
//  地表 69.9% 判闭合，等于抛硬币）。所以由**生产者自己声明**：
//  _CameraNormalsTexture.a 写成 -1 表示「薄壳/双面」；URP 现状下其余
//  不透明几何的该通道为 +1，天空为 0，两者都按闭合面处理。
//  生产者见 GrassWind.shader 的 DepthNormals Pass。
// ════════════════════════════════════════════════════════════════
//  ═══════════════════════════════════════════════════════════════════════
//  依赖：include 本文件要求调用方已声明 _CameraDepthTexture **和**
//  _CameraNormalsTexture（DeclareDepthTexture.hlsl + DeclareNormalsTexture.hlsl）。
//  缺任一声明只会在 Metal 上以 undeclared identifier 报错，而且整个 shader
//  会退化成 error shader —— 表现为「改了 diffuse 却毫无反应」。
//  ═══════════════════════════════════════════════════════════════════════
#define SSGI_THIN_SURFACE_FLAG (-1.0)

bool SST_IsThinSurface(float2 uv)
{
    uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraNormalsTexture_TexelSize.xy);
    return SAMPLE_TEXTURE2D_X_LOD(_CameraNormalsTexture, sampler_PointClamp, uv, 0).a < -0.5;
}

// 命中面能不能按「不透明闭合物」做背面剔除。
bool SST_HitSurfaceIsClosed(float2 uv)
{
    return !SST_IsThinSurface(uv);
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
    #if defined(SSGI_TRACE_HIZ)
        context.pyramidMipCount = _SSGIPyramidMipCount;
    #endif
    #if UNITY_REVERSED_Z
        context.reversedZ = 1u;
    #endif
    return context;
}

ScreenTraceConfig SST_CreateTraceConfig(float thickness, int stepCount)
{
    ScreenTraceConfig config;
    config.maxSteps = stepCount;
    config.refinementSteps = 6;
    config.thickness = thickness;
    return config;
}

bool SST_TracePrepared(float3 originWS, float3 directionWS, float maxDistance,
    ScreenTraceContext context, ScreenTraceConfig config, out ScreenSpaceTraceHit hit)
{
    hit = (ScreenSpaceTraceHit)0;
    hit.status = SCREEN_TRACE_INVALID;
    RayStepRay ray;
    if (!RayStep_CreateRay(originWS, directionWS, float2(0.0, maxDistance), ray))
        return false;
    ScreenTraceHit geometry;
    bool found;
    #if defined(SSGI_TRACE_RAY)
        found = ScreenTrace_TraceRay(ray, context, config, geometry);
    #elif defined(SSGI_TRACE_HIZ)
        if (all(context.depthSize == _SSGIPyramidSize.xy)
            && all(context.depthTextureSize == _SSGIPyramidSize.xy)
            && all(context.depthUvScaleBias == float4(1.0, 1.0, 0.0, 0.0)))
            found = ScreenTrace_TraceHiZ(ray, context, config, geometry);
        else
            found = ScreenTrace_TraceDDA(ray, context, config, geometry);
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

bool SST_Trace(float3 originWS, float3 directionWS, float maxDistance,
    float thickness, int stepCount, out ScreenSpaceTraceHit hit)
{
    return SST_TracePrepared(originWS, directionWS, maxDistance,
        SST_CreateTraceContext(), SST_CreateTraceConfig(thickness, stepCount), hit);
}
#endif

#endif
