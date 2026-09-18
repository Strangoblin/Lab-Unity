Shader "PostProcess/RimToonStylized"
{
    Properties
    {
        [HDR]_OutlineColorA ("Outline Color A", Color) = (0,0,0,1)
        [HDR]_OutlineColorB ("Outline Color B", Color) = (1,1,1,1)
        _OutlineScaleA ("Outline Scale A", Range(0,1)) = 0.2
        _OutlineScaleB ("Outline Scale B", Range(0,1)) = 0.2
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _NoiseScale ("Noise Scale", Float) = 1.0
        _NoiseSpeed ("Noise Speed", Float) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 screenUV   : TEXCOORD0;
        float2 uv         : TEXCOORD1;
    };

    Texture2D _NoiseTex;
    SamplerState sampler_NoiseTex;

    float4 _OutlineColorA;
    float4 _OutlineColorB;
    float  _OutlineScaleA;
    float  _OutlineScaleB;
    float  _NoiseScale;
    float  _NoiseSpeed;

    Varyings Vert(Attributes input)
    {
        Varyings output;
        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        float4 positionCS = TransformWorldToHClip(positionWS);

        float2 screenUV = float2(positionCS.x, positionCS.y * _ProjectionParams.x) / positionCS.w * 0.5 + 0.5;
        float2 noiseUV  = screenUV * _NoiseScale + _Time.y * _NoiseSpeed;
        float noiseValueA = 1;
        float noiseValueB = _NoiseTex.SampleLevel(sampler_NoiseTex, noiseUV, 0).r;

        float3 offset = normalWS * (_OutlineScaleA * noiseValueA - _OutlineScaleB * noiseValueB);
        
        output.positionCS = TransformWorldToHClip(positionWS + offset);
        output.screenUV = float2(positionCS.x, positionCS.y * _ProjectionParams.x) / positionCS.w * 0.5 + 0.5;
        output.uv = input.uv;
        return output;
    }

    half4 Frag(Varyings input) : SV_Target
    {
        float2 screenUV = input.screenUV;
        float2 noiseUV  = screenUV * _NoiseScale + _Time.y * _NoiseSpeed;
        float  colorMix = _NoiseTex.Sample(sampler_NoiseTex, noiseUV).r;
        half4  color    = lerp(_OutlineColorA, _OutlineColorB, colorMix);
        return color;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" }

        LOD 200

        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode"="SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One Zero

            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
