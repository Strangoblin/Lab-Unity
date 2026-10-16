// ════════════════════════════════════════════════════════════════
//  SSGITemporal — shared history reprojection for AO, diffuse and specular.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/SSGITemporal"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    TEXTURE2D_X(_SSGIHistoryColor);
    TEXTURE2D_X(_SSGIHistoryDepth);
    TEXTURE2D_X(_SSGIMotionTexture);

    CBUFFER_START(UnityPerMaterial)
        float _SSGIHistoryValid;
        float _SSGITemporalBlend;
        float4x4 _SSGIPreviousViewMatrix;
    CBUFFER_END

    #include "Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/SSGITemporal.hlsl"

    float4 Frag_Resolve(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return SSGITemporal_Resolve(input.texcoord);
    }

    float4 Frag_Depth(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float rawDepth = SST_SampleDepth(input.texcoord);
        return SST_IsSurface(rawDepth) ? SST_EyeDepth(rawDepth).xxxx : 0.0;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "TemporalResolve"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Resolve
            ENDHLSL
        }

        Pass
        {
            Name "HistoryDepth"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Depth
            ENDHLSL
        }
    }
    Fallback Off
}
