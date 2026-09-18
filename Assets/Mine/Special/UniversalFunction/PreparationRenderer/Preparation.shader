Shader "PreparationRenderer/Preparation"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionHCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    TEXTURE2D(_CameraDepthTexture);
    SAMPLER(sampler_CameraDepthTexture);
    TEXTURE2D(_CameraColorTexture);
    SAMPLER(sampler_CameraColorTexture);
    TEXTURE2D(_CameraNormalTexture);
    SAMPLER(sampler_CameraNormalTexture);

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionHCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        return output;
    }

    float Frag_SampleDepth(Varyings input) : SV_Target
    {
        float depth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, input.uv, 0).r;
        return depth;
    }

    half4 Frag_SampleColor(Varyings input) : SV_Target
    {
        half4 color = SAMPLE_TEXTURE2D_LOD(_CameraColorTexture, sampler_CameraColorTexture, input.uv, 0);
        return color;
    }

    half4 Frag_SampleNormal(Varyings input) : SV_Target
    {
        half4 normal = SAMPLE_TEXTURE2D_LOD(_CameraNormalTexture, sampler_CameraNormalTexture, input.uv, 0);
        return normal;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "PreDepthPass"

            Cull Off
            ZWrite On
            ZTest Always
            
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_SampleDepth
            ENDHLSL
        }

        Pass
        {
            Name "PreColorPass"

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_SampleColor
            ENDHLSL
        }
        
        Pass
        {
            Name "PreNormalPass"

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_SampleNormal
            ENDHLSL
        }
    }
}
