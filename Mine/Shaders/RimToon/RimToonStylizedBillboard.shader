Shader "Custom/RimToonStylizedBillboard"
{
    Properties
    {
        [HDR]_OutlineColorA ("Outline Color A", Color) = (0,0,0,1)
        [HDR]_OutlineColorB ("Outline Color B", Color) = (1,1,1,1)
        _OutlineScaleA ("Outline Scale A", Range(0,20)) = 0.2
        _OutlineScaleB ("Outline Scale B", Range(0,1)) = 0.2
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _NoiseScale ("Noise Scale", Float) = 1.0
        _NoiseSpeed ("Noise Speed", Float) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Assets/Mine/Special/HLSL/SDF.hlsl"
    #include "Assets/Mine/Special/HLSL/TBN.hlsl"

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv         : TEXCOORD;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionOS : TEXCOORD0;
        float2 screenUV   : TEXCOORD1;
        float2 uv         : TEXCOORD2;
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
        float3 local = input.positionOS.xyz;
        float3 center = float3(0,0,0);
        float3 tangent, bitangent, normal;
        BillboardTBN(center, tangent, bitangent, normal);

        float3 positionOS = local.x * tangent + local.y * normal + local.z * bitangent;
        float4 positionCS = TransformObjectToHClip(positionOS);

        output.positionCS = positionCS;
        output.positionOS = positionOS;
        output.screenUV = float2(positionCS.x, positionCS.y * _ProjectionParams.x) / positionCS.w * 0.5 + 0.5;
        output.uv = input.uv;
        return output;
    }

    half4 Frag(Varyings input) : SV_Target
    {
        float3 positionOS = input.positionOS;
        float  alphaA   = saturate( - SphereSDF(positionOS, _OutlineScaleA) * _OutlineScaleB );

        float2 screenUV = input.screenUV;
        float2 noiseUV = screenUV * _NoiseScale + _Time.y * _NoiseSpeed;
        float  alphaB = _NoiseTex.Sample(sampler_NoiseTex, noiseUV).r;

        half4 color = lerp(_OutlineColorA, _OutlineColorB, alphaB);
        half  alpha = alphaA * alphaB;
        alpha = step(1-alphaA, alpha);
        return half4(color.rgb, alpha);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" }

        Pass
        {
            Name "BillboardEffect"

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

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
