Shader "InteractionManager/InteractionDebug"
{
    // ═══════════════════════════════════════════════════════════════
    //  测试可视化 Shader — 在平面/网格上显示 _InteractionResultTex。
    //  用 _InteractionOrthoV / _InteractionOrthoP 将世界坐标映射到 RT UV。
    // ═══════════════════════════════════════════════════════════════

    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float4 _BaseColor;

    TEXTURE2D(_InteractionWaterTex);
    SAMPLER(sampler_InteractionWaterTex);
    TEXTURE2D(_InteractionOriginTex);
    SAMPLER(sampler_InteractionOriginTex);

    float4x4 _InteractionOrthoV;
    float4x4 _InteractionOrthoP;
    float3   _InteractionAreaPos;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionHCS : SV_POSITION;
        float2 uv          : TEXCOORD0;
        float3 worldPos    : TEXCOORD1;
    };

    Varyings Vert(Attributes i)
    {
        Varyings o;
        float3 worldPos = TransformObjectToWorld(i.positionOS);
        o.positionHCS   = TransformWorldToHClip(worldPos);
        o.uv            = i.uv;
        o.worldPos      = worldPos;
        return o;
    }

    half4 Frag(Varyings i) : SV_Target
    {
        // 片元中直接采样 interaction RT
        float4 clipPos = mul(_InteractionOrthoP, mul(_InteractionOrthoV, float4(i.worldPos, 1.0)));
        float2 rtUV = clipPos.xy / clipPos.w * 0.5 + 0.5;
        float interaction = SAMPLE_TEXTURE2D_LOD(_InteractionWaterTex, sampler_InteractionWaterTex, rtUV, 0).r;

        // 双向热力图: 负=蓝(凹陷), 零=黑(平衡), 正=红(波峰)
        half3 trough    = half3(0.0, 0.0, 1.0);   // -1 → 蓝
        half3 eq        = half3(0.0, 0.0, 0.0);   //  0 → 黑
        half3 crest     = half3(1.0, 0.0, 0.0);   // +1 → 红
        half  a         = abs(interaction);
        half3 color     = interaction < 0.0
            ? lerp(eq, trough, saturate(a))        // 负 → 蓝
            : lerp(eq, crest,  saturate(a));       // 正 → 红

        return half4(color * _BaseColor.rgb, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalRenderPipeline"
        }
        LOD 100

        // ════════════════════════════════════════════════════════════
        //  Pass 0 — 深度预写入 (DepthOnly)
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            DepthVaryings DepthVert(Attributes i)
            {
                DepthVaryings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                return o;
            }

            half4 DepthFrag(DepthVaryings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  Pass 1 — 可视化调试 (UniversalForward)
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "Debug"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
