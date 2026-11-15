// ════════════════════════════════════════════════════════════
//  ColoredGlass — 彩窗玻璃
//
//  Forward：双面 Unlit 可见窗面。
//  ColoredShadow（LightMode）：被 ColoredShadowFeature 从主光视角绘制到
//  光空间彩色透射图，RGB = 透射色，A = 归一化光空间深度。
// ════════════════════════════════════════════════════════════
Shader "Render/ColoredGlass"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" { }
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    TRANSFORM_TEX(input.uv, _BaseMap)) * _BaseColor;
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ColoredShadow"
            Tags { "LightMode" = "ColoredShadow" }

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertColoredShadow
            #pragma fragment FragColoredShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4x4 _ColoredShadowWorldToLight;
            float4 _ColoredShadowParams;   // xy = center, zw = size
            float4 _ColoredShadowDepth;    // x = zMin, y = zMax, z = enabled, w = bias

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionLS : TEXCOORD1;
            };

            Varyings VertColoredShadow(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 pLS = mul(_ColoredShadowWorldToLight, float4(positionWS, 1.0)).xyz;
                float2 uv = (pLS.xy - _ColoredShadowParams.xy) / _ColoredShadowParams.zw + 0.5;

                output.positionCS = float4(uv * 2.0 - 1.0, 0.5, 1.0);
                output.uv = input.uv;
                output.positionLS = pLS;
                return output;
            }

            half4 FragColoredShadow(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    TRANSFORM_TEX(input.uv, _BaseMap)).rgb * _BaseColor.rgb;

                float depth = saturate(
                    (input.positionLS.z - _ColoredShadowDepth.x)
                    / (_ColoredShadowDepth.y - _ColoredShadowDepth.x));

                return half4(color, depth);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
