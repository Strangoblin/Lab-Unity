Shader "InteractionManager/InteractorObject"
{
    // ═══════════════════════════════════════════════════════════════
    //  互动物体 Shader — 两个 Pass，两个相机各取一个：
    //
    //  Pass 0 "InteractionCapture" (LightMode = InteractionCapture)
    //      InteractionCaptureFeature 的颜色捕获用它：采样 _CustomDepthTexture，
    //      输出穿透深度到交互 RT。只有挂了该 Feature 的正交相机会命中这个 Tag。
    //
    //  Pass 1 "Forward" (LightMode = UniversalForward)
    //      主相机走标准 URP 前向，渲染物体本该有的外观。
    //
    //  同一个物体、同一个 Layer 就够了：主相机看不到 Pass 0，正交相机看不到 Pass 1。
    // ═══════════════════════════════════════════════════════════════

    Properties
    {
        _Intensity ("Interaction Intensity", Range(0, 10)) = 1.0
        _BaseColor ("Base Color", Color) = (1, 0.45, 0.15, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float _Intensity;
    half4 _BaseColor;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
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
        //  Pass 0 — 交互捕获（只被交互相机的颜色捕获命中）
        //  Cull Front 渲染背面对比场景深度，输出按压深度。
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "InteractionCapture"
            Tags { "LightMode" = "InteractionCapture" }

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

            #pragma vertex CaptureVert
            #pragma fragment CaptureFrag

            struct CaptureVaryings
            {
                float4 positionHCS : SV_POSITION;
                float  viewDepth   : TEXCOORD0;
                float4 positionHS  : TEXCOORD1;
            };

            CaptureVaryings CaptureVert(Attributes i)
            {
                CaptureVaryings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                o.positionHS  = ComputeScreenPos(o.positionHCS);
                o.viewDepth   = o.positionHCS.z / o.positionHCS.w;
                return o;
            }

            half4 CaptureFrag(CaptureVaryings i) : SV_Target
            {
                float2 screenUV = i.positionHS.xy / i.positionHS.w;

                // 采样场景深度（InteractionCaptureFeature 的 DepthOnly Pass 写入的原始 depth buffer 值）
                float sceneDepth = SAMPLE_TEXTURE2D(_CustomDepthTexture, sampler_CustomDepthTexture, screenUV).r;

                // 正交相机 depth 线性编码: 差值 * (far-near) 得到世界单位米
                // 物体在场景下方时 viewDepth > sceneDepth
                float depthScale = 20.0; // far - near = 10 - (-10)
                float depthDiff  = saturate(-(i.viewDepth - sceneDepth) * depthScale) * _Intensity;
                return depthDiff;
            }
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  Pass 1 — 可见外观（主相机的标准 URP 前向使用）
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag

            struct ForwardVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            ForwardVaryings ForwardVert(Attributes i)
            {
                ForwardVaryings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                return o;
            }

            half4 ForwardFrag(ForwardVaryings i) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
