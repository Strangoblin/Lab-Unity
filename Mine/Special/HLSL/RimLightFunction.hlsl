//RimLightFunction.hlsl
#ifndef RIMLIGHTFUNCTION_HLSL_INCLUDED
#define RIMLIGHTFUNCTION_HLSL_INCLUDED

float ComputeEyeDepth(float2 uv)
{
    float rawDepth = SampleSceneDepth(uv);
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

float RimLightDepth(float3 lightDirWS, float2 uvOrigin, float rimPower)
{
    if (rimPower <= 0.0) return 0.0;

    float3 lightDirVS = mul(UNITY_MATRIX_V, float4(lightDirWS, 0.0)).xyz;
    float2 offsetDir = normalize(lightDirVS.xy);
    float  offsetFactor = 0.01 / rimPower;
    float2 uvOffset = uvOrigin + offsetDir * offsetFactor;

    float eyeDepthOrigin = ComputeEyeDepth(uvOrigin);
    float eyeDepthOffset = ComputeEyeDepth(uvOffset);
    float depthDiff = abs(eyeDepthOffset - eyeDepthOrigin);
    return saturate(depthDiff);
}
#endif