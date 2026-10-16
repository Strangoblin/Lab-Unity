// ════════════════════════════════════════════════════════════
//  HDRMapSkybox — direction sampling for all supported HDR image projections
// ════════════════════════════════════════════════════════════
Shader "Skybox/HDRMap"
{
    Properties
    {
        [NoScaleOffset] _HDRMap("HDR Map", 2D) = "grey" {}
        [Enum(Equirectangular, 0, Octahedral, 1, EqualAreaOctahedral, 2, Hemisphere, 3)]
        _MapProjection("Map Projection", Float) = 0
        [Enum(Panorama, 0, Interior, 1)] _MapCoverage("Map Coverage", Float) = 0
        _Exposure("Exposure", Range(0, 8)) = 1
        _Rotation("Rotation (Degrees)", Range(0, 360)) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Special/HLSL/HDRMapFunction.hlsl"

    TEXTURE2D(_HDRMap);
    SAMPLER(sampler_HDRMap);
    CBUFFER_START(UnityPerMaterial)
        float _MapProjection;
        float _MapCoverage;
        float _Exposure;
        float _Rotation;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 direction : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        #if UNITY_REVERSED_Z
            output.positionCS.z = 0.0;
        #else
            output.positionCS.z = output.positionCS.w;
        #endif
        output.direction = input.positionOS.xyz;
        return output;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float sine, cosine;
        sincos(radians(_Rotation), sine, cosine);
        float3 direction = float3(cosine * input.direction.x + sine * input.direction.z,
            input.direction.y, -sine * input.direction.x + cosine * input.direction.z);
        float4 sampled = HDRMapSample(TEXTURE2D_ARGS(_HDRMap, sampler_HDRMap),
            direction, (int)round(_MapProjection), (int)round(_MapCoverage));
        return float4(sampled.rgb * _Exposure, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "HDRMapSkybox"
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
