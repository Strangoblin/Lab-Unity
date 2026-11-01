// ════════════════════════════════════════════════════════════
//  HDRMapReflectionDemo — observe HDRMap reflection sampling on a mesh
// ════════════════════════════════════════════════════════════
Shader "Render/HDRMapReflectionDemo"
{
    Properties
    {
        [Header(Source)]
        [NoScaleOffset] _HDRMap("HDR Map", 2D) = "grey" {}
        [Enum(Equirectangular, 0, Octahedral, 1, EqualAreaOctahedral, 2, Hemisphere, 3)]
        _MapProjection("Map Projection", Float) = 0
        [Enum(Panorama, 0, Interior, 1)] _MapCoverage("Map Coverage", Float) = 0

        [Header(Display)]
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _ReflectionStrength("Reflection Strength", Range(0, 4)) = 1
        _Exposure("Exposure", Range(0, 8)) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Special/HLSL/HDRMapFunction.hlsl"

    TEXTURE2D(_HDRMap);
    SAMPLER(sampler_HDRMap);
    CBUFFER_START(UnityPerMaterial)
        float4 _Tint;
        float _MapProjection;
        float _MapCoverage;
        float _ReflectionStrength;
        float _Exposure;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        return output;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        float3 normalWS = normalize(input.normalWS);
        float3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
        float4 reflected = HDRMapSampleReflection(
            TEXTURE2D_ARGS(_HDRMap, sampler_HDRMap),
            normalWS,
            viewDirectionWS,
            (int)round(_MapProjection),
            (int)round(_MapCoverage));
        float3 color = reflected.rgb * _Tint.rgb * _ReflectionStrength * _Exposure;
        return float4(color, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "HDRMapReflectionDemo"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
