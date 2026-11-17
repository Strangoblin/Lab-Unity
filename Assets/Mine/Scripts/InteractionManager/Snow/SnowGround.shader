// ═══════════════════════════════════════════════════════════════
//  SnowGround — 雪地子集的可见表面
//
//  Forward：**顶点不动**，只把 _InteractionSnowTex 的压痕换算成法线（法线互动）。
//           不做网格变形 —— 地面网格多疏都不影响痕迹解析度，也不会顶穿参照面。
//  DepthOnly：只写深度，作为交互相机的参照面（TransparentDepthMergeFeature
//             按 layer 6 + DepthOnly 把它并进 _CameraDepthTexture）。
//
//  放在透明队列是为了让现有的 TransparentDepthMergeFeature 能接管参照面
//  （该 Feature 固定过滤 RenderQueueRange.transparent）；颜色不透明，ZWrite 开。
// ═══════════════════════════════════════════════════════════════
Shader "InteractionManager/SnowGround"
{
    Properties
    {
        _BaseColor    ("Snow Color", Color)   = (0.92, 0.95, 1.0, 1)
        _ShadowTint   ("Shadow Tint", Color)  = (0.50, 0.63, 0.82, 1)
        _DepthScale   ("Imprint Relief (m)", Range(0, 0.6)) = 0.22   // 只用于法线：假定位移这么多米时的坡度
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.9
        _Smoothness   ("Smoothness", Range(0, 1)) = 0.35
        // 视差映射步数：0 = 关（只用坡度法线），8–16 = 开。只改采样位置，不动顶点也不动深度。
        _ParallaxSteps ("Parallax Steps", Range(0, 32)) = 8
    }

    HLSLINCLUDE
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionGroundLighting.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionParallax.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Snow/SnowInteraction.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _ShadowTint;
        float _DepthScale;
        float _NormalStrength;
        float _Smoothness;
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

        // ── 可见表面：顶点不动，压痕只走法线 ─────────────────
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
                // 视差映射：只把采样点沿视线推进到坑底真正可见的位置，顶点和深度都不动。
                float3 sampleWS = i.positionWS + InteractionParallaxOffsetWS(
                    _InteractionSnowTex, sampler_InteractionSnowTex, i.positionWS,
                    normalize(_WorldSpaceCameraPos - i.positionWS), _DepthScale, 1.0,
                    (int)_ParallaxSteps, _InteractionSnowValid);

                // 坡度按 _DepthScale 换算成"假定位移了这么多米"的角度；形状只由法线和明暗表达。
                float2 slope = SnowImprintSlopeXZ(sampleWS) * _DepthScale;
                float3 normalWS = normalize(float3(-slope.x * _NormalStrength, 1.0, -slope.y * _NormalStrength));

                half4 color = InteractionGroundShade(i.positionWS, normalWS, _BaseColor, _ShadowTint, _Smoothness, 0.0);

                // 压痕深处略暗，让凹槽在近距离也读得出来。
                float imprint = SampleSnowImprintWS(sampleWS);
                color.rgb *= 1.0 - saturate(imprint) * 0.18;
                return color;
            }
            ENDHLSL
        }

        // ── 参照面：不做位移，只写深度 ─────────────────────────
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
