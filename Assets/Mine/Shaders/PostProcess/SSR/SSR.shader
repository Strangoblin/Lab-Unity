Shader "PostProcess/SSR"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    HLSLINCLUDE
    // ── URP 核心 ──
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

    // ════════════════════════════════════════════════════════════
    //  共享 Uniforms
    // ════════════════════════════════════════════════════════════

    float _StepSize;
    float _MaxDistance;

    float _Thickness;
    float _Smoothness;
    float _JitterScale;
    float _BlurScale;

    int _StepCount;
    int _BinCount;

    int _FromMipLevel;
    int _MaxMipLevel;
    float4 _TexelSize;

    float4x4 _CameraViewMatrix;
    float4x4 _CameraProjectionMatrix;

    // ════════════════════════════════════════════════════════════
    //  命中验证（采样层 + 步进层共享）
    // ════════════════════════════════════════════════════════════

    float4 HitProcess(float4 color, float3 reflectDir, float2 currentUV)
    {
        float3 normalHitWS = SampleSceneNormals(currentUV);
        float ndotr = dot(normalHitWS, -reflectDir);
        if (ndotr <= 0.0)
            return color;

        float3 result = SampleSceneColor(currentUV);
        float alpha = 1.0;
        float2 edge  = abs(currentUV * 2 - 1);
        float edgeFade = 1 - pow(max(edge.x, edge.y), 4);
        alpha *= edgeFade;
        alpha *= _Smoothness;

        return float4(result, alpha);
    }

    // ════════════════════════════════════════════════════════════
    //  项目 includes（按依赖顺序）
    // ════════════════════════════════════════════════════════════

    #include "Assets/Mine/Shaders/PostProcess/SSR/RayMarchFunction.hlsl"   // 步进策略
    #include "Assets/Mine/Shaders/PostProcess/SSR/RaySampleFunction.hlsl"   // 采样策略
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"                   // 模糊函数

    // ════════════════════════════════════════════════════════════
    //  模糊 Pass 入口
    // ════════════════════════════════════════════════════════════

    half4 Frag_BlurHorizontal(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
        return BlurHorizontal(uv, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }

    half4 Frag_BlurVertical(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
        return BlurVertical(uv, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }

    // ════════════════════════════════════════════════════════════
    //  主 Pass 入口 — 关键字分发到采样策略
    // ════════════════════════════════════════════════════════════

    half4 Frag(Varyings input) : SV_Target
    {
        #if defined(SSR_RAY3D)
            return Frag_SSR_RAY3D(input);
        #else
            return Frag_SSR_DDA2D(input);
        #endif
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "SSR_Raymarch"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma multi_compile _ SSR_DDA2D SSR_RAY3D SSR_HIZ2D
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SSR_BlurHorizontal"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "SSR_BlurVertical"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "SSR_HiZDepthMip"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_HiZDepthMip
            ENDHLSL
        }
    }
}
