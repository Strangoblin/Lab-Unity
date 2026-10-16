// ════════════════════════════════════════════════════════════
//  Sandy — 镜头沙粒击打、暖色高度雾与镜头积尘，共享 SandTex 噪声
// ════════════════════════════════════════════════════════════
Shader "PostProcess/Sandy"
{
    Properties
    {
        [NoScaleOffset] _SandTex ("Sand Tex", 2D) = "white" {}
        _Randomness ("Randomness", Range(0, 1)) = 0.5
        _Warmth ("Warmth", Range(0, 1)) = 0.3
        _Dusty ("Dusty", Range(0, 1)) = 0.5
        _Wind ("Wind", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/TRS.hlsl"

    TEXTURE2D_X(_SandTex);

    CBUFFER_START(UnityPerMaterial)
        float _Randomness;
        float _Warmth;
        float _Dusty;
        float _Wind;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════
    //  SandyRandom — 与 Snowy 相同的周期随机数
    // ════════════════════════════════════════════════════════════
    float4 SandyRandom(float index)
    {
        return frac(sin((index + 1.0) * float4(127.1, 311.7, 74.7, 269.5))
                    * 43758.5453);
    }

    // ════════════════════════════════════════════════════
    //  SandyParticle — 单层固定网格的随机击打，风控制生成周期
    // ════════════════════════════════════════════════════
    float SandyParticle(float2 uv)
    {
        float2 tileCount = float2(40.0, 24.0);
        float2 tile = floor(uv * tileCount);
        float2 tileUV = frac(uv * tileCount);
        float phase = frac(dot(tile, float2(0.75487766, 0.56984029)));
        float time = _Time.y * _Wind * 2.0 + phase;
        float cycle = frac(time);
        float4 random = (SandyRandom(floor(time) + tile.x * 17.0 + tile.y * 101.0)
            * 2.0 - 1.0) * _Randomness;
        float scale = (random.z * 0.25 + 0.75) * 0.22;
        float alpha = smoothstep(0.0, 0.12, cycle) * (1.0 - smoothstep(0.55, 1.0, cycle));
        float aspect = _ScreenParams.x / _ScreenParams.y * tileCount.y / tileCount.x;
        float2 localUV = TRS2D_InverseTransformUV(
            tileUV, random.xy * 0.25, random.x * PI, scale, float2(0.5, 0.5), aspect);
        float noise = SAMPLE_TEXTURE2D_X(_SandTex, sampler_LinearRepeat, localUV + random.xy).r;
        float shape = 1.0 - smoothstep(0.0, 0.5, length(localUV - 0.5) * 2.0);
        return shape * lerp(0.35, 1.0, noise) * alpha * _Dusty;
    }

    // ════════════════════════════════════════════════════
    //  Frag_Sandy — 单层沙粒，中央弱四角强
    // ════════════════════════════════════════════════════
    half4 Frag_Sandy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float2 p = uv * 2.0 - 1.0;
        float coverage = lerp(0.5, 1.0, smoothstep(0.2, 1.8, dot(p, p)));
        float particle = SandyParticle(uv);
        return half4(lerp(scene.rgb, float3(0.62, 0.43, 0.23), particle * 0.8), scene.a);
    }

    // ════════════════════════════════════════════════════
    //  Frag_SandAtmosphere — 单噪声积尘、近似高度雾与暖调色
    // ════════════════════════════════════════════════════
    half4 Frag_SandAtmosphere(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float2 noise = SAMPLE_TEXTURE2D_X(_SandTex, sampler_LinearRepeat, uv * 3.0).rg;
        float2 p = uv * 2.0 - 1.0;
        float2 p2 = p * p;
        float deposit = smoothstep(0.3, 1.15, dot(p2, p2)) * lerp(0.35, 1.0, noise.r) * _Dusty;
        float rawDepth = SampleSceneDepth(uv);
        #if !UNITY_REVERSED_Z
            rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
        #endif
        float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        float3 ray = worldPos - _WorldSpaceCameraPos;
        float3 forward = -UNITY_MATRIX_V[2].xyz;
        ray = lerp(ray, forward * max(dot(ray, forward), 0.0), unity_OrthoParams.w);
        float rayLength = max(length(ray), 0.0001);
        float distance = min(rayLength, 250.0);
        float height = worldPos.y - ray.y + ray.y * (distance / rayLength) * 0.5;
        float density = exp2(-max(height, 0.0) * 0.12);
        float fog = (1.0 - exp2(-distance * density * 0.025 * lerp(0.8, 1.2, noise.g)))
            * _Dusty * 0.75;

        float3 color = lerp(scene.rgb, float3(0.66, 0.49, 0.30), fog);
        color = lerp(color, float3(0.46, 0.31, 0.16), deposit * 0.55);
        float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
        float3 warm = lerp(color, luminance.xxx, 0.3) * float3(1.08, 0.96, 0.82);
        return half4(lerp(color, warm, _Warmth), scene.a);
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
            Name "Sandy"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Sandy
            ENDHLSL
        }
        Pass
        {
            Name "SandAtmosphere"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SandAtmosphere
            ENDHLSL
        }
    }
    Fallback Off
}
