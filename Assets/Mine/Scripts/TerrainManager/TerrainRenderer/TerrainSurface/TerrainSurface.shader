// ════════════════════════════════════════════════════════════
//  TerrainSurface — 采样 TerrainMapData 烘焙的区域与道路颜色图
// ════════════════════════════════════════════════════════════
Shader "TerrainSurface/TerrainSurface"
{
    Properties
    {
        [MainTexture] _ColorMap ("Terrain Color Map", 2D) = "white" {}
        [MainColor] _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_ColorMap);
    SAMPLER(sampler_ColorMap);

    CBUFFER_START(UnityPerMaterial)
        half4 _Tint;
    CBUFFER_END

    struct TerrainAttributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct TerrainVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 使用地表生成器写入的全图 UV
    // ════════════════════════════════════════════════════════════
    TerrainVaryings Vert(TerrainAttributes input)
    {
        TerrainVaryings output = (TerrainVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 地表颜色直接来自区域与道路的统一数据图
    // ════════════════════════════════════════════════════════════
    half4 Frag(TerrainVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, input.uv) * _Tint;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
