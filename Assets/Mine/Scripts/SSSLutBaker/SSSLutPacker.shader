// ════════════════════════════════════════════════════════════
//  SSSLutPacker — 对环形表面的 Lambert 响应做 RGB 高斯扩散预积分。
//  RGB 已含入射余弦，A=1；纹理坐标采用两端采样点，运行时需映射到像素中心。
// ════════════════════════════════════════════════════════════

Shader "SSSLutBaker/SSSLutPacker"
{
    Properties
    {
        _SampleCount ("Sample Count", Float) = 256
        _LutResolution ("LUT Resolution", Vector) = (128,128,0,0)
        _ScatterProfile ("Scatter Profile", Vector) = (1,0.35,0.2,2)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Special/HLSL/SSSFunction.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _SampleCount;
        float4 _LutResolution;
        float4 _ScatterProfile;
    CBUFFER_END

    struct SSSBakeAttributes
    {
        uint vertexID : SV_VertexID;
    };

    struct SSSBakeVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert / Frag — 全屏三角形与 LUT 参数域
    // ════════════════════════════════════════════════════════════

    SSSBakeVaryings Vert(SSSBakeAttributes input)
    {
        SSSBakeVaryings output = (SSSBakeVaryings)0;
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    float4 Frag(SSSBakeVaryings input) : SV_Target
    {
        float2 grid = saturate((input.uv * _LutResolution.xy - 0.5) / (_LutResolution.xy - 1.0));
        return float4(SSS_Integrate(grid.x * 2.0 - 1.0, grid.y * _ScatterProfile.w,
            _ScatterProfile.rgb, (uint)_SampleCount), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SSSLutPack"
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
