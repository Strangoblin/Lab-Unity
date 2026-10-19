// ════════════════════════════════════════════════════════════
//  ScreenWarp — 单次RGBA采样；逻辑UV变形与物理RT采样区域分开。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/ScreenWarp"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Special/HLSL/TRS.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/ScreenWarp/ScreenWarpFunction.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _WarpParameters;
        float4 _WarpCenter;
        float4 _WarpBorderColor;
        float4 _WarpSourceSize;
        float _WarpDebug;
    CBUFFER_END

    // ════════════════════════════════════════════════════════
    //  Frag_ScreenWarp — 采样边界限制在有效视口，保留源图Alpha。
    // ════════════════════════════════════════════════════════
    float4 Frag_ScreenWarp(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = DYNAMIC_SCALING_REMOVE_SCALEBIAS(input.texcoord);
        float aspect = _WarpSourceSize.x / _WarpSourceSize.y;
        float2 sourceUV = ScreenWarp_SourceUV(uv, aspect, _WarpParameters.x,
            _WarpParameters.y, _WarpCenter.xy, _WarpParameters.z);
        float inside = ScreenWarp_Inside(sourceUV);

        if (_WarpDebug > 1.5)
            return float4(1.0 - inside, inside, 0.0, 1.0);
        if (_WarpDebug > 0.5)
            return float4(saturate(sourceUV), inside, 1.0);

        float2 halfTexel = _WarpSourceSize.zw * 0.5;
        float2 clampedUV = clamp(sourceUV, halfTexel, 1.0 - halfTexel);
        float2 sampleUV = DYNAMIC_SCALING_APPLY_SCALEBIAS(clampedUV);
        float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV);
        return lerp(color, _WarpBorderColor, _WarpParameters.w * (1.0 - inside));
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
            Name "ScreenWarp"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag_ScreenWarp
            ENDHLSL
        }
    }
}
