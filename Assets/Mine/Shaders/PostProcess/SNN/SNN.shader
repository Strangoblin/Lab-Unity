// ════════════════════════════════════════════════════════════
//  SNN — 对称最近邻滤波（Symmetric Nearest Neighbour）
//
//  Unity 6 URP 17+ 全屏后处理。四组对称样本对，各取更接近中心色
//  的一侧求和取均值，保边去噪。采样预算由编译期常量 4 对固定，
//  _Radius 只缩放偏移，不参与循环边界。
//  _DebugMode = 1 时只输出被选中的四个邻域均值（不含中心），
//  用于观察滤波取向，不另建 Pass。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/SNN"
{
    Properties
    {
        _Radius ("Radius", Range(1, 10)) = 5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        float _Radius;
        float _DebugMode;   // 0 = Off，1 = Selection（选中邻域均值）
    CBUFFER_END

    // ── 常量 ──
    // 采样预算固定：4 组对称样本对 + 中心像素
    static const float  SNNSampleCount = 5.0;
    static const float  SNNPairCount   = 4.0;

    static const float2 SNNPairOffsets[4] =
    {
        float2(1.0, 0.0), float2(0.0, 1.0),
        float2(0.70710678, 0.70710678), float2(-0.70710678, 0.70710678)
    };

    // ════════════════════════════════════════════════════════════
    //  像素距离 — 平方欧氏距离，仅用于同尺度比较，免开方
    // ════════════════════════════════════════════════════════════
    float ComputePixelDistance(float3 colorA, float3 colorB)
    {
        float3 diff = colorA - colorB;
        return dot(diff, diff);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_SNN — 中心像素与四对样本，每对取距离更近的一侧累加取均
    // ════════════════════════════════════════════════════════════
    half4 Frag_SNN(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float3 colorOrigin = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

        float radius = max(_Radius, 0.0);
        float3 colorSum = colorOrigin;

        [unroll]
        for (int i = 0; i < 4; i++)
        {
            float2 offset = SNNPairOffsets[i] * radius * texelSize;
            float3 colorA = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).rgb;
            float3 colorB = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset).rgb;
            float distA = ComputePixelDistance(colorOrigin, colorA);
            float distB = ComputePixelDistance(colorOrigin, colorB);
            colorSum += distA < distB ? colorA : colorB;
        }

        if (_DebugMode > 0.5)
            return half4((colorSum - colorOrigin) / SNNPairCount, 1.0);

        colorSum /= SNNSampleCount;
        return half4(colorSum, 1.0);
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
            Name "SNN"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SNN
            ENDHLSL
        }
    }
}
