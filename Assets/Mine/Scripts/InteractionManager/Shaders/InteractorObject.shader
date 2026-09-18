Shader "InteractionManager/InteractorObject"
{
    // ═══════════════════════════════════════════════════════════════
    //  互动物体 Shader — 两个 Pass：
    //
    //  Pass 0 (InteractionPass, UniversalForward): DrawObjectsPass 使用，
    //     输出单通道交互强度，写入 _CameraColorTexture (R8/RFloat)。
    //
    //  Pass 1 (DepthDifferencePass): 保留的深度比较逻辑，采样场景深度计算
    //     按压深度。供后续多 Pass Shader 集成参考。
    // ═══════════════════════════════════════════════════════════════

    Properties
    {
        _Intensity ("Interaction Intensity", Range(0, 10)) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float _Intensity;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionHCS : SV_POSITION;
        float2 uv          : TEXCOORD0;
        float  viewDepth   : TEXCOORD1;
    };
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalRenderPipeline"
        }
        LOD 100

        // ════════════════════════════════════════════════════════════
        //  Pass 0 — 深度预写入 (DepthOnly)
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            DepthVaryings DepthVert(Attributes i)
            {
                DepthVaryings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                return o;
            }

            half4 DepthFrag(DepthVaryings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  Pass 1 — 深度比较 (UniversalForward)
        //  Cull Front 渲染背面对比场景深度，输出按压深度。
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "DepthDifferencePass"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest Always
            Blend One Zero

            Stencil
            {
                Ref 2
                Comp Always
                Pass Replace
                WriteMask 2
            }

            HLSLPROGRAM
            TEXTURE2D(_CustomDepthTexture);
            SAMPLER(sampler_CustomDepthTexture);

            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                float  viewDepth   : TEXCOORD0;
                float4 positionHS  : TEXCOORD1;
            };

            DepthVaryings DepthVert(Attributes i)
            {
                DepthVaryings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                o.positionHS  = ComputeScreenPos(o.positionHCS);
                o.viewDepth   = o.positionHCS.z / o.positionHCS.w;
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_Target
            {
                float2 screenUV = i.positionHS.xy / i.positionHS.w;

                // 采样场景深度（CustomRenderer DepthOnly Pass 写入的原始 depth buffer 值）
                float sceneDepth = SAMPLE_TEXTURE2D(_CustomDepthTexture, sampler_CustomDepthTexture, screenUV).r;

                // 正交相机 depth 线性编码: 差值 * (far-near) 得到世界单位米
                // 物体在场景下方时 viewDepth > sceneDepth
                float depthScale = 20.0; // far - near = 10 - (-10)
                float depthDiff  = saturate(-(i.viewDepth - sceneDepth) * depthScale) * _Intensity;
                return depthDiff;
            }
            ENDHLSL
        }
    }
}
