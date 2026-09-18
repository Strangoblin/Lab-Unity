// ════════════════════════════════════════════════════════════
//  Snowy — 屏幕空间六层雪粒子，深度 1 / 2 / 3 各两层
// ════════════════════════════════════════════════════════════
Shader "PostProcess/Snowy"
{
    Properties
    {
        [NoScaleOffset] _SnowTex ("Snow Tex", 2D) = "white" {}
        _Randomness ("Randomness", Range(0, 1)) = 0.5
        _Coldness ("Coldness", Range(0, 1)) = 0.3
        _Frost ("Frost", Range(0, 1)) = 0.5
        _Wind ("Wind", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/TRS.hlsl"

    TEXTURE2D(_SnowTex);

    CBUFFER_START(UnityPerMaterial)
        float _Randomness;
        float _Coldness;
        float _Frost;
        float _Wind;
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
    //  SnowyParticleUV — 每层独立周期与随机姿态，共享等比 UV 逆变换
    // ════════════════════════════════════════════════════════════
    float2 SnowyParticleUV(float2 uv, float layer, float depth, out float visibility)
    {
        float time = _Time.y * _Wind + (layer + 0.5) / 6.0;
        float index = floor(time) + layer * 127.0;
        float cycle = frac(time);

        float4 random = (SnowyRandom(index) * 2.0 - 1.0) * _Randomness;
        float2 pivot = float2(0.5, 0.5);
        float2 translation = float2(random.x, random.y + 0.5 - cycle)
                           + sin(cycle * TWO_PI + random.x) * 0.1 * (1 - _Wind);
        float angle = (cycle + random.x) * PI;
        float scale = (depth + random.x * 0.5) * (cycle + 0.5) * 0.01;
        float aspect = _ScreenParams.x / _ScreenParams.y;
        visibility = (cos(time + index) * 0.5 + 0.5) * smoothstep(1.0, 0.8, cycle);

        return TRS2D_InverseTransformUV(
            uv, translation, angle, scale, pivot, aspect);
    }

    float SnowParticle(float2 uv)
    {
        float snow = SAMPLE_TEXTURE2D(_SnowTex, sampler_LinearClamp, uv).r;
        float sdf  = 1.0 - length(uv - 0.5) * 2.0;
        return sdf;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Snowy — 六层依次 Alpha 合成，场景透明度保持不变
    // ════════════════════════════════════════════════════════════
    half4 Frag_Snowy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        float3 color = scene.rgb;

        [unroll]
        for (int layer = 0; layer < 10; layer++)
        {
            float depth = 1.0 + floor(layer * 0.25);
            float visibility;
            float2 particleUV = SnowyParticleUV(input.texcoord, layer, depth, visibility);
            float  particle = SnowParticle(particleUV);
            return particle;
            float2 inside = step(0.0, particleUV) * step(particleUV, 1.0);
            float  alpha = saturate(particle * inside.x * inside.y * visibility);
            color = lerp(color, particle, alpha);
        }

        return half4(color, scene.a);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_SnowAtmosphere — 冷调色、深度雾、流动雪幕与静态霜边
    // ════════════════════════════════════════════════════════════
    half4 Frag_SnowAtmosphere(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float wind = _Wind * _Time.y;
        float4 scene    = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 atmo1    = SAMPLE_TEXTURE2D_X(_SnowTex, sampler_LinearClamp, frac(uv * 7));
        float4 atmo2    = SAMPLE_TEXTURE2D_X(_SnowTex, sampler_LinearClamp, frac(uv * 5));
        float4 atmo3    = SAMPLE_TEXTURE2D_X(_SnowTex, sampler_LinearClamp, frac(float2(uv.x * 0.5 - wind , uv.y)));

        float  edge     = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
        float  frost    = 1.0 - smoothstep(0.0, (0.1 + atmo1.x * atmo2.x) * _Frost, edge);
        scene = lerp(scene, 1, frost * _Frost);

        float rawDepth = SampleSceneDepth(uv);
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float atmos    = (1.0 - exp2(- eyeDepth / (100 * (1 + atmo3.x - _Wind)))) * _Frost;
        scene = lerp(scene, 1, atmos);

        float luminance = dot(scene.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 cold     = lerp(scene.rgb, luminance.xxx, 0.3) * float3(0.88, 0.96, 1.05);
        float3 color    = lerp(scene.rgb, cold, _Coldness);

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
        Pass
        {
            Name "SnowAtmosphere"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SnowAtmosphere
            ENDHLSL
        }
    }
    Fallback Off
}
