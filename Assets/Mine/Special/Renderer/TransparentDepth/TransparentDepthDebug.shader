// ════════════════════════════════════════════════════════════════
//  Transparent Depth Debug — grayscale view of the merged depth copy.
// ════════════════════════════════════════════════════════════════
Shader "Hidden/Mine/TransparentDepthDebug"
{
    Properties
    {
        [HideInInspector] _DebugFarPlane ("Far Plane", Float) = 100
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _DebugFarPlane;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════
    //  Depth — linear eye depth remapped to near-white / far-black.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Depth(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float rawDepth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float value = saturate(1.0 - eyeDepth / max(_DebugFarPlane, 1e-4));
        return float4(value, value, value, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DEBUG DEPTH"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Depth
            ENDHLSL
        }
    }
}
