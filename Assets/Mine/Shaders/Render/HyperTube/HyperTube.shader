// ════════════════════════════════════════════════════════════
//  HyperTube — Shadertoy 原始移植
//  Shader by Frostbyte
//  License: CC BY-NC-SA 4.0
// ════════════════════════════════════════════════════════════

Shader "Render/HyperTube"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Shaders/Render/HyperTube/HyperTubeFunction.hlsl"

    struct HyperTubeAttributes
    {
        float4 positionOS : POSITION;
    };

    struct HyperTubeVaryings
    {
        float4 positionCS : SV_POSITION;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 普通材质网格顶点变换
    // ════════════════════════════════════════════════════════════
    HyperTubeVaryings Vert(HyperTubeAttributes input)
    {
        HyperTubeVaryings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 用屏幕坐标重建原始 Shadertoy 射线
    // ════════════════════════════════════════════════════════════
    half4 Frag(HyperTubeVaryings input) : SV_Target
    {
        float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
        float2 resolution = _ScreenParams.xy;
        float2 fragCoord = screenUV * resolution;
        float3 color = HyperTubeRender(fragCoord, resolution, _Time.y);
        return half4(color, 0.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        // ════════════════════════════════════════════════════════════
        //  Pass 0: HyperTube_Main — 十步低采样体积光
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "HyperTube_Main"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
