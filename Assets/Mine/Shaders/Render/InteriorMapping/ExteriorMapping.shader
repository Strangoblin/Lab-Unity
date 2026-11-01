// ════════════════════════════════════════════════════════════
//  ExteriorMapping — solid cube impostor rendered on a quad
//  Entry-face ray cast, the inverse of InteriorMapping's room cast.
// ════════════════════════════════════════════════════════════
Shader "Render/ExteriorMapping"
{
    Properties
    {
        [Header(Cube)]
        [Enum(Fixed, 0, Face Camera, 1)]
        _BillboardMode("Billboard Mode", Float) = 1
        [Enum(Quad Relative, 0, Object Transform, 1)]
        _CubeSpace("Cube Space", Float) = 1
        _CubeSize("Cube Size (Quad Fraction)", Range(0.05, 2)) = 0.6
        _CubeRotation("Cube Rotation (Euler XYZ)", Vector) = (25, 45, 0, 0)
        _CubeOffset("Cube Offset (Cube Space)", Vector) = (0, 0, 0, 0)

        [Header(Faces)]
        [MainTexture] [NoScaleOffset] _FaceFront("Front (+Z)", 2D) = "white" {}
        [NoScaleOffset] _FaceBack("Back (-Z)", 2D) = "white" {}
        [NoScaleOffset] _FaceLeft("Left (-X)", 2D) = "white" {}
        [NoScaleOffset] _FaceRight("Right (+X)", 2D) = "white" {}
        [NoScaleOffset] _FaceTop("Top (+Y)", 2D) = "white" {}
        [NoScaleOffset] _FaceBottom("Bottom (-Y)", 2D) = "white" {}

        [Header(Shading)]
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _LightDirection("Fake Light Direction (Cube Space)", Vector) = (0.4, 0.8, 0.5, 0)
        _Ambient("Ambient", Range(0, 1)) = 0.35
        _ShadingStrength("Shading Strength", Range(0, 1)) = 0.65
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Shaders/Render/InteriorMapping/ExteriorMappingFunction.hlsl"

    struct ExteriorMappingAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct ExteriorMappingVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — billboard or fixed quad position and normal
    // ════════════════════════════════════════════════════════════
    ExteriorMappingVaryings Vert(ExteriorMappingAttributes input)
    {
        ExteriorMappingVaryings output = (ExteriorMappingVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 positionWS;
        float3 normalWS;
        if (_BillboardMode > 0.5)
        {
            positionWS = ExteriorMappingBillboardPositionWS(input.positionOS.xyz);
            normalWS = ExteriorMappingBillboardNormalWS();
        }
        else
        {
            positionWS = TransformObjectToWorld(input.positionOS.xyz);
            normalWS = TransformObjectToWorldNormal(input.normalOS);
        }

        output.positionWS = positionWS;
        output.normalWS = normalWS;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.uv = input.uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — camera ray into cube space, sample the hit face
    // ════════════════════════════════════════════════════════════
    half4 Frag(ExteriorMappingVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float alpha;
        float3 color = ExteriorMappingRenderSurface(
            input.uv,
            input.positionWS,
            GetCameraPositionWS(),
            input.normalWS,
            ddx(input.positionWS),
            ddy(input.positionWS),
            ddx(input.uv),
            ddy(input.uv),
            alpha);
        return half4(color, alpha);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }
        LOD 100

        Pass
        {
            Name "ExteriorMapping"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

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
