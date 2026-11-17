// ═══════════════════════════════════════════════════════════════
//  SandGround — 沙地子集的可见表面
//
//  Forward：**顶点不动**，只把 _InteractionSandTex 的有符号高度换算成法线（法线互动）。
//          坑/脊的形状全靠法线与明暗表达，网格不变形。
//  DepthOnly：只写深度，作为交互相机的参照面（同 SnowGround）。
//
//  透明队列 + ZWrite 开，理由见 SnowGround 顶部说明。
// ═══════════════════════════════════════════════════════════════
Shader "InteractionManager/SandGround"
{
    Properties
    {
        _BaseColor    ("Sand Color", Color)   = (0.78, 0.65, 0.40, 1)
        _ShadowTint   ("Shadow Tint", Color)  = (0.42, 0.33, 0.20, 1)
        _DepthScale   ("Crater Relief (m)", Range(0, 0.6)) = 0.30   // 只用于法线：假定位移这么多米时的坡度
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1.0
        _Smoothness   ("Smoothness", Range(0, 1)) = 0.2
        _RippleScale  ("Ripple Scale", Range(0, 8)) = 1.6
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.12
        // 视差映射步数：0 = 关。沙只对**坑**生效（负值），坑沿的脊仍然只走法线。
        _ParallaxSteps ("Parallax Steps", Range(0, 32)) = 8
    }

    HLSLINCLUDE
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionGroundLighting.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionParallax.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Sand/SandInteraction.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _ShadowTint;
        float _DepthScale;
        float _NormalStrength;
        float _Smoothness;
        float _RippleScale;
        float _RippleStrength;
        float _ParallaxSteps;
    CBUFFER_END

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
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);

                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // 视差映射：只对平面以下的坑生效（sign = -1），坑沿的脊仍然只走法线。
                float3 sampleWS = i.positionWS + InteractionParallaxOffsetWS(
                    _InteractionSandTex, sampler_InteractionSandTex, i.positionWS,
                    normalize(_WorldSpaceCameraPos - i.positionWS), _DepthScale, -1.0,
                    (int)_ParallaxSteps, _InteractionSandValid);

                // 坡度按 _DepthScale 换算成"假定位移了这么多米"的角度；坑和脊只由法线与明暗区分。
                float2 slope = SandHeightSlopeXZ(sampleWS) * _DepthScale;

                // 未扰动沙面的细波纹：只动法线，不动顶点。
                float2 rippleDir = float2(0.8, 0.6);
                float ripplePhase = dot(i.positionWS.xz, rippleDir) * _RippleScale;
                float ripple = cos(ripplePhase) * _RippleStrength;
                slope += rippleDir * ripple;

                float3 normalWS = normalize(float3(-slope.x * _NormalStrength, 1.0, -slope.y * _NormalStrength));

                half4 color = InteractionGroundShade(i.positionWS, normalWS, _BaseColor, _ShadowTint, _Smoothness, 0.0);

                float height = SampleSandHeightWS(sampleWS);
                color.rgb *= 1.0 + saturate(-height) * 0.12;   // 坑底略暗
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragDepth

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings VertDepth(Attributes v)
            {
                DepthVaryings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 FragDepth(DepthVaryings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
