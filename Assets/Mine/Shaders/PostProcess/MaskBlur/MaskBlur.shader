// ════════════════════════════════════════════════════════════════
//  Mask Blur — diffuse the current scene colors selected by a material mask.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/MaskBlur"
{
    Properties
    {
        [HideInInspector] _BlurRadius ("Blur Radius", Float) = 0.5
        [HideInInspector] _Intensity ("Intensity", Float) = 0.5
        [HideInInspector] _DebugMode ("Debug Mode", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D_X(_MaskBlurMaskTexture);
    TEXTURE2D_X(_MaskBlurTexture);

    CBUFFER_START(UnityPerMaterial)
        float _BlurRadius;
        float _Intensity;
        float _DebugMode;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════
    //  Gaussian — fixed five-sample kernel using source texture texels.
    // ════════════════════════════════════════════════════════════
    half4 MaskBlurGaussian(float2 uv, float2 direction)
    {
        float2 offset = direction * _BlitTexture_TexelSize.xy * (_BlurRadius * 2.0);
        half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset) * 0.1216216;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset * 0.5) * 0.2332432;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.290918;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset * 0.5) * 0.2332432;
        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset) * 0.1216216;
        return color / 1.0006476;
    }

    // ════════════════════════════════════════════════════════════
    //  Extract — retain selected HDR scene colors with mask coverage in alpha.
    // ════════════════════════════════════════════════════════════
    half4 Frag_Extract(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
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
        return MaskBlurGaussian(input.texcoord, float2(1.0, 0.0));
    }

    half4 Frag_Vertical(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return MaskBlurGaussian(input.texcoord, float2(0.0, 1.0));
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Horizontal
            ENDHLSL
        }

        Pass
        {
            Name "Vertical"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Vertical
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
