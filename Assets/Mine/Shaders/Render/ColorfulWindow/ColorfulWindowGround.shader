// ════════════════════════════════════════════════════════════
//  ColorfulWindowGround — 彩窗测试地面
//
//  仅用于 ColorfulWindowTest.unity：以 URP 主光的彩色 Cookie 作为
//  逐像素透射色，验证地面与 SSL 读到的是同一份光照数据。
//  Forward 走 3 参数 GetMainLight，_LIGHT_COOKIES 变体开启时
//  URP 会把 SampleMainLightCookie(positionWS) 乘进 light.color。
// ════════════════════════════════════════════════════════════
Shader "Render/ColorfulWindowGround"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _Ambient ("Ambient", Range(0.0, 0.5)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Ambient;
            CBUFFER_END

            // 彩窗光平面（场景组件发布；无组件时 mask=0）
            float4 _ColorfulWindowPlane;
            float  _ColorfulWindowMask;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // ════════════════════════════════════════════════════════
            //  Frag — 主光直接光；3 参数重载按 _LIGHT_COOKIES 乘彩窗色
            // ════════════════════════════════════════════════════════
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                #if defined(_LIGHT_COOKIES)
                    // 手动乘 Cookie：房间内侧取彩窗色，窗平面外侧保持阳光
                    float2 cookieUV = ComputeLightCookieUVDirectional(
                        _MainLightWorldToLight,
                        input.positionWS,
                        float4(1.0, 1.0, 0.0, 0.0),
                        URP_TEXTURE_WRAP_MODE_NONE);
                    float3 cookie = (cookieUV.x < 0.0 || cookieUV.x > 1.0 ||
                                     cookieUV.y < 0.0 || cookieUV.y > 1.0)
                        ? float3(1.0, 1.0, 1.0)
                        : SampleMainLightCookie(input.positionWS);
                    if (_ColorfulWindowMask > 0.5)
                    {
                        float side = dot(input.positionWS, _ColorfulWindowPlane.xyz)
                                   + _ColorfulWindowPlane.w;
                        cookie = side < 0.0 ? float3(1.0, 1.0, 1.0) : cookie;
                    }
                    mainLight.color *= cookie;
                #endif

                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 direct = _BaseColor.rgb * mainLight.color
                              * (NdotL * mainLight.shadowAttenuation);
                float3 ambient = _BaseColor.rgb * _Ambient;
                return half4(direct + ambient, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertShadow
            #pragma fragment FragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Ambient;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings VertShadow(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 FragShadow(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
