// ════════════════════════════════════════════════════════════════
//  Mask RBR Mask — redraw object silhouettes against sampled camera depth.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/MaskRBRMask"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/MaskFunction.hlsl"

    struct MaskRBRAttributes
    {
        float4 positionOS : POSITION;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct MaskRBRVaryings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Object mask vertex — use the standard object transform for each eye.
    // ════════════════════════════════════════════════════════════
    MaskRBRVaryings Vert_Mask(MaskRBRAttributes input)
    {
        MaskRBRVaryings output = (MaskRBRVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Object mask fragment — reject geometry hidden by the camera depth.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Mask(MaskRBRVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
        float sceneDepth = SampleSceneDepth(screenUV);
        float visible = Mask_VisibleRawDepth(input.positionCS.z, sceneDepth, 0.0000001);
        return float4(visible, visible, visible, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        BlendOp Max
        Blend One One

        Pass
        {
            Name "ObjectMask"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert_Mask
            #pragma fragment Frag_Mask
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "TransparentObjectMask"
            Cull Off
            ColorMask G
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert_Mask
            #pragma fragment Frag_Mask
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "TransparentSkyOccluders"
            Cull Off
            ColorMask B
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert_Mask
            #pragma fragment Frag_Mask
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
