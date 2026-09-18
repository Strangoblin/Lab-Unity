// ════════════════════════════════════════════════════════════
//  DebugOutput.shader — 通用屏幕颜色可视化
// ════════════════════════════════════════════════════════════
//  输入来自真实 URP RenderGraph 的 _BlitTexture。
//  所有显示参数均由 Shader / Material 持有，RendererFeature 不复制参数。

Shader "PostProcess/DebugOutput"
{
    Properties
    {
        [Enum(Color, 0, Red, 1, Green, 2, Blue, 3, Alpha, 4, Luminance, 5, UV, 6)]
        _DebugChannel ("Debug Channel", Float) = 0
        [Enum(None, 0, Log2, 1, Inverse, 2)]
        _DisplayTransform ("Display Transform", Float) = 0
        _Exposure ("Exposure", Range(-8, 8)) = 0
        _Contrast ("Contrast", Range(0, 4)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1
        [Toggle] _Invert ("Invert", Float) = 0
        [Toggle] _FlipY ("Flip Y", Float) = 0
        [Toggle] _CheckerBackground ("Checker Background", Float) = 0
        _CheckerColorA ("Checker Color A", Color) = (0.08, 0.08, 0.08, 1)
        _CheckerColorB ("Checker Color B", Color) = (0.16, 0.16, 0.16, 1)
        _CheckerScale ("Checker Scale", Range(1, 128)) = 16
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _DebugChannel;
        float _DisplayTransform;
        float _Exposure;
        float _Contrast;
        float _Saturation;
        float _Invert;
        float _FlipY;
        float _CheckerBackground;
        float4 _CheckerColorA;
        float4 _CheckerColorB;
        float _CheckerScale;
    CBUFFER_END

    float3 ApplyTransform(float3 color)
    {
        if (_DisplayTransform > 0.5 && _DisplayTransform < 1.5)
            color = log2(1.0 + max(color, 0.0));
        else if (_DisplayTransform >= 1.5)
            color = 1.0 - saturate(color);

        color *= exp2(_Exposure);
        color = (color - 0.5) * _Contrast + 0.5;

        float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
        color = lerp(luminance.xxx, color, _Saturation);

        if (_Invert > 0.5)
            color = 1.0 - color;

        return color;
    }

    float3 SampleChecker(float2 uv)
    {
        float2 cell = floor(uv * _CheckerScale);
        float parity = fmod(cell.x + cell.y, 2.0);
        return lerp(_CheckerColorA.rgb, _CheckerColorB.rgb, parity);
    }

    half4 Frag(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 sampleUV = uv;
        if (_FlipY > 0.5)
            sampleUV.y = 1.0 - sampleUV.y;

        float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV);
        float3 color = source.rgb;

        if (_DebugChannel > 0.5 && _DebugChannel < 1.5)
            color = source.rrr;
        else if (_DebugChannel >= 1.5 && _DebugChannel < 2.5)
            color = source.ggg;
        else if (_DebugChannel >= 2.5 && _DebugChannel < 3.5)
            color = source.bbb;
        else if (_DebugChannel >= 3.5 && _DebugChannel < 4.5)
            color = source.aaa;
        else if (_DebugChannel >= 4.5 && _DebugChannel < 5.5)
            color = dot(source.rgb, float3(0.2126, 0.7152, 0.0722)).xxx;
        else if (_DebugChannel >= 5.5)
            color = float3(uv, 0.0);

        color = ApplyTransform(color);

        if (_CheckerBackground > 0.5)
        {
            float3 checker = SampleChecker(uv);
            color = lerp(checker, color, saturate(source.a));
        }

        return half4(color, source.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "DebugOutput"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
