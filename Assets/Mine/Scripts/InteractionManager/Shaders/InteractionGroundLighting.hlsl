// ═══════════════════════════════════════════════════════════════
//  InteractionGroundLighting.hlsl — 交互地面（雪 / 沙 / 草）共用着色
//  只做最朴素的主光 + SH 环境光，避免三个地面 Shader 各写一套。
// ═══════════════════════════════════════════════════════════════

#ifndef INTERACTIONGROUNDLIGHTING_HLSL_INCLUDED
#define INTERACTIONGROUNDLIGHTING_HLSL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// metallic：0 = 纯漫反射（单层地面一直用这个值，输出与从前完全一致）；
// 越大漫反射越弱、高光越接近底色，用来区分雪、湿沙这类层。
half4 InteractionGroundShade(float3 positionWS, float3 normalWS, half4 albedo,
                             half4 shadowTint, half smoothness, half metallic)
{
    Light mainLight = GetMainLight();
    float3 N = normalize(normalWS);
    float3 L = mainLight.direction;
    float3 V = normalize(_WorldSpaceCameraPos - positionWS);
    float3 H = normalize(L + V);

    float ndl = saturate(dot(N, L));
    float metal = saturate(metallic);
    float specPower = exp2(lerp(2.0, 10.0, saturate(smoothness)));
    float spec = pow(saturate(dot(N, H)), specPower) * smoothness * 0.25;

    float3 ambient = SampleSH(N);
    float3 diffuse = lerp(shadowTint.rgb, albedo.rgb, ndl) * (1.0 - metal);
    float3 specTint = lerp(half3(1.0, 1.0, 1.0), albedo.rgb, metal);
    float3 color = diffuse * (ambient + mainLight.color * ndl)
                 + mainLight.color * spec * specTint;
    return half4(color, 1.0);
}

#endif // INTERACTIONGROUNDLIGHTING_HLSL_INCLUDED
