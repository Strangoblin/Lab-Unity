Shader "PostProcess/RimToonScreen"
{
    Properties
    {
        _RimPower ("Rim Power", Range(0,10)) = 2.0
        _BlurScale ("Blur Scale", Range(0,1)) = 0.5
        _BlurIntensity ("Blur Intensity", Range(0,1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/RimLightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    float _RimPower;
    float _BlurScale;
    float _BlurIntensity;

    TEXTURE2D_X(_RTTempMainTex);
    TEXTURE2D_X(_RimToonMaskRT);
    TEXTURE2D_X(_RimToonColorRT);
    TEXTURE2D_X(_RimToonBlurRT);

    float4 Frag_Mask(Varyings input) : SV_Target
    {
        return float4(1.0, 1.0, 1.0, 1.0);
    }

    float4 Frag_ClearMask(Varyings input) : SV_Target
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float4 Frag_Source(Varyings input) : SV_Target
    {
        Light  mainLight  = GetMainLight();
        float3 lightDirWS = mainLight.direction;
        float3 lightColor = mainLight.color.rgb;
        float3 normalWS   = SampleSceneNormals(input.texcoord);
        float  DepthRim   = RimLightDepth(lightDirWS, input.texcoord, _RimPower);
        float  DepthAtten = dot(normalWS, lightDirWS) * 0.5 + 0.5;

        float3 main = SAMPLE_TEXTURE2D_X(_RTTempMainTex, sampler_LinearClamp, input.texcoord).rgb;
        float  mask = SAMPLE_TEXTURE2D_X(_RimToonMaskRT, sampler_LinearClamp, input.texcoord).r;
        float  glow = mask * DepthAtten * DepthRim * 0.5;
        return float4(lightColor * glow + main * mask, mask);
    }

    float4 Frag_BlurH(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float4 color = BlurHorizontal(uv, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
        return color;
    }

    float4 Frag_BlurV(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float4 color = BlurVertical(uv, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
        return color;
    }

    half4 Frag_Mix(Varyings input) : SV_Target
    {
        half4 main = SAMPLE_TEXTURE2D_X(_RTTempMainTex, sampler_LinearClamp, input.texcoord);
        half  mask = SAMPLE_TEXTURE2D_X(_RimToonMaskRT, sampler_LinearClamp, input.texcoord).r;
        half4 glow = SAMPLE_TEXTURE2D_X(_RimToonColorRT, sampler_LinearClamp, input.texcoord);
        half4 blur = SAMPLE_TEXTURE2D_X(_RimToonBlurRT, sampler_LinearClamp, input.texcoord);
        return glow + main * (1.0 - mask) + blur * _BlurIntensity;
    }

    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "RimToonClearMask"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_ClearMask
            ENDHLSL
        }

        Pass
        {
            Name "RimToonMask"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            Stencil
            {
                Ref 1
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Mask
            ENDHLSL
        }

        Pass
        {
            Name "RimToonGlowSource"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Source
            ENDHLSL
        }

        Pass
        {
            Name "RimToonBlurH"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurH
            ENDHLSL
        }

        Pass
        {
            Name "RimToonBlurV"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_BlurV
            ENDHLSL
        }

        Pass
        {
            Name "RimToonComposite"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Mix
            ENDHLSL
        }
    }
}
