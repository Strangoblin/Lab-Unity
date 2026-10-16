// ═══════════════════════════════════════════════════════════════
//  TraceCopy — same-frame native-resolution Diffuse Trace export.
// ═══════════════════════════════════════════════════════════════
Shader "PostProcess/TraceCopy"
{
    Properties { }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    TEXTURE2D_X(_GITraceTexture);
    float4 Frag_Copy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return SAMPLE_TEXTURE2D_X_LOD(_GITraceTexture, sampler_PointClamp, input.texcoord, 0);
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Copy"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Copy
            ENDHLSL
        }
    }
    Fallback Off
}
