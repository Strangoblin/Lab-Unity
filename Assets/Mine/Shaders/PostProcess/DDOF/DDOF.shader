Shader "PostProcess/DDOF"// Dynamic Depth of Field
{
    Properties
    {
        _FocusRange ("Focus Range", Range(0,50)) = 5.0
        _BlurScale ("Blur Scale", Range(0,5)) = 2.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    float _FocusRange;
    float _BlurScale;

    TEXTURE2D_X(_DDOFTempMainTex);
    TEXTURE2D_X(_DDOFCoCTex);

    float3 ComputeDepth(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float l01Depth = Linear01Depth(rawDepth, _ZBufferParams);
        return float3(abs(eyeDepth), l01Depth, rawDepth);
    }

    float4 Frag_CoC(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float  centerDepth = ComputeDepth(float2(0.5, 0.5)).x;
        float  pixelDepth  = ComputeDepth(uv).x;
        float  l01Depth    = ComputeDepth(uv).y;
        float  baseMask = saturate((abs(pixelDepth - centerDepth) - _FocusRange) / _FocusRange);
        float  distMask = (1 - l01Depth) + 1;
        return float4(baseMask , distMask, 0, 1.0);
    }

    float4 Frag_BlurHorizontal(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float  coc = SAMPLE_TEXTURE2D_X(_DDOFCoCTex, sampler_LinearClamp, uv).g;
        float  blurScale = coc * _BlurScale;
        float4 color = BlurHorizontal(uv, texelSize, blurScale, _BlitTexture, sampler_LinearClamp);
        return color;
    }

    float4 Frag_BlurVertical(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float  coc = SAMPLE_TEXTURE2D_X(_DDOFCoCTex, sampler_LinearClamp, uv).g;
        float  blurScale = coc * _BlurScale;
        float4 color = BlurVertical(uv, texelSize, blurScale, _BlitTexture, sampler_LinearClamp);
        return color;
    }

    half4 Frag_DDOF(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;

        half4 main = SAMPLE_TEXTURE2D_X(_DDOFTempMainTex, sampler_LinearClamp, uv);
        half  mask = SAMPLE_TEXTURE2D_X(_DDOFCoCTex, sampler_LinearClamp, uv).r;
        half4 blur = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        return lerp(main, blur, mask);
    }
    
    ENDHLSL

    SubShader
    {
        Tags { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry"
        }
        LOD 100

        Pass
        {
            Name "DDOF_CoC"
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_CoC
            ENDHLSL
        }

        Pass
        {
            Name "DDOF_BlurH"
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "DDOF_BlurV"
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "DDOF_Main"
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_DDOF
            ENDHLSL
        }
    }
}
