// ════════════════════════════════════════════════════════════
//  NPRFunction — 漫反射、高光与 Fresnel 边缘光函数
// ════════════════════════════════════════════════════════════
#ifndef NPRFUNCTION_HLSL_INCLUDED
#define NPRFUNCTION_HLSL_INCLUDED

//half_lambert lighting function
float DiffuseLambert(float3 normalWS, float3 lightDirWS)
{
    float NdotL = dot(normalWS, lightDirWS);
    return NdotL;
}

//blinn-phong specular function
float SpecularBlinnPhong(float3 normalWS, float3 lightDirWS, float3 viewDirWS, float smoothness)
{
    if (smoothness <= 0.0)
        return 0.0;
    float3 halfDir = normalize(lightDirWS + viewDirWS);
    float NdotH = max(dot(normalWS, halfDir), 0.0);
    return pow(NdotH, smoothness * 64) * smoothness;
}

float SpecularBlinnPhongPBR(float NdotH, float alpha)
{
    return (1 / alpha) * pow(NdotH, max((2 / alpha - 2), 1e-5));
}

float SpecularKajiyaKayPBR(float BdotH, float alpha)
{
    return (1 / alpha) * pow(BdotH, max((2 / alpha - 2), 1e-5));
}

//fresnel rim lighting function
float RimFresnel(float3 normalWS, float3 viewDirWS, float rimPower)
{
    if (rimPower <= 0.0)
        return 0.0;
    float NdotV = dot(normalWS, viewDirWS);
    return pow(1.0 - saturate(NdotV), 4) * rimPower * 4;
}
#endif