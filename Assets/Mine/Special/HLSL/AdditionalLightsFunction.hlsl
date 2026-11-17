//AdditionalLightsFunction.hlsl
#ifndef ADDITIONALLIGHTFUNCTION_HLSL_INCLUDED
#define ADDITIONALLIGHTFUNCTION_HLSL_INCLUDED

#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile _ _SHADOWS_SOFT

#pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX

void MainLight(float3 positionWS, out float3 direction, out float3 color, out float distanceAtten, out float shadowAtten)
{
    Light mainLight = GetMainLight();

    direction       = mainLight.direction;
    color           = mainLight.color;
    distanceAtten   = mainLight.distanceAttenuation;

    // 必须走 MainLightShadow 而不是 GetMainLight(shadowCoord)：
    // 后者内部只调 MainLightRealtimeShadow，**不含阴影距离淡出**。
    // 超出 shadow distance 后 shadowCoord 越界，阴影贴图的比较采样是 clamp 的，
    // 会读到贴图边缘的纹素 —— 表现为远处地形/草随机变暗、随相机移动闪烁。
    // MainLightShadow 会用 GetMainLightShadowFade 把实时阴影按距离 lerp 回 1。
    // 无光照贴图，baked 恒为 1，occlusionProbeChannels 传 0。
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    shadowAtten = MainLightShadow(shadowCoord, positionWS, half4(1, 1, 1, 1), half4(0, 0, 0, 0));
}

float AdditionalLights(float3 positionWS, float3 normalWS, float3 viewDirWS, float smoothness)
{
    float3 addColor = float3(0,0,0);
    int count = GetAdditionalLightsCount();
    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
    for (int i = 0; i < count; i++)
    {
        Light light = GetAdditionalLight(i, positionWS, unity_ProbesOcclusion);
        float3 lightDir   = light.direction;
        float3 lightColor = light.color;
        float  lightAtten = light.distanceAttenuation * light.shadowAttenuation;

        float diffuse  = saturate(DiffuseLambert(normalWS, lightDir));
        float specular = SpecularBlinnPhong(normalWS, lightDir, viewDirWS, smoothness);

        addColor += lightAtten * (diffuse + specular) * lightColor;
    }
    #endif
    return addColor;
}
#endif