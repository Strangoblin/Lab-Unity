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
        return rotatedSlope / (1.0 + length(rotatedSlope)) * 0.015;
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
    //  RainyBlur — 固定五点核模拟湿镜头的小半径散射
    // ════════════════════════════════════════════════════════════
    float3 RainyBlur(float2 uv)
    {
        float2 stepUV = _BlitTexture_TexelSize.xy * 2.0;
        float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.4;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(stepUV.x, 0)).rgb * 0.15;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(stepUV.x, 0)).rgb * 0.15;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, stepUV.y)).rgb * 0.15;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, stepUV.y)).rgb * 0.15;
        return color;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_RainAtmosphere — 冷调色、深度高度雾、流动雨幕与屏幕水雾
    // ════════════════════════════════════════════════════════════
    half4 Frag_RainAtmosphere(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene    = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 atmo1    = SAMPLE_TEXTURE2D_X(_RainTex, sampler_LinearRepeat, uv * 7);
        float4 atmo2    = SAMPLE_TEXTURE2D_X(_RainTex, sampler_LinearRepeat, uv * 5);
        float4 atmo3    = SAMPLE_TEXTURE2D_X(_RainTex, sampler_LinearRepeat, float2(uv.x * 0.5 - _Wind * _Time.y , uv.y));

        float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
        float mist = 1.0 - smoothstep(0.0, (0.1 + atmo1.x * atmo2.x) * _Foggy, edge);
        scene.rgb = lerp(scene.rgb, RainyBlur(uv), _Foggy * (0.15 + 0.85 * mist));

        float rawDepth = SampleSceneDepth(uv);
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float heightWeight = 0.0;
        if (Linear01Depth(rawDepth, _ZBufferParams) < 0.9999)
        {
            float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            float midpointHeight = (_WorldSpaceCameraPos.y + worldPos.y) * 0.5;
            heightWeight = exp2(-max(midpointHeight, 0.0) * 0.12);
        }
        float atmos = (1.0 - exp2(-eyeDepth / (100 * (1 + atmo3.x - _Wind)))) * _Foggy * heightWeight;
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
