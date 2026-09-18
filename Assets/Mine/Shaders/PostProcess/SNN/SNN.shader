Shader "PostProcess/SNN"
{
    Properties
    {
        _Radius ("Radius", Range(1,10)) = 5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float _Radius;

    static const float2 SNNPairOffsets[4] = {
        float2(1.0, 0.0), float2(0.0, 1.0),
        float2(0.70710678, 0.70710678), float2(-0.70710678, 0.70710678)
    };

    float ComputePixelDistance(float3 colorA, float3 colorB)
    {
        float3 diff = colorA - colorB;
        return dot(diff, diff);
    }

    half4 Frag_SNN(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float3 colorOrigin = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

        float radius = max(_Radius, 0.0);
        float3 colorSum = colorOrigin;
        const float sampleCount = 5.0;

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
        colorSum /= sampleCount;
        return half4(colorSum, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "SNN"

            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SNN
            ENDHLSL
        }
    }
}
