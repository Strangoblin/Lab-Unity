// ════════════════════════════════════════════════════════════
//  ColoredGlass — 彩窗玻璃（普通透明队列 + 光空间透射图）
//
//  Forward：普通透明玻璃。Blend + ZWrite Off，由 URP 按距离正常排序；
//          放在 TransparentWithShadow 层即可被主管线画出来。
//  TransparentShadow（LightMode）：被 TransparentShadowFeature 从主光视角
//  绘制到光空间透射图，RGB = 透射色，A = 归一化光空间深度。
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
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

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
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    TRANSFORM_TEX(input.uv, _BaseMap)) * _BaseColor;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "TransparentShadow"
            Tags { "LightMode" = "TransparentShadow" }

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertTransparentShadow
            #pragma fragment FragTransparentShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4x4 _TransparentShadowWorldToLight;
            float4 _TransparentShadowParams;   // xy = center, zw = size
            float4 _TransparentShadowDepth;    // x = zMin, y = zMax, z = enabled, w = bias

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

            Varyings VertTransparentShadow(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 pLS = mul(_TransparentShadowWorldToLight, float4(positionWS, 1.0)).xyz;
                float2 uv = (pLS.xy - _TransparentShadowParams.xy) / _TransparentShadowParams.zw + 0.5;

                output.positionCS = float4(uv * 2.0 - 1.0, 0.5, 1.0);
                output.uv = input.uv;
                output.positionLS = pLS;
                return output;
            }

            half4 FragTransparentShadow(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    TRANSFORM_TEX(input.uv, _BaseMap)).rgb * _BaseColor.rgb;

                float depth = saturate(
                    (input.positionLS.z - _TransparentShadowDepth.x)
                    / (_TransparentShadowDepth.y - _TransparentShadowDepth.x));

                return half4(color, depth);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
