Shader "Picker/OutlineComposite"
{
    // ════════════════════════════════════════════════════════════
    //  OutlineComposite — 全屏后处理：Mask 四邻采样描边 + 合成
    //
    //  _BlitTexture     = Camera Color（Blitter 自动绑定）
    //  _OutlineMaskTex  = Mask RT（C# 侧手动 SetGlobalTexture）
    // ════════════════════════════════════════════════════════════

    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        _OutlineWidth ("Outline Width", Range(1, 5)) = 2
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // ── Mask RT（C# 通过 cmd.SetGlobalTexture 绑定） ──
    TEXTURE2D_X(_OutlineMaskTex);
    sampler   sampler_OutlineMaskTex;

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        float4 _OutlineColor;
        float  _OutlineWidth;
        float2 _OutlineMaskTex_TexelSize;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════
    //  Frag — 四邻采样描边
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 ts = _OutlineMaskTex_TexelSize * _OutlineWidth;

        // 采样 Mask（0=背景, 1=物体）
        float center = SAMPLE_TEXTURE2D_X(_OutlineMaskTex, sampler_OutlineMaskTex, uv).r;
        float up     = SAMPLE_TEXTURE2D_X(_OutlineMaskTex, sampler_OutlineMaskTex, uv + float2(0,  ts.y)).r;
        float down   = SAMPLE_TEXTURE2D_X(_OutlineMaskTex, sampler_OutlineMaskTex, uv + float2(0, -ts.y)).r;
        float right  = SAMPLE_TEXTURE2D_X(_OutlineMaskTex, sampler_OutlineMaskTex, uv + float2( ts.x, 0)).r;
        float left   = SAMPLE_TEXTURE2D_X(_OutlineMaskTex, sampler_OutlineMaskTex, uv + float2(-ts.x, 0)).r;

        // Camera Color 由 Blitter 绑定到 _BlitTexture
        half4 cameraColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

        // 物体内部 → 原色
        if (center > 0.5)
            return cameraColor;

        // 物体边缘（四邻任一在物体内）→ 描边色
        float edge = up + down + left + right;
        if (edge > 0.0)
            return _OutlineColor;

        // 背景 → 原色
        return cameraColor;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OUTLINE_COMPOSITE"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
