Shader "Render/ShiningCard"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]  _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Grid)]
        [Space]
        _GridCount        ("Grid Count", Float) = 10
        _GapWidth         ("Gap Width", Range(0.0, 0.2)) = 0.02
        _GapSoftness      ("Gap Softness", Range(0.0, 0.3)) = 0.03

        [Header(Flash)]
        [Space]
        _ViewDirStrength  ("View Dir Strength", Range(0.0, 2.0)) = 0.5
        _DiagonalWidth    ("Diagonal Width", Range(0.05, 0.5)) = 0.2

        [Header(Dark Flash)]
        [Space]
        _DarkHueShift     ("Dark Hue Shift", Range(-1.0, 1.0)) = 0.05
        _DarkSaturation   ("Dark Saturation", Range(0.0, 2.0)) = 0.8
        _DarkValue        ("Dark Value", Range(0.0, 2.0)) = 1.0

        [Header(Bright Flash)]
        [Space]
        _BrightHueShift   ("Bright Hue Shift", Range(-1.0, 1.0)) = 0.35
        _BrightSaturation ("Bright Saturation", Range(0.0, 2.0)) = 1.5
        _BrightValue      ("Bright Value", Range(0.0, 2.0)) = 1.3
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/Mine/Special/HLSL/HSV.hlsl"

    // ── 纹理与采样器 ──
    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float  _GridCount;
        float  _GapWidth;
        float  _GapSoftness;
        float  _ViewDirStrength;
        float  _DiagonalWidth;
        float  _DarkHueShift;
        float  _DarkSaturation;
        float  _DarkValue;
        float  _BrightHueShift;
        float  _BrightSaturation;
        float  _BrightValue;
    CBUFFER_END

    // ── 顶点输入 ──
    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float4 tangentOS  : TANGENT;
        float2 uv         : TEXCOORD0;
    };

    // ── 顶点→片元 ──
    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS   : TEXCOORD2;
        float3 tangentWS  : TEXCOORD3;
        float  tangentSign : TEXCOORD4;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 标准位置/法线变换，传递 TBN 数据供 Frag 计算视线
    // ════════════════════════════════════════════════════════════
    Varyings Vert(Attributes input)
    {
        Varyings output;

        VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
        VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

        output.positionCS  = positionInputs.positionCS;
        output.positionWS  = positionInputs.positionWS;
        output.normalWS    = normalInputs.normalWS;
        output.tangentWS   = normalInputs.tangentWS;
        output.tangentSign = input.tangentOS.w * GetOddNegativeScale();
        output.uv          = TRANSFORM_TEX(input.uv, _BaseMap);

        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  GetViewDirTS — 世界空间视线 → 切线空间视线
    // ════════════════════════════════════════════════════════════
    float3 GetViewDirTS(float3 positionWS, float3 normalWS, float3 tangentWS, float tangentSign)
    {
        float3 bitangentWS = cross(normalWS, tangentWS) * tangentSign;
        float3 viewDirWS   = normalize(GetWorldSpaceViewDir(positionWS));
        return float3(
            dot(viewDirWS, tangentWS),
            dot(viewDirWS, bitangentWS),
            dot(viewDirWS, normalWS)
        );
    }

    // ════════════════════════════════════════════════════════════
    //  TriangleGridMask — 静态三角格子遮罩（不随视角移动）
    // ════════════════════════════════════════════════════════════
    float TriangleGridMask(float2 uv, float gridCount, float gapWidth, float gapSoftness)
    {
        float2 p = uv * gridCount;
        const float sqrt3_2 = 0.8660254;

        float p0 =  p.x * sqrt3_2 + p.y * 0.5;
        float p1 =  p.y;
        float p2 = -p.x * sqrt3_2 + p.y * 0.5;

        float f0 = frac(p0);
        float f1 = frac(p1);
        float f2 = frac(p2);

        float d0 = min(f0, 1.0 - f0);
        float d1 = min(f1, 1.0 - f1);
        float d2 = min(f2, 1.0 - f2);

        float minEdge = min(min(d0, d1), d2);
        return smoothstep(gapWidth, gapWidth + gapSoftness, minEdge);
    }

    // ════════════════════════════════════════════════════════════
    //  DiagonalFlash — 对角渐变闪光，shift 控制视角偏移
    // ════════════════════════════════════════════════════════════
    float DiagonalFlash(float2 uv, float2 shift, float width)
    {
        float2 p = uv + shift;
        float d = dot(p, float2(1.2, 1.0));
        float flash = frac(d);
        return smoothstep(0.0, width, flash) * smoothstep(1.0, width, flash);
    }

    // ════════════════════════════════════════════════════════════
    //  ApplyFlashColor — HSV 颜色偏移，受 flashAmount 调制
    // ════════════════════════════════════════════════════════════
    half4 ApplyFlashColor(half4 baseColor, float flashAmount,
        float hueShift, float saturation, float value)
    {
        float3 hsv = RGBtoHSV(baseColor.rgb);
        hsv.x = frac(hsv.x + hueShift * flashAmount);
        hsv.y = hsv.y * lerp(1.0, saturation, flashAmount);
        hsv.z = hsv.z * lerp(1.0, value, flashAmount);
        return half4(HSVtoRGB(hsv), baseColor.a);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 闪卡管线
    //
    //  viewDir → 对角闪光 → 暗/亮两套 HSV 参数插值  ×  三角遮罩
    //  遮罩直接相乘（无阈值），间隙不闪、三角内闪光
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        // ── Layer 1-2: 视线 → 闪光偏移 ──
        float3 normalWS  = normalize(input.normalWS);
        float3 tangentWS = normalize(input.tangentWS);
        float3 viewDirTS = GetViewDirTS(input.positionWS, normalWS, tangentWS, input.tangentSign);
        float2 gridShift = viewDirTS.xy * _ViewDirStrength;

        // ── Layer 3: 对角闪光 + 三角遮罩 ──
        float flashVal = DiagonalFlash(input.uv, gridShift, _DiagonalWidth);
        float triMask  = TriangleGridMask(input.uv, _GridCount, _GapWidth, _GapSoftness);
        float mask     = flashVal * triMask;
        // ── Layer 4: 对角渐变插值暗/亮 HSV 参数 ──
        float hueShift = lerp(_DarkHueShift,   _BrightHueShift,   mask);
        float sat      = lerp(_DarkSaturation, _BrightSaturation, mask);
        float val      = lerp(_DarkValue,      _BrightValue,      mask);

        // ── Layer 5: 遮罩直接调制闪光强度 → HSV 偏移 ──
        half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
        return ApplyFlashColor(baseColor, mask, hueShift, sat, val);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
