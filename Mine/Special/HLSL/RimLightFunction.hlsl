//RimLightFunction.hlsl
#ifndef RIMLIGHTFUNCTION_HLSL_INCLUDED
#define RIMLIGHTFUNCTION_HLSL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float ComputeEyeDepth(float2 uv)
{
    float rawDepth = SampleSceneDepth(uv);
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

float RimLightDepth(float3 offsetDirWS, float2 uvOrigin, float offsetFactor)
{
    float2 offsetDirVS = normalize(mul(UNITY_MATRIX_V, float4(offsetDirWS, 0.0)).xy);
    float2 uvOffset = uvOrigin + offsetDirVS * offsetFactor;

    float eyeDepthOrigin = ComputeEyeDepth(uvOrigin);
    float eyeDepthOffset = ComputeEyeDepth(uvOffset);
    float depthDiff = abs(eyeDepthOffset - eyeDepthOrigin);
    return saturate(depthDiff);
}
#endif