// ════════════════════════════════════════════════════════════════
//  DiffuseGI — screen-space diffuse gather, wide spatial reconstruction and resolve.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/SequenceBaseline"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    TEXTURE2D_X(_GITexture);
    TEXTURE2D_X(_GITraceTexture);

    CBUFFER_START(UnityPerMaterial)
        float4 _GITraceParams;
        float4 _GIFilterParams;
        float4 _GISourceSize;
        float4 _GIReceiverAlbedo;
        float _GIIntensity;
        int _GIRayCount;
        int _GIStepCount;
        int _GIDebugMode;
        int _DiffuseGIFrameIndex;
    CBUFFER_END

    #define SCREEN_TRACE_LOAD_DEPTH(pixel) LOAD_TEXTURE2D_X(_CameraDepthTexture, pixel).r
    #define SCREEN_TRACE_LOAD_PYRAMID(pixel, mip) 0.0
    #include "Assets/Mine/Special/HLSL/RayStepFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/ScreenSpaceTraceFunction.hlsl"
    #undef SCREEN_TRACE_LOAD_DEPTH
    #undef SCREEN_TRACE_LOAD_PYRAMID
    #include "Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/RuntimeFixture/SequenceBaselineFunction.hlsl"

    // ════════════════════════════════════════════════════════════
    //  Trace — linear HDR incident-radiance average and separate confidence.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Trace(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return DiffuseGI_Gather(input.texcoord);
    }

    // ════════════════════════════════════════════════════════════
    //  SpatialResolve — geometry-guided radiance reconstruction.
    // ════════════════════════════════════════════════════════════
    float4 Frag_SpatialResolve(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return DiffuseGI_SpatialResolve(input.texcoord);
    }

    // ════════════════════════════════════════════════════════════
    //  Resolve — guide low-resolution radiance into the camera resolution.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Resolve(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return DiffuseGI_Resolve(input.texcoord);
    }

    // ════════════════════════════════════════════════════════════
    //  Composite — apply receiver albedo once, retain source camera alpha.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Composite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 indirect = SAMPLE_TEXTURE2D_X_LOD(_GITexture, sampler_PointClamp, uv, 0);
        if (_GIDebugMode == 1)
            return float4(SAMPLE_TEXTURE2D_X_LOD(_GITraceTexture, sampler_PointClamp, uv, 0).rgb * _GIReceiverAlbedo.rgb, 1.0);
        if (_GIDebugMode == 2)
            return float4(indirect.rgb * _GIReceiverAlbedo.rgb, 1.0);
        if (_GIDebugMode == 3)
            return float4(indirect.aaa, 1.0);
        float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
        return float4(scene.rgb + indirect.rgb * _GIReceiverAlbedo.rgb * _GIIntensity, scene.a);
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag_Trace
            #pragma multi_compile_local_fragment _ SSGI_TRACE_RAY
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "SpatialResolve"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_SpatialResolve
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
    }
    Fallback Off
}

