// ═══════════════════════════════════════════════════════════════
//  Adapter — actual camera depth, frozen SST baseline and trace diagnostics.
// ═══════════════════════════════════════════════════════════════
Shader "PostProcess/Adapter"
{
    Properties { }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    TEXTURE2D_X(_GITraceTexture);
    float4 _TestOrigin;
    float4 _TestDirection;
    float4 _TestParams;
    int _TestMode;
    int _TestInjectFault;
    #define SCREEN_TRACE_LOAD_DEPTH(pixel) LOAD_TEXTURE2D_X(_CameraDepthTexture, pixel).r
    #define SCREEN_TRACE_LOAD_PYRAMID(pixel, mip) 0.0
    #include "Assets/Mine/Special/HLSL/RayStepFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/ScreenSpaceTraceFunction.hlsl"
    #undef SCREEN_TRACE_LOAD_DEPTH
    #undef SCREEN_TRACE_LOAD_PYRAMID
    #include "Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl"
    #undef SCREEN_SPACE_TRACE_INCLUDED
    #define ScreenSpaceTraceHit LegacyHit
    #define SST_SampleDepth Legacy_SampleDepth
    #define SST_IsSurface Legacy_IsSurface
    #define SST_EyeDepth Legacy_EyeDepth
    #define SST_WorldPosition Legacy_WorldPosition
    #define SST_Project Legacy_Project
    #define SST_Trace Legacy_Trace
    #include "Assets/Mine/Shaders/PostProcess/SSGI/Validation~/BaselineScreenSpaceTrace.hlsl"
    #undef ScreenSpaceTraceHit
    #undef SST_SampleDepth
    #undef SST_IsSurface
    #undef SST_EyeDepth
    #undef SST_WorldPosition
    #undef SST_Project
    #undef SST_Trace

    float4 Frag_Check(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        if (_TestMode == 8)
            return SAMPLE_TEXTURE2D_X_LOD(_GITraceTexture, sampler_PointClamp, input.texcoord, 0);
        ScreenTraceContext context = SST_CreateTraceContext();
        if (_TestMode == 2)
            return float4(context.depthSize, context.depthTextureSize);
        if (_TestMode == 3)
            return float4(SST_WorldPosition(input.texcoord, SST_SampleDepth(input.texcoord)), 1.0);
        if (_TestMode == 5)
        {
            float2 uv;
            float eyeDepth;
            bool valid = SST_Project(_TestOrigin.xyz, uv, eyeDepth);
            return float4(uv, eyeDepth, valid ? 1.0 : 0.0);
        }
        if (_TestMode == 6)
            return float4(_RTHandleScale.xy, _CameraDepthTexture_TexelSize.xy);
        if (_TestMode == 7)
        {
            float raw = SST_SampleDepth(input.texcoord);
            return float4(SST_EyeDepth(raw), raw, SST_IsSurface(raw) ? 1.0 : 0.0, 1.0);
        }
        if (_TestMode == 1)
        {
            LegacyHit legacy;
            bool found = Legacy_Trace(_TestOrigin.xyz, _TestDirection.xyz,
                _TestParams.x, _TestParams.y, (int)_TestParams.z, legacy);
            return float4(legacy.distance, legacy.uv, found ? 0.0 : 1.0);
        }
        ScreenSpaceTraceHit hit;
        bool found = SST_Trace(_TestOrigin.xyz, _TestDirection.xyz,
            _TestParams.x, _TestParams.y, (int)_TestParams.z, hit);
        if (_TestMode == 4)
            return float4(hit.steps, hit.depthQueries, hit.eyeDepth, (float)hit.status);
        if (found && _TestInjectFault != 0)
            hit.distance += 0.5;
        return float4(hit.distance, hit.uv, (float)hit.status);
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Check"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag_Check
            #pragma multi_compile_local_fragment _ SSGI_TRACE_RAY
            ENDHLSL
        }
    }
    Fallback Off
}
