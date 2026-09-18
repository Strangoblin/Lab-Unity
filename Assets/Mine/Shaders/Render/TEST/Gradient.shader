Shader "Render/Gradient"
{
    Properties
    {
        _Offset ("Offset", Float) = 0
        _Texture ("Texture", 2D) = "white" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    float _Offset;
    TEXTURE2D(_Texture);
    SAMPLER(sampler_Texture);

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        return output;
    }

    half4 Frag(Varyings input) : SV_TARGET
    {
        float2 uv1 = input.uv;
        float2 uv2 = input.uv + float2(_Offset, _Offset);
        float2 uv3 = input.uv - float2(_Offset, _Offset);
        float color1 = _Texture.Sample(sampler_Texture, uv1).r;
        float color2 = _Texture.Sample(sampler_Texture, uv2).g;
        float color3 = _Texture.Sample(sampler_Texture, uv3).b;

        return half4(color1, color2, color3, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}