// ════════════════════════════════════════════════════════════
//  LightFunction — URP 主光获取与无阴影额外光补光汇总
// ════════════════════════════════════════════════════════════
#ifndef LIGHTFUNCTION_HLSL_INCLUDED
#define LIGHTFUNCTION_HLSL_INCLUDED

void MainLight(float3 positionWS, out float3 direction, out float3 color, out float distanceAtten, out float shadowAtten)
{
    #if SHADOWS_SCREEN
        float4 positionCS  = TransformWorldToHClip(positionWS);
        float4 shadowCoord = ComputeScreenPos(positionCS);
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    #endif
    Light mainLight = GetMainLight(shadowCoord);

    direction       = mainLight.direction;
    color           = mainLight.color;
    distanceAtten   = mainLight.distanceAttenuation;
    shadowAtten     = mainLight.shadowAttenuation;
}


// ════════════════════════════════════════════════════════════
//  LightFunction_FillColor — 汇总无阴影补光颜色，输出贡献最强局部灯的方向与衰减
// ════════════════════════════════════════════════════════════
float3 LightFunction_FillColor(float3 positionWS, float2 normalizedScreenSpaceUV,
    out float3 direction, out float distanceAtten)
{
    float3 fillColor = 0.0;
    float strongestContribution = 0.0;
    direction = 0.0;
    distanceAtten = 0.0;

    #if defined(_ADDITIONAL_LIGHTS)
        InputData inputData = (InputData)0;
        inputData.positionWS = positionWS;
        inputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;

        #if defined(_LIGHT_LAYERS)
            uint meshRenderingLayers = GetMeshRenderingLayer();
        #endif

        #if USE_CLUSTER_LIGHT_LOOP
            UNITY_LOOP
            for (uint lightIndex = 0;
                 lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS);
                 ++lightIndex)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                Light light = GetAdditionalLight(lightIndex, positionWS);
                #if defined(_LIGHT_LAYERS)
                    if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                        continue;
                #endif
                fillColor += light.color * light.distanceAttenuation;
            }
        #endif

        uint lightCount = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(lightCount)
            Light light = GetAdditionalLight(lightIndex, positionWS);
            #if defined(_LIGHT_LAYERS)
                if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                    continue;
            #endif
            float3 attenuatedColor = light.color * light.distanceAttenuation;
            fillColor += attenuatedColor;

            #if !USE_CLUSTER_LIGHT_LOOP
                int perObjectLightIndex = GetPerObjectLightIndex(lightIndex);
                #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                    if (_AdditionalLightsBuffer[perObjectLightIndex].position.w == 0.0)
                        continue;
                #else
                    if (_AdditionalLightsPosition[perObjectLightIndex].w == 0.0)
                        continue;
                #endif
            #endif

            float contribution = dot(attenuatedColor, float3(0.2126, 0.7152, 0.0722));
            if (contribution > strongestContribution)
            {
                strongestContribution = contribution;
                direction = light.direction;
                distanceAtten = light.distanceAttenuation;
            }
        LIGHT_LOOP_END
    #endif

    return fillColor;
}

#endif
