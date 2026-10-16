// ════════════════════════════════════════════════════════════
//  Snowy — 标准 UV 内的单面片随机移动、旋转与缩放实验
// ════════════════════════════════════════════════════════════
Shader "PostProcess/Snowy"
{
    Properties
    {
        [NoScaleOffset] _ParticleTex ("Particle RGBA", 2D) = "white" {}
        _Speed ("Speed", Range(0, 10)) = 1
        _Randomness ("Randomness", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Special/HLSL/TRS.hlsl"

    TEXTURE2D(_ParticleTex);

    CBUFFER_START(UnityPerMaterial)
        float _Speed;
        float _Randomness;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════
    //  SnowyRandom — 每个时间节点生成位置、角度与尺寸随机数
    // ════════════════════════════════════════════════════════════
    float4 SnowyRandom(float index)
    {
        return frac(sin((index + 1.0) * float4(127.1, 311.7, 74.7, 269.5))
                    * 43758.5453);
    }

    // ════════════════════════════════════════════════════════════
    //  SnowyParticleUV — 平滑插值随机姿态，再逆变换到面片 UV
    // ════════════════════════════════════════════════════════════
    float2 SnowyParticleUV(float2 uv)
    {
        float time = _Time.y * _Speed;
        float blend = smoothstep(0.0, 1.0, frac(time));
        float4 random = lerp(SnowyRandom(floor(time)), SnowyRandom(floor(time) + 1.0), blend);
        float amount = saturate(_Randomness);
        float size = 0.1 * lerp(1.0, lerp(0.5, 1.5, random.w), amount);
        float2 center = 0.5 + (random.xy - 0.5) * (0.78 * amount);
        float angle = (random.z * 2.0 - 1.0) * TWO_PI * amount;
        float2 pivot = float2(0.5, 0.5);
        return TRS2D_InverseTransformPoint(
            uv, center - pivot, angle, float2(size, size), pivot);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Snowy — 面片 RGBA 覆盖场景，范围外保留原图
    // ════════════════════════════════════════════════════════════
    half4 Frag_Snowy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 particleUV = SnowyParticleUV(input.texcoord);
        float2 inside = step(0.0, particleUV) * step(particleUV, 1.0);
        half4 particle = SAMPLE_TEXTURE2D(_ParticleTex, sampler_LinearClamp, saturate(particleUV));
        half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        half alpha = saturate(particle.a * inside.x * inside.y);
        return half4(lerp(scene.rgb, particle.rgb, alpha), scene.a);
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
