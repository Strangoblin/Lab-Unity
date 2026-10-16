// ════════════════════════════════════════════════════════════════
//  Mask Blur — diffuse the current scene colors selected by a material mask.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/MaskBlur"
{
    Properties
    {
        [HideInInspector] _BlurParameters ("Radius, Mode, Jitter, Seed", Vector) = (0.5, 0, 1, 0)
        [HideInInspector] _BlurOutputSize ("Output Size", Vector) = (1920, 1080, 0, 0)
        [HideInInspector] _ExtractDownsample ("Extract Downsample", Float) = 0
        [HideInInspector] _Intensity ("Intensity", Float) = 0.5
        [HideInInspector] _DebugMode ("Debug Mode", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D_X(_MaskBlurMaskTexture);
    TEXTURE2D_X(_MaskBlurTexture);

    CBUFFER_START(UnityPerMaterial)
        float4 _BlurParameters;
        float4 _BlurOutputSize;
        float _ExtractDownsample;
        float _Intensity;
        float _DebugMode;
    CBUFFER_END

    #include "Assets/Mine/Special/HLSL/HashFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    // ════════════════════════════════════════════════════════════
    //  Gaussian — shared fixed or spatially jittered separable kernel.
    // ════════════════════════════════════════════════════════════
    half4 MaskBlurGaussian(Varyings input, float2 direction, float passSeed)
    {
        float2 uvStep = Blur_GetUVStep(_BlurParameters.x, _BlurOutputSize.xy, (int)_BlurParameters.y);
        #if defined(_MASKBLUR_JITTERED)
        #if defined(_MASKBLUR_QUALITY_HIGH)
        return Blur_Gaussian9Jittered(input.texcoord, uvStep, direction,
            input.texcoord * _BlurOutputSize.xy, _BlurParameters.z, _BlurParameters.w + passSeed,
            TEXTURE2D_X_ARGS(_BlitTexture, sampler_LinearClamp));
        #elif defined(_MASKBLUR_QUALITY_MEDIUM)
        return Blur_Gaussian9Jittered(input.texcoord, uvStep, direction,
            input.texcoord * _BlurOutputSize.xy, _BlurParameters.z, _BlurParameters.w + passSeed,
            TEXTURE2D_X_ARGS(_BlitTexture, sampler_LinearClamp));
        #else
        return Blur_Gaussian5Jittered(input.texcoord, uvStep, direction,
            input.texcoord * _BlurOutputSize.xy, _BlurParameters.z, _BlurParameters.w + passSeed,
            TEXTURE2D_X_ARGS(_BlitTexture, sampler_LinearClamp));
        #endif
        #else
        return Blur_Gaussian5(input.texcoord, uvStep, direction,
            TEXTURE2D_X_ARGS(_BlitTexture, sampler_LinearClamp));
        #endif
    }

    // ════════════════════════════════════════════════════════════
    //  Extract — retain selected HDR scene colors with mask coverage in alpha.
    // ════════════════════════════════════════════════════════════
    float4 MaskBlurExtractPoint(float2 uv)
    {
        float mask = SAMPLE_TEXTURE2D_X(_MaskBlurMaskTexture, sampler_PointClamp, uv).r;
        float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
        return float4(scene * mask, mask);
    }

    half4 Frag_Extract(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        UNITY_BRANCH
        if (_ExtractDownsample > 0.5)
        {
            float2 offset = abs(_BlitTexture_TexelSize.xy) * 0.5;
            float4 selected = MaskBlurExtractPoint(input.texcoord + float2(-offset.x, -offset.y));
            selected += MaskBlurExtractPoint(input.texcoord + float2(offset.x, -offset.y));
            selected += MaskBlurExtractPoint(input.texcoord + float2(-offset.x, offset.y));
            selected += MaskBlurExtractPoint(input.texcoord + offset);
            return selected * 0.25;
        }
        half mask = SAMPLE_TEXTURE2D_X(_MaskBlurMaskTexture, sampler_LinearClamp, input.texcoord).r;
        half3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
        return half4(scene * mask, mask);
    }

    // ════════════════════════════════════════════════════════════
    //  Horizontal / vertical — diffuse color and mask through the same kernel.
    // ════════════════════════════════════════════════════════════
    half4 Frag_Horizontal(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return MaskBlurGaussian(input, float2(1.0, 0.0), 0.0);
    }

    half4 Frag_Vertical(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return MaskBlurGaussian(input, float2(0.0, 1.0), 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Composite — add premultiplied diffuse light and preserve camera alpha.
    // ════════════════════════════════════════════════════════════
    half4 Frag_Composite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        half4 blur = SAMPLE_TEXTURE2D_X(_MaskBlurTexture, sampler_LinearClamp, input.texcoord);
        if (_DebugMode > 0.5 && _DebugMode < 1.5)
        {
            half mask = SAMPLE_TEXTURE2D_X(_MaskBlurMaskTexture, sampler_LinearClamp, input.texcoord).r;
            return half4(mask, mask, mask, scene.a);
        }
        if (_DebugMode > 1.5)
            return half4(blur.rgb, scene.a);
        return half4(scene.rgb + blur.rgb * _Intensity, scene.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "Extract"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Extract
            ENDHLSL
        }

        Pass
        {
            Name "Horizontal"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Horizontal
            #pragma multi_compile_local_fragment _ _MASKBLUR_JITTERED
            #pragma multi_compile_local_fragment _ _MASKBLUR_QUALITY_MEDIUM _MASKBLUR_QUALITY_HIGH
            ENDHLSL
        }

        Pass
        {
            Name "Vertical"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Vertical
            #pragma multi_compile_local_fragment _ _MASKBLUR_JITTERED
            #pragma multi_compile_local_fragment _ _MASKBLUR_QUALITY_MEDIUM _MASKBLUR_QUALITY_HIGH
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            ENDHLSL
        }
    }
}
