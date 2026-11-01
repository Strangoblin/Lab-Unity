// ════════════════════════════════════════════════════════════
//  FabricLutPacker — 预积分 KM 双流薄层的 R / T / R∞
//  U = sqrt(K/S / MaxK)  V = sqrt(S*d / MaxSd)，两端采样点，运行时需映射到像素中心。
// ════════════════════════════════════════════════════════════

Shader "FabricLutBaker/FabricLutPacker"
{
    Properties
    {
        _LutResolution ("LUT Resolution", Vector) = (128,128,0,0)
        _MaxAbsorption ("Max K/S", Float) = 16
        _MaxOpticalDepth ("Max S*d", Float) = 8
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Special/HLSL/FabricFunction.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _LutResolution;
        float  _MaxAbsorption;
        float  _MaxOpticalDepth;
    CBUFFER_END

    struct FabricBakeAttributes
    {
        uint vertexID : SV_VertexID;
    };

    struct FabricBakeVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert / Frag — 全屏三角形与 (K/S, S*d) 参数域
    // ════════════════════════════════════════════════════════════

    FabricBakeVaryings Vert(FabricBakeAttributes input)
    {
        FabricBakeVaryings output = (FabricBakeVaryings)0;
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    float4 Frag(FabricBakeVaryings input) : SV_Target
    {
        float2 grid = saturate((input.uv * _LutResolution.xy - 0.5) / max(_LutResolution.xy - 1.0, 1.0));
        float absorption = _MaxAbsorption * grid.x * grid.x;
        float opticalDepth = _MaxOpticalDepth * grid.y * grid.y;

        real R, T;
        KM_Slab(absorption, 1.0, opticalDepth, R, T);
        real infinity = KM_ReflectanceInfinite(absorption, 1.0);
        return float4(R, T, infinity, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Bake"
            Cull Off
            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            ENDHLSL
        }
    }
}
