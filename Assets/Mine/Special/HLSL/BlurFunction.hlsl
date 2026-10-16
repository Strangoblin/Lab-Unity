// ════════════════════════════════════════════════════════════════
//  Blur Function — legacy Gaussian / bilateral and XR Gaussian kernels.
//  调用方先 include Core.hlsl 与 HashFunction.hlsl；尺寸、方向与种子由参数提供。
// ════════════════════════════════════════════════════════════════
#ifndef BLURFUNCTION_HLSL_INCLUDED
#define BLURFUNCTION_HLSL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

float4 BlurHorizontal(float2 uv, float2 texelSize, float BlurScale, Texture2D MainTex, SamplerState sampler_LinearClamp)
{
    float4 color = float4(0,0,0,0);

    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(-2.0, 0.0)) * 0.1216216;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(-1.0, 0.0)) * 0.2332432;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv) * 0.290918;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(1.0, 0.0)) * 0.2332432;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(2.0, 0.0)) * 0.1216216;
    return color;
}

float4 BlurVertical(float2 uv, float2 texelSize, float BlurScale, Texture2D MainTex, SamplerState sampler_LinearClamp)
{
    float4 color = float4(0,0,0,0);

    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(0.0, -2.0)) * 0.1216216;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(0.0, -1.0)) * 0.2332432;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv) * 0.290918;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(0.0, 1.0)) * 0.2332432;
    color += SAMPLE_TEXTURE2D(MainTex, sampler_LinearClamp, uv + BlurScale * texelSize * float2(0.0, 2.0)) * 0.1216216;
    return color;
}


// ════════════════════════════════════════════════════════════════
//  双边保边模糊（深度 + 可选法线引导）
//
//  关键字（在调用 Shader 中声明）：
//    BLUR_BILATERAL_LOW           强度低 — 边缘柔和，允许少量渗漏
//    BLUR_BILATERAL_MEDIUM        强度中 — 默认平衡（未定义任何强度时生效）
//    BLUR_BILATERAL_HIGH          强度高 — 边缘锐利，严格保边
//    BLUR_BILATERAL_NORMAL        启用法线保边（未定义时仅深度保边）
// ════════════════════════════════════════════════════════════════

// ── 强度预设 ──
#if defined(BLUR_BILATERAL_LOW)
    static const float _BilateralDepthSens = 2.0;
    static const float _BilateralNormalPow = 2.0;
#elif defined(BLUR_BILATERAL_HIGH)
    static const float _BilateralDepthSens = 50.0;
    static const float _BilateralNormalPow = 32.0;
#else
    static const float _BilateralDepthSens = 10.0;
    static const float _BilateralNormalPow = 8.0;
#endif

// 5-tap Gaussian
static const float _BilateralGauss[5] = { 0.1216, 0.2332, 0.2910, 0.2332, 0.1216 };
static const float _BilateralOffset[5] = { -2, -1, 0, 1, 2 };

float4 BilateralBlurHorizontal(
    float2 uv, float2 texelSize, float blurScale,
    Texture2D MainTex, SamplerState samplerState)
{
    float  centerRawDepth = SampleSceneDepth(uv);
    float  centerEyeDepth = LinearEyeDepth(centerRawDepth, _ZBufferParams);
    #ifdef BLUR_BILATERAL_NORMAL
    float3 centerNormal    = SampleSceneNormals(uv);
    #endif

    float4 sum    = 0;
    float  totalW = 0;

    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float2 suv = uv + blurScale * texelSize * float2(_BilateralOffset[i], 0);
        float4 s   = SAMPLE_TEXTURE2D(MainTex, samplerState, suv);

        float sdRaw = SampleSceneDepth(suv);
        float sdEye = LinearEyeDepth(sdRaw, _ZBufferParams);
        float depthW = exp(-abs(centerEyeDepth - sdEye) * _BilateralDepthSens);
        float normalW = 1.0;

        #ifdef BLUR_BILATERAL_NORMAL
        float3 sn = SampleSceneNormals(suv);
        normalW   = pow(saturate(dot(centerNormal, sn)), _BilateralNormalPow);
        #endif

        float w = _BilateralGauss[i] * depthW * normalW;
        sum    += s * w;
        totalW += w;
    }

    return sum / max(totalW, 0.0001);
}

