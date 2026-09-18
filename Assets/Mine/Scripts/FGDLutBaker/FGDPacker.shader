// ════════════════════════════════════════════════════════════════════════════
//  FGDPacker — FGD LUT 烘焙 Pixel Shader
//  逐像素调用 Unity 内置 IntegrateGGXAndDisneyDiffuseFGD，
//  用 SV_Position 计算像素坐标→UV，绕过 URP 兼容模式下的 texcoord 传递问题。
//  positionSS.x / _LutResolution.x = NdotV,  positionSS.y / _LutResolution.y = roughness
// ════════════════════════════════════════════════════════════════════════════
Shader "FGDLutBaker/FGDPacker"
{
    Properties
    {
        _SampleCount("Sample Count", Float) = 1024
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FGDPack"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

            float _SampleCount;
            float2 _LutResolution;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input, float4 positionSS : SV_Position) : SV_Target
            {
                // 从 screen-space 像素坐标推导 UV：像素中心 + 0.5 偏移
                float NdotV     = (positionSS.x + 0.5) / _LutResolution.x;
                float roughness = (positionSS.y + 0.5) / _LutResolution.y;
                return IntegrateGGXAndDisneyDiffuseFGD(NdotV, roughness, (uint)_SampleCount);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
