// ═══════════════════════════════════════════════════════════════
//  GrassGround — 草地子集的可见地面
//
//  草本身由 GrassChunkManager 绘制；这块地面只负责两件事：
//  1. 给草一个可见的底色；
//  2. 提供交互相机用的平坦参照面（DepthOnly，不做位移）。
//
//  与 SnowGround / SandGround 一样放在透明队列，交给
//  TransparentDepthMergeFeature 合并参照深度。
// ═══════════════════════════════════════════════════════════════
Shader "InteractionManager/GrassGround"
{
    Properties
    {
        _BaseColor    ("Ground Color", Color)  = (0.24, 0.33, 0.15, 1)
        _ShadowTint   ("Shadow Tint", Color)   = (0.10, 0.16, 0.08, 1)
        _Smoothness   ("Smoothness", Range(0, 1)) = 0.1
        _PatchScale   ("Patch Scale", Range(0, 8)) = 1.4
        _PatchStrength("Patch Strength", Range(0, 1)) = 0.18
    }

    HLSLINCLUDE
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionGroundLighting.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _ShadowTint;
        float _Smoothness;
        float _PatchScale;
        float _PatchStrength;
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
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float patch = sin(i.positionWS.x * _PatchScale) * cos(i.positionWS.z * _PatchScale * 0.7);
                half4 albedo = _BaseColor;
                albedo.rgb *= 1.0 + patch * _PatchStrength;

                float3 normalWS = normalize(i.normalWS);
                return InteractionGroundShade(i.positionWS, normalWS, albedo, _ShadowTint, _Smoothness, 0.0);
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