float4 BilateralBlurVertical(
    float2 uv, float2 texelSize, float blurScale,
    Texture2D MainTex, SamplerState samplerState)
{
    float  centerRawDepth = SampleSceneDepth(uv);
    float  centerEyeDepth = LinearEyeDepth(centerRawDepth, _ZBufferParams);
    #ifdef BLUR_BILATERAL_NORMAL
    float3 centerNormal    = SampleSceneNormals(uv);
    #endif

    float4 sum    = 0;
    float  totalW = 0;

    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float2 suv = uv + blurScale * texelSize * float2(0, _BilateralOffset[i]);
        float4 s   = SAMPLE_TEXTURE2D(MainTex, samplerState, suv);

        float sdRaw = SampleSceneDepth(suv);
        float sdEye = LinearEyeDepth(sdRaw, _ZBufferParams);
        float depthW = exp(-abs(centerEyeDepth - sdEye) * _BilateralDepthSens);
        float normalW = 1.0;
        
        #ifdef BLUR_BILATERAL_NORMAL
        float3 sn = SampleSceneNormals(suv);
        normalW   = pow(saturate(dot(centerNormal, sn)), _BilateralNormalPow);
        #endif

        float w = _BilateralGauss[i] * depthW * normalW;
        sum    += s * w;
        totalW += w;
    }

    return sum / max(totalW, 0.0001);
}

// ════════════════════════════════════════════════════════════════
//  UV 步长 — 像素或屏幕高度比例，显式传入输出尺寸。
// ════════════════════════════════════════════════════════════════
float2 Blur_GetUVStep(float radius, float2 outputSize, int radiusMode)
{
    float2 safeSize = max(outputSize, float2(1.0, 1.0));
    float safeRadius = max(radius, 0.0);
    if (radiusMode == 1)
        return safeRadius * float2(safeSize.y / safeSize.x, 1.0);
    return safeRadius / safeSize;
}

// ════════════════════════════════════════════════════════════════
//  固定空间整数 Hash — 混合二维像素、整数种子与采样层编号。
// ════════════════════════════════════════════════════════════════
#if SHADER_TARGET >= 35
float Blur_Hash01(uint2 pixelCoord, uint seed, uint tapIndex)
{
    return Hash_UInt2SeedStream01(pixelCoord, seed, tapIndex);
}
#endif

// ════════════════════════════════════════════════════════════════
//  XR 固定五采样 Gaussian — 旧核归一化，颜色与遮罩共用权重。
// ════════════════════════════════════════════════════════════════
float4 Blur_Gaussian5(
    float2 uv, float2 uvStep, float2 direction,
    TEXTURE2D_X_PARAM(mainTex, samplerState))
{
    float2 offset = uvStep * direction;
    float4 color = SAMPLE_TEXTURE2D_X(mainTex, samplerState, uv - offset * 2.0) * 0.1216216;
    color += SAMPLE_TEXTURE2D_X(mainTex, samplerState, uv - offset) * 0.2332432;
    color += SAMPLE_TEXTURE2D_X(mainTex, samplerState, uv) * 0.290918;
    color += SAMPLE_TEXTURE2D_X(mainTex, samplerState, uv + offset) * 0.2332432;
    color += SAMPLE_TEXTURE2D_X(mainTex, samplerState, uv + offset * 2.0) * 0.1216216;
    return color / 1.0006476;
}

