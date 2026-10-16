// ════════════════════════════════════════════════════════════
//  Snowy — UV 网格单层落雪实验，供 URP Full Screen Pass 使用
// ════════════════════════════════════════════════════════════
Shader "Mine/PostProcess/Snowy"
{
    Properties
    {
        [Header(Particle)]
        [NoScaleOffset] _ParticleTex ("Particle RGBA", 2D) = "white" {}
        _ParticleColor ("Particle Color", Color) = (1, 1, 1, 1)
        _ParticleSize ("Particle Width And Height In Cell UV", Range(0, 0.7)) = 0.1
        _Scale ("Scale XY", Vector) = (1, 1, 0, 0)
        _Rotation ("Rotation Degrees CCW", Range(-180, 180)) = 0

        [Header(Layout)]
        [Enum(Single,0,Grid,1)] _Layout ("Layout", Float) = 0
        _Grid ("Grid Columns Rows", Vector) = (10, 10, 0, 0)
        _Offset ("Offset XY In Screen UV", Vector) = (0, 0, 0, 0)

        [Header(Motion)]
        [Toggle] _Animate ("Animate", Float) = 0
        _PreviewTime ("Preview Time Seconds", Float) = 0
        _Velocity ("Velocity XY Screen UV Per Second", Vector) = (0.015, -0.06, 0, 0)
        _RotationSpeed ("Rotation Degrees Per Second", Float) = 25

        [Header(Debug)]
        [Enum(Composite,0,ParticleMask,1,ParticleUV,2)] _DebugView ("Debug View", Float) = 0
        [Toggle] _ShowGrid ("Show Grid", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D(_ParticleTex);
    SAMPLER(sampler_ParticleTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _ParticleColor;
        float4 _Scale;
        float4 _Grid;
        float4 _Offset;
        float4 _Velocity;
        float _ParticleSize;
        float _Rotation;
        float _Layout;
        float _Animate;
        float _PreviewTime;
        float _RotationSpeed;
        float _DebugView;
        float _ShowGrid;
    CBUFFER_END

    #include "SnowyParticle.hlsl"

    // ════════════════════════════════════════════════════════════
    //  Frag_Snowy — 采样粒子 RGBA 并合成场景，支持遮罩和局部 UV 观测
    // ════════════════════════════════════════════════════════════
    half4 Frag_Snowy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float time = _PreviewTime + (_Animate > 0.5 ? _Time.y : 0.0);
        float bounds;
        float2 particleUV = SnowyParticleUV(uv, time, bounds);
        half4 particle = SAMPLE_TEXTURE2D_LOD(_ParticleTex, sampler_LinearClamp, saturate(particleUV), 0);
        half alpha = saturate(particle.a * _ParticleColor.a * bounds);
        half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        half3 color = lerp(scene.rgb, particle.rgb * _ParticleColor.rgb, alpha);
        if (_DebugView > 1.5)
            color = half3(saturate(particleUV), 0.0) * bounds;
        else if (_DebugView > 0.5)
            color = alpha.xxx;
        color = lerp(color, half3(0.1, 0.65, 0.8), SnowyGridLines(uv));
        return half4(color, scene.a);
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
            Name "Snowy"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Snowy
            ENDHLSL
        }
    }
    Fallback Off
}
