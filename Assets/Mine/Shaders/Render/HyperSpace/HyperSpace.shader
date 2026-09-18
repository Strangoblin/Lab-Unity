// ════════════════════════════════════════════════════════════
//  HyperSpace — Shadertoy 原始移植
//  Created by Benoit Marini, 2020
//  License: CC BY-NC-SA 3.0
// ════════════════════════════════════════════════════════════

Shader "Render/HyperSpace"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Shaders/Render/HyperSpace/HyperSpaceFunction.hlsl"

    #define HYPERSPACE_NUM_LAYERS 16.0

    struct HyperSpaceAttributes
    {
        float4 positionOS : POSITION;
    };

    struct HyperSpaceVaryings
    {
        float4 positionCS : SV_POSITION;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 普通材质网格顶点变换；片元阶段使用裁剪空间坐标还原屏幕像素
    // ════════════════════════════════════════════════════════════
    HyperSpaceVaryings Vert(HyperSpaceAttributes input)
    {
        HyperSpaceVaryings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 逐层叠加原始 HyperSpace 公式
    // ════════════════════════════════════════════════════════════
    half4 Frag(HyperSpaceVaryings input) : SV_Target
    {
        float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
        float2 fragCoord = screenUV * _ScreenParams.xy;
        float2 uv = (fragCoord - _ScreenParams.xy * 0.5) / _ScreenParams.y;
        float time = _Time.y * 0.3;
        float3 col = 0.0;

        // 原文条件为 i <= 1.0，因此 NUM_LAYERS=16 实际采样 17 层。
        [unroll]
        for (int layer = 0; layer <= 16; layer++)
        {
            float i = (float)layer / HYPERSPACE_NUM_LAYERS;
            float d = frac(i + time);
            float s = lerp(5.0, 0.5, d);
            float f = d * smoothstep(1.0, 0.9, d);
            col += HyperSpaceTex(float3(uv * s, i * 4.0), time).xyz * f;
        }

        col /= HYPERSPACE_NUM_LAYERS;
        col *= float3(2.0, 1.0, 2.0);
        col = pow(col, float3(0.5, 0.5, 0.5));

        return half4(col, 0.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        // ════════════════════════════════════════════════════════════
        //  Pass 0: HyperSpace_Main — 原始 16 层迭代叠加
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "HyperSpace_Main"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