// ════════════════════════════════════════════════════════════════
//  XR 分层抖动 Gaussian — 共用采样循环，点数由调用方传编译期常量。
// ════════════════════════════════════════════════════════════════
float4 Blur_GaussianJittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed, int sampleCount,
    TEXTURE2D_X_PARAM(mainTex, samplerState))
{
#if SHADER_TARGET >= 35
    float strength = saturate(jitterStrength);
    float4 color = float4(0.0, 0.0, 0.0, 0.0);
    UNITY_BRANCH
    if (strength <= 0.0)
    {
        color = Blur_Gaussian5(uv, uvStep, direction, TEXTURE2D_X_ARGS(mainTex, samplerState));
    }
    else
    {
        const float weights[27] =
        {
            0.1216216, 0.2332432, 0.290918, 0.2332432, 0.1216216,
            0.0544094825, 0.0879394219, 0.1239134970,
            0.1522227692, 0.1630296588, 0.1522227692,
            0.1239134970, 0.0879394219, 0.0544094825,
            0.0346004322, 0.0496734398, 0.0667749739,
            0.0840523767, 0.0990679210, 0.1093359240,
            0.1129898648,
            0.1093359240, 0.0990679210, 0.0840523767,
            0.0667749739, 0.0496734398, 0.0346004322
        };
        int tapCount = sampleCount == 9 ? 9 : (sampleCount == 13 ? 13 : 5);
        int weightOffset = tapCount == 5 ? 0 : (tapCount == 9 ? 5 : 14);
        float stratumWidth = 5.0 / (float)tapCount;
        float sampleScale;
        if (tapCount == 5)
        {
            float secondMoment = (2.0 * 0.2332432 + 8.0 * 0.1216216) / 1.0006476;
            sampleScale = rsqrt(1.0 + strength * strength / (12.0 * secondMoment));
        }
        else
        {
            float secondMoment = tapCount == 9 ? 1.4258538730 : 1.4254231168;
            float jitterMoment = strength * strength * stratumWidth * stratumWidth / 12.0;
            sampleScale = rsqrt((secondMoment + jitterMoment) / 1.43852761);
        }

        uint2 pixel = (uint2)max(floor(pixelCoord), float2(0.0, 0.0));
        uint passSeed = (uint)max(floor(seed), 0.0);
        float2 offset = uvStep * direction * sampleScale;
        [unroll]
        for (int i = 0; i < 13; i++)
        {
            if (i < tapCount)
            {
                float midpoint = ((float)i + 0.5) * stratumWidth - 2.5;
                float distance = midpoint
                    + (Blur_Hash01(pixel, passSeed, (uint)i) - 0.5) * strength * stratumWidth;
                color += SAMPLE_TEXTURE2D_X(mainTex, samplerState, uv + offset * distance)
                    * weights[weightOffset + i];
            }
        }
        if (tapCount == 5)
            color /= 1.0006476;
    }
    return color;
#else
    return Blur_Gaussian5(uv, uvStep, direction, TEXTURE2D_X_ARGS(mainTex, samplerState));
#endif
}

// ════════════════════════════════════════════════════════════════
//  固定点数入口 — 保留原接口，以字面常量复用统一抖动核。
// ════════════════════════════════════════════════════════════════
float4 Blur_Gaussian5Jittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed, TEXTURE2D_X_PARAM(mainTex, samplerState))
{
    return Blur_GaussianJittered(uv, uvStep, direction, pixelCoord,
        jitterStrength, seed, 5, TEXTURE2D_X_ARGS(mainTex, samplerState));
}

float4 Blur_Gaussian9Jittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed, TEXTURE2D_X_PARAM(mainTex, samplerState))
{
    return Blur_GaussianJittered(uv, uvStep, direction, pixelCoord,
        jitterStrength, seed, 9, TEXTURE2D_X_ARGS(mainTex, samplerState));
}

float4 Blur_Gaussian13Jittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed, TEXTURE2D_X_PARAM(mainTex, samplerState))
{
    return Blur_GaussianJittered(uv, uvStep, direction, pixelCoord,
        jitterStrength, seed, 13, TEXTURE2D_X_ARGS(mainTex, samplerState));
}

#endif
