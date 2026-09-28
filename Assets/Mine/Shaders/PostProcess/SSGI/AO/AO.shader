// ════════════════════════════════════════════════════════════════
//  AO — SSAO/HBAO occlusion, bilateral blur and scene colour multiplication.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/AO"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    TEXTURE2D_X(_AOTexture);

    CBUFFER_START(UnityPerMaterial)
        float4 _AOParams;
        float4 _AOFilterParams;
        float4 _AOSourceSize;
        int _AOMode;
        int _AOSampleCount;
        int _AOStepCount;
        int _AODebugMode;
    CBUFFER_END

    #include "Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/AO/AOFunction.hlsl"

    // ════════════════════════════════════════════════════════════
    //  Trace — SSAO or HBAO occlusion, sky left unoccluded.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Trace(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return AO_Trace(input.texcoord);
    }

    // ════════════════════════════════════════════════════════════
    //  BlurHorizontal — depth/normal guided horizontal smoothing.
    // ════════════════════════════════════════════════════════════
    float4 Frag_BlurHorizontal(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return AO_Blur(input.texcoord, float2(1, 0)).xxxx;
    }

    // ════════════════════════════════════════════════════════════
    //  BlurVertical — depth/normal guided vertical smoothing.
    // ════════════════════════════════════════════════════════════
    float4 Frag_BlurVertical(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return AO_Blur(input.texcoord, float2(0, 1)).xxxx;
    }

    // ════════════════════════════════════════════════════════════
    //  Composite — multiply scene colour by visibility; debug views bypass it.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Composite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float visibility = saturate(AO_Resolve(uv));
        if (_AODebugMode == 1)
            return float4(visibility.xxx, 1.0);
        if (_AODebugMode == 2)
            return float4(Linear01Depth(SST_SampleDepth(uv), _ZBufferParams).xxx, 1.0);
        if (_AODebugMode == 3)
            return float4(AO_SampleNormal(uv) * 0.5 + 0.5, 1.0);
        float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
        return float4(scene.rgb * (1.0 - saturate((1.0 - visibility) * _AOParams.w)), scene.a);
    }
    // ════════════════════════════════════════════════════════════
    //  Resolve — full-resolution visibility for the integrated compositor.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Resolve(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return AO_Resolve(input.texcoord).xxxx;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Trace"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Trace
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "BlurHorizontal"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_BlurHorizontal
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "BlurVertical"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_BlurVertical
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "Resolve"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Resolve
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
