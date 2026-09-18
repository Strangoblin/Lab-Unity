// ════════════════════════════════════════════════════════════
//  InteriorMappingScreenDebug.shader — 2D 平面全屏验证
// ════════════════════════════════════════════════════════════
//  使用与 InteriorMapping 物体 Shader 共用的求交、展开和房间合成函数。
//  DebugFeature 只负责全屏输出；相机位置和房间参数由本 Shader 自己持有。

Shader "PostProcess/InteriorMappingScreenDebug"
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
        [Header(Screen Debug)]
        [Enum(Box, 0, Hemisphere, 1)]
        _ProjectionType("Projection Type", Float) = 0
        _DebugCameraPosition("Camera Position (Local)", Vector) = (0, 0, 2.14, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Shaders/Render/InteriorMapping/InteriorMappingFunction.hlsl"

    CBUFFER_START(InteriorMappingScreenDebugParameters)
        float4 _DebugCameraPosition;
    CBUFFER_END

    half4 Frag(Varyings input) : SV_Target
    {
        int projectionType = (int)round(clamp(_ProjectionType, 0.0, 1.0));
        float hitMask;
        float3 color = InteriorMappingRender(
            input.texcoord,
            _DebugCameraPosition.xyz,
            projectionType,
            hitMask);
        return half4(color, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "InteriorMappingScreenDebug"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
