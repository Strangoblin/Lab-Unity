// ════════════════════════════════════════════════════════════
//  Rainy — 屏幕雨滴、水雾与雨天气氛，共享 RainTex 噪声
// ════════════════════════════════════════════════════════════
Shader "PostProcess/Rainy"
{
    Properties
    {
        [NoScaleOffset] _RainTex ("Rain Tex", 2D) = "white" {}
        _Randomness ("Randomness", Range(0, 1)) = 0.5
        _Coldness ("Coldness", Range(0, 1)) = 0.3
        _Foggy ("Foggy", Range(0, 1)) = 0.5
        _Wind ("Wind", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/TRS.hlsl"

    TEXTURE2D_X(_RainTex);

    CBUFFER_START(UnityPerMaterial)
        float _Randomness;
        float _Coldness;
        float _Foggy;
        float _Wind;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════
    //  RainyRandom — 与 Snowy 相同的周期随机数
    // ════════════════════════════════════════════════════════════
    float4 RainyRandom(float index)
    {
        return frac(sin((index + 1.0) * float4(127.1, 311.7, 74.7, 269.5))
                    * 43758.5453);
    }

    // ════════════════════════════════════════════════════════════
    //  RainyComposite10 — 沿用 Snowy 的单粒子十次合成权重
    // ════════════════════════════════════════════════════════════
    float RainyComposite10(float particle)
    {
        float remain  = 1.0 - particle;
        float remain2 = remain * remain;
        float remain4 = remain2 * remain2;
        float remain8 = remain4 * remain4;
        return 1.0 - remain8 * remain2;
    }

    // ════════════════════════════════════════════════════════════
    //  RainyDepth — 将 RainTex 高度与 Snowy SDF 合成凹陷深度
    // ════════════════════════════════════════════════════════════
    float RainyDepth(float2 localUV, float2 textureOffset, float alpha)
    {
        float height = SAMPLE_TEXTURE2D_X(
            _RainTex, sampler_LinearRepeat, localUV + textureOffset).r - 0.5;
        float sdf = _Foggy - length(localUV + height * 0.5 - 0.5) * 2.0;
        float particle = saturate(sdf) * alpha * _Foggy;
        return RainyComposite10(particle);
    }

    // ════════════════════════════════════════════════════════════
    //  RainyParticle — 每格一个 Snowy 式周期粒子，高度坡度产生内凹折射
    // ════════════════════════════════════════════════════════════
    float2 RainyParticle(float2 uv)
    {
        float2 tileCount = float2(4.0, 4.0);
        float2 tile = floor(uv * tileCount);
        float2 tileUV = frac(uv * tileCount);
        float phase = frac(dot(tile, float2(0.75487766, 0.56984029)));
        float time = _Time.y * _Wind * 2.0 + phase;
        float index = floor(time);
        float cycle = frac(time);

        float4 random = (RainyRandom(index + tile.x * 17.0 + tile.y * 101.0)
            * 2.0 - 1.0) * _Randomness;
        float2 pivot = float2(0.5, 0.5);
        float2 translation = random.xy * 0.25;
        float angle = random.x;
        float scale = (random.x * 0.25 + 0.75) * 0.35;
        float alpha = smoothstep(1.0, 0.3, cycle);
        float aspect = _ScreenParams.x / _ScreenParams.y;
        float tileAspect = aspect * tileCount.y / tileCount.x;
        float2 localUV = TRS2D_InverseTransformUV(
            tileUV, translation, angle, scale, pivot, tileAspect);

        if (alpha <= 0.0 || any(localUV < 0.0) || any(localUV > 1.0))
            return 0.0;

        float2 textureOffset = random.xy;
        float center = RainyDepth(localUV, textureOffset, alpha);
        if (center <= 0.0)
            return 0.0;

        float2 delta = float2(0.02, 0.0);
        float right = RainyDepth(localUV + delta, textureOffset, alpha);
        float left = RainyDepth(localUV - delta, textureOffset, alpha);
        delta = float2(0.0, 0.02);
        float up = RainyDepth(localUV + delta, textureOffset, alpha);
        float down = RainyDepth(localUV - delta, textureOffset, alpha);
        float2 slope = float2(right - left, up - down) / 0.04;
        float2 rotatedSlope = TRS2D_RotateIsotropic(slope, angle);
        return rotatedSlope / (1.0 + length(rotatedSlope)) * 0.015 * center;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Rainy — 以凹陷高度场的梯度直接偏移屏幕 UV
    // ════════════════════════════════════════════════════════════
    half4 Frag_Rainy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

        float2 offsetUV = RainyParticle(uv);
        float3 color = SAMPLE_TEXTURE2D_X(
            _BlitTexture, sampler_LinearClamp, saturate(uv + offsetUV)).rgb;

        return half4(color, scene.a);
    }

    // ════════════════════════════════════════════════════════════
    //  RainyBlur — 固定五点十字核，半径随镜头水汽增加
    // ════════════════════════════════════════════════════════════
    float3 RainyBlur(float2 uv, float radius)
    {
        float2 stepUV = _BlitTexture_TexelSize.xy * radius;
        float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.4;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(stepUV.x, 0)).rgb * 0.15;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(stepUV.x, 0)).rgb * 0.15;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, stepUV.y)).rgb * 0.15;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, stepUV.y)).rgb * 0.15;
        return color;
    }

    // ════════════════════════════════════════════════════
    //  Frag_RainAtmosphere — 单噪声水汽、近似高度雾与冷调色
    // ════════════════════════════════════════════════════
    half4 Frag_RainAtmosphere(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float2 noise = SAMPLE_TEXTURE2D_X(_RainTex, sampler_LinearRepeat, uv * 2.0).rg;
        float2 p = uv * 2.0 - 1.0;
        float2 p2 = p * p;
        float mist = smoothstep(0.25, 1.1, dot(p2, p2)) * lerp(0.75, 1.0, noise.r) * _Foggy;
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
        float density = exp2(-max(height, 0.0) * 0.08);
        float fog = (1.0 - exp2(-distance * density * 0.018 * lerp(0.8, 1.2, noise.g)))
            * _Foggy * 0.65;

        float3 fogColor = float3(0.65, 0.72, 0.78);
        float3 color = scene.rgb;
        if (mist > 0.0)
            color = lerp(color, RainyBlur(uv, lerp(2.0, 5.0, mist)), mist * 0.8);
        color = lerp(color, fogColor, 1.0 - (1.0 - fog) * (1.0 - mist * 0.12));

        float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
        float3 cold = lerp(color, luminance.xxx, 0.3) * float3(0.88, 0.96, 1.05);
        color = lerp(color, cold, _Coldness);
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
            Name "Rainy"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Rainy
            ENDHLSL
        }
        Pass
        {
            Name "RainAtmosphere"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_RainAtmosphere
            ENDHLSL
        }
    }
    Fallback Off
}
