// ════════════════════════════════════════════════════════════
//  DDOF — 动态景深（Dynamic Depth of Field）
//
//  Unity 6 URP 17+ 全屏后处理。四步管线：
//    Pass 0  由场景深度生成 CoC 遮罩（R = 失焦量，G = 距离遮罩）
//    Pass 1  按 CoC 缩放的水平高斯模糊
//    Pass 2  按 CoC 缩放的垂直高斯模糊
//    Pass 3  原图与模糊结果按遮罩插值合成
//  Pass 编排在 DDOFFeature.cs，本文件只提供四个片元入口。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/DDOF"
{
    Properties
    {
        _FocusRange ("Focus Range", Range(0,50)) = 5.0
        _BlurScale ("Blur Scale", Range(0,5)) = 2.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Special/HLSL/HashFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        float _FocusRange;
        float _BlurScale;
    CBUFFER_END

    // ── 中间纹理（Feature 端 cmd.SetGlobalTexture 发布） ──
    TEXTURE2D_X(_DDOFTempMainTex);
    TEXTURE2D_X(_DDOFCoCTex);

    // ════════════════════════════════════════════════════════════
    //  ComputeDepth — 由原始深度解出 (线性眼空间深度, 线性 0-1 深度, 原始深度)
    // ════════════════════════════════════════════════════════════
    float3 ComputeDepth(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float l01Depth = Linear01Depth(rawDepth, _ZBufferParams);
        return float3(abs(eyeDepth), l01Depth, rawDepth);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 0: CoC 遮罩 — 与屏幕中心深度的差超出对焦范围即失焦
    //
    //  输出：R = 失焦量, G = 距离遮罩 (2 - l01Depth)
    // ════════════════════════════════════════════════════════════
    float4 Frag_CoC(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float  centerDepth = ComputeDepth(float2(0.5, 0.5)).x;
        float  pixelDepth  = ComputeDepth(uv).x;
        float  l01Depth    = ComputeDepth(uv).y;
        float  baseMask = saturate((abs(pixelDepth - centerDepth) - _FocusRange) / _FocusRange);
        float  distMask = (1 - l01Depth) + 1;
        return float4(baseMask, distMask, 0, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 1: 水平模糊 — 调用 BlurFunction.hlsl，模糊半径由 CoC 缩放
    // ════════════════════════════════════════════════════════════
    float4 Frag_BlurHorizontal(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float  coc = SAMPLE_TEXTURE2D_X(_DDOFCoCTex, sampler_LinearClamp, uv).g;
        float  blurScale = coc * _BlurScale;
        return BlurHorizontal(uv, texelSize, blurScale, _BlitTexture, sampler_LinearClamp);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 2: 垂直模糊 — 与水平同参，半径取自同一 CoC
    // ════════════════════════════════════════════════════════════
    float4 Frag_BlurVertical(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float  coc = SAMPLE_TEXTURE2D_X(_DDOFCoCTex, sampler_LinearClamp, uv).g;
        float  blurScale = coc * _BlurScale;
        return BlurVertical(uv, texelSize, blurScale, _BlitTexture, sampler_LinearClamp);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 3: 合成 — 原图与模糊图按 CoC 遮罩 lerp 输出到屏幕
    // ════════════════════════════════════════════════════════════
    half4 Frag_DDOF(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;

        half4 main = SAMPLE_TEXTURE2D_X(_DDOFTempMainTex, sampler_LinearClamp, uv);
        half  mask = SAMPLE_TEXTURE2D_X(_DDOFCoCTex, sampler_LinearClamp, uv).r;
        half4 blur = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        return lerp(main, blur, mask);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "DDOF_CoC"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_CoC
            ENDHLSL
        }

        Pass
        {
            Name "DDOF_BlurH"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "DDOF_BlurV"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "DDOF_Main"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_DDOF
            ENDHLSL
        }
    }
}
