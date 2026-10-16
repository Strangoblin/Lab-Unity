// ════════════════════════════════════════════════════════════
//  Weather — 模块化雨滴折射、沙雪粒子与镜头、场景气氛
// ════════════════════════════════════════════════════════════
Shader "PostProcess/Weather"
{
    Properties
    {
        [Header(Technical)]
        [NoScaleOffset] _WeatherNoiseTex ("Weather Noise", 2D) = "white" {}
        _WeatherTargetSize ("Target Size", Vector) = (1920, 1080, 0.0005208333, 0.0009259259)
        _WeatherTime ("Weather Time", Float) = 0
        [Toggle] _LegacySnowAlpha ("Legacy Snow Alpha", Float) = 1

        [Header(Common)]
        _Randomness ("Randomness", Range(0, 1)) = 0.5
        _CycleRate ("Particle Cycle Rate", Range(0, 2)) = 1

        [Header(Particles)]
        _ParticleShape ("Shape Threshold", Range(0, 1)) = 0.5
        _ParticleOpacity ("Particle Opacity", Range(0, 1)) = 0.5
        _RefractionScale ("Rain Refraction Scale", Range(0, 4)) = 1

        [Header(Lens)]
        _LensStrength ("Lens Strength", Range(0, 1)) = 0.5
        _EdgeBaseWidth ("Snow Edge Base Width", Range(0, 1)) = 0.05
        _EdgeNoiseWidth ("Snow Edge Noise Width", Range(0, 1)) = 0.5

        [Header(Fog)]
        _FogStrength ("Fog Strength", Range(0, 1)) = 0.5
        _FogBaseDistance ("Snow Fog Base Distance", Float) = 50
        _FogNoiseDistance ("Snow Fog Noise Distance", Float) = 100
        _FogNoiseScrollX ("Snow Fog Scroll X", Float) = 0.5

        [Header(Tone)]
        _ToneStrength ("Tone Strength", Range(0, 1)) = 0.3
        _ToneTint ("Tone Tint", Vector) = (0.88, 0.96, 1.05, 1)

        [Header(Debug)]
        [Enum(Off, 0, Particles, 1, Refraction, 2, Lens, 3, Fog, 4, Final, 5)]
        _WeatherDebug ("Weather Debug", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/TRS.hlsl"

    TEXTURE2D(_WeatherNoiseTex);

    CBUFFER_START(UnityPerMaterial)
        float _Randomness;
        float _CycleRate;
        float _ParticleShape;
        float _ParticleOpacity;
        float _RefractionScale;
        float _LensStrength;
        float _EdgeBaseWidth;
        float _EdgeNoiseWidth;
        float _FogStrength;
        float _FogBaseDistance;
        float _FogNoiseDistance;
        float _FogNoiseScrollX;
        float _ToneStrength;
        float4 _ToneTint;
        float _LegacySnowAlpha;
        float _WeatherDebug;
        float4 _WeatherTargetSize;
        float _WeatherTime;
    CBUFFER_END

    #include "Assets/Mine/Shaders/PostProcess/Weather/WeatherCommon.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/Weather/WeatherParticles.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/Weather/WeatherAtmosphere.hlsl"

    // ════════════════════════════════════════════════════════════
    //  Frag_WeatherParticles — 当前模式的粒子叠色或雨滴折射
    // ════════════════════════════════════════════════════════════
    half4 Frag_WeatherParticles(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        WeatherScreenContext context = Weather_CreateContext(input.texcoord);
        float4 scene = SAMPLE_TEXTURE2D_X(
            _BlitTexture, sampler_LinearClamp, context.sourceUV);
        return Weather_ApplyParticles(context, scene);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_WeatherAtmosphere — 独立镜头沉积、场景雾与调色
    // ════════════════════════════════════════════════════════════
    half4 Frag_WeatherAtmosphere(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        WeatherScreenContext context = Weather_CreateContext(input.texcoord);
        float4 scene = SAMPLE_TEXTURE2D_X(
            _BlitTexture, sampler_LinearClamp, context.sourceUV);
        return Weather_ApplyAtmosphere(context, scene);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Particles"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_WeatherParticles
            #pragma multi_compile_local WEATHER_RAIN WEATHER_SAND WEATHER_SNOW
            ENDHLSL
        }
        Pass
        {
            Name "Atmosphere"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_WeatherAtmosphere
            #pragma multi_compile_local WEATHER_RAIN WEATHER_SAND WEATHER_SNOW
            ENDHLSL
        }
    }
    Fallback Off
}
