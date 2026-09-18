// ════════════════════════════════════════════════════════════
//  InteriorMapping — Box / Hemisphere 单房间室内映射
//  一张 2D InteriorMap 提供完整室内投射内容。
// ════════════════════════════════════════════════════════════
Shader "Render/InteriorMapping"
{
    Properties
    {
        [MainTexture] _InteriorMap("Single Room Interior Map", 2D) = "white" {}
        [Header(Room)]
        _WindowSize("Window Size (Local XY)", Vector) = (1, 1, 0, 0)
        _RoomSize("Room Size (XY / Depth)", Vector) = (2, 2, 3, 0)
        _RoomTint("Room Tint", Color) = (0.25, 0.35, 0.55, 1)
        _FallbackColor("Fallback Color", Color) = (0.0375, 0.0525, 0.0825, 1)
        _RoomBrightness("Room Brightness", Range(0, 8)) = 1.5
        [Enum(Box, 0, Hemisphere, 1)]
        _ProjectionType("Projection Type", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Shaders/Render/InteriorMapping/InteriorMappingFunction.hlsl"

    struct InteriorMappingAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
    };

    struct InteriorMappingVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
    };

    InteriorMappingVaryings Vert(InteriorMappingAttributes input)
    {
        InteriorMappingVaryings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        return output;
    }

    half4 Frag(InteriorMappingVaryings input) : SV_Target
    {
        int projectionType = (int)round(clamp(_ProjectionType, 0.0, 1.0));
        float hitMask;
        float3 color = InteriorMappingRenderSurface(
            input.uv,
            input.positionWS,
            GetCameraPositionWS(),
            input.normalWS,
            ddx(input.positionWS),
            ddy(input.positionWS),
            ddx(input.uv),
            ddy(input.uv),
            projectionType,
            hitMask);
        return half4(color, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "InteriorMapping"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
