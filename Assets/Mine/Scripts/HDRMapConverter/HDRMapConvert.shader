Shader "Hidden/Mine/HDRMapConvert"
{
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Mine/Special/HLSL/HDRMapFunction.hlsl"

        TEXTURE2D(_Source2D);
        SAMPLER(sampler_Source2D);
        TEXTURECUBE(_SourceCube);
        SAMPLER(sampler_SourceCube);
        int _SourceIsCube;
        int _SourceProjection;
        int _TargetProjection;
        int _Coverage;

        struct Attributes { uint vertexID : SV_VertexID; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        float4 Frag(Varyings input) : SV_Target
        {
            float targetValid;
            float3 direction = HDRMapUVToDirection(input.uv, _TargetProjection, targetValid);
            targetValid *= HDRMapCoverageMask(direction, _Coverage);
            if (targetValid < 0.5)
                return float4(0.0, 0.0, 0.0, 0.0);
            if (_SourceIsCube != 0)
                return SAMPLE_TEXTURECUBE_LOD(_SourceCube, sampler_SourceCube, direction, 0.0);
            return HDRMapSampleLOD(TEXTURE2D_ARGS(_Source2D, sampler_Source2D), direction, _SourceProjection, _Coverage, 0.0);
        }
        float4 CopyFace(Varyings input) : SV_Target
        {
            return SAMPLE_TEXTURE2D_LOD(_Source2D, sampler_Source2D,
                float2(input.uv.x, 1.0 - input.uv.y), 0.0);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "HDRMapConvert"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "CopyCaptureFace"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment CopyFace
            ENDHLSL
        }
    }
    Fallback Off
}
