// ════════════════════════════════════════════════════════════
//  Snowy — 屏幕雪斑与雪天气氛，共享 SnowTex 噪声
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
    //  SnowyParticle — 周期随机落点，噪声扰动 SDF 并淡出
    // ════════════════════════════════════════════════════════════
    float SnowyParticle(float2 uv)
    {
        float time = _Time.y * _Wind * 2;
        float index = floor(time);
        float cycle = frac(time);

        float4 random = (SnowyRandom(index) * 2.0 - 1.0) * _Randomness;
        float2 pivot = float2(0.5, 0.5);
        float2 translation = random.xy * 0.5;
        float angle = random.x;
        float scale = (random.x * 0.25 + 0.75) * 0.1;
        float alpha  = smoothstep(1.0, 0.3, cycle);
        float aspect = _ScreenParams.x / _ScreenParams.y;
        uv = TRS2D_InverseTransformUV(
            uv, translation, angle, scale, pivot, aspect);

        float snow = SAMPLE_TEXTURE2D(_SnowTex, sampler_LinearRepeat, uv + random).r;
        float sdf = _Frost - length(lerp(uv, snow, 0.5) - 0.5) * 2.0;
        float particle = saturate(sdf) * alpha * _Frost;
        return particle;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Snowy — 同一雪斑重复合成十次，保留输入透明度
    // ════════════════════════════════════════════════════════════
    half4 Frag_Snowy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        float3 color = scene.rgb;

        [unroll]
        for (int i = 0; i < 10; i++)
        {
            float particle = SnowyParticle(uv);
            color = lerp(color, 1, particle);
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
        float4 scene    = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 atmo1    = SAMPLE_TEXTURE2D_X(_SnowTex, sampler_LinearRepeat, uv * 7);
        float4 atmo2    = SAMPLE_TEXTURE2D_X(_SnowTex, sampler_LinearRepeat, uv * 5);
        float4 atmo3    = SAMPLE_TEXTURE2D_X(_SnowTex, sampler_LinearRepeat, float2(uv.x * 0.5 - _Wind * _Time.y , uv.y));

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
