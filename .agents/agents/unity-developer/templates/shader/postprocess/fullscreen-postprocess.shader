// ═══════════════════════════════════════════════════════════════
//  Unity 6 URP 全屏后处理 Shader 模板
//
//  基于 Unity 官方 Blit.hlsl + Core.hlsl 模式
//  Package 参考:
//    com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl
//    com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl
//
//  使用: 复制此文件 → 替换 ⚠️ 标记 → 创建 Material → Feature 引用
// ═══════════════════════════════════════════════════════════════

Shader "PostProcess/⚠️YourEffectName"
{
    Properties
    {
        // ⚠️ 替换为你的参数 — 名称以 _ 开头，与 CBUFFER 一致
        _Intensity ("Intensity", Range(0, 1)) = 0.5
        // _Color ("Tint", Color) = (1,1,1,1)
        // _MainTex ("Texture", 2D) = "white" {}
    }

    HLSLINCLUDE
    // ═══ 必装 include（顺序固定）═══
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    // ⚠️ 自定义 include 放这里

    // ═══ Blit.hlsl 已提供的内容（不需要重复声明）═══
    // struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
    // struct Varyings { float4 positionCS : SV_POSITION; float2 texcoord : TEXCOORD0; };
    // Varyings Vert(Attributes input);
    // TEXTURE2D_X(_BlitTexture);

    // ═══ CBUFFER — 材质参数（对应 Properties 块）═══
    CBUFFER_START(UnityPerMaterial)
        float _Intensity;
        // ⚠️ float4 _Color;
    CBUFFER_END

    // ═══ 全局参数（C# 端 Shader.SetGlobalXXX 设置）═══
    // float _CustomGlobalParam;

    // ═══ 采样器 — Blit.hlsl 通常不需要额外声明 ═══
    // Unity 6 中 Blitter.BlitTexture 自动绑定 sampler_LinearClamp

    // ═══ Pass 0: 主效果 ═══
    half4 Frag_⚠️YourEffect(Varyings input) : SV_Target
    {
        // ⚠️ Metal 注意：_BlitTexture + sampler_LinearClamp 是 Blit.hlsl 提供的
        float2 uv = input.texcoord;
        half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

        // ⚠️ 替换为你的效果逻辑
        color.rgb *= _Intensity;

        return color;
    }

    // ⚠️ 多 pass 模式：每个 pass 一个独立的 fragment 函数
    // half4 Frag_Pass1(Varyings input) : SV_Target { ... }
    // half4 Frag_Pass2(Varyings input) : SV_Target { ... }

    ENDHLSL

    SubShader
    {
        // ═══ 全屏后处理标准 Tags ═══
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ═══ 全屏后处理标准状态 ═══
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "⚠️YourEffect" // ⚠️ 替换 pass 名
            // ⚠️ 不需要 LightMode tag — Blitter.BlitTexture 不使用它

            HLSLPROGRAM
            #pragma target 2.0 // ⚠️ Metal 必须
            #pragma vertex Vert    // Blit.hlsl 提供
            #pragma fragment Frag_⚠️YourEffect
            ENDHLSL
        }

        // ⚠️ 多 pass 模板：
        // Pass { Name "⚠️Pass2" HLSLPROGRAM #pragma vertex Vert #pragma fragment Frag_Pass1 ENDHLSL }
        // Pass { Name "⚠️Pass3" HLSLPROGRAM #pragma vertex Vert #pragma fragment Frag_Pass2 ENDHLSL }
    }

    // ⚠️ Unity 6 URP fallback
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
