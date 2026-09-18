// ════════════════════════════════════════════════════════════
//  TheStarryNight — 星夜全屏后处理：星空（环+扇+星点）+ 山水多层 + 麦田前景
// ════════════════════════════════════════════════════════════
Shader "Render/TheStarryNight"
{
    Properties
    {
        // 全局共享（三类图案同源：密度 / 柔度 / 扰动 / 速度）
        _SectorCount ("Sector Count", Range(0, 2)) = 0.5
        _Softness ("Softness", Range(0.001, 0.5)) = 0.001
        _Random ("Random", Range(0, 1)) = 0.2
        _Speed ("Speed", Range(0, 1)) = 0.5

        // 星空（环 + 扇 + 星点；A/B/C = 星点近→远三段配色）
        _SkyColorA ("Sky Color A", Color) = (1, 1, 1, 1)
        _SkyColorB ("Sky Color B", Color) = (0, 0, 0, 1)
        _SkyColorC ("Sky Color C", Color) = (0, 0, 0, 1)

        // 山水（多层行带；A 亮 B 暗带内渐变，A 的 alpha = 层不透明度）
        _MountColorA ("Mount Color A", Color) = (0.22, 0.28, 0.42, 1)
        _MountColorB ("Mount Color B", Color) = (0.10, 0.25, 0.30, 1)

        // 麦田（前景多层横带；A 亮 B 暗带内渐变，A 的 alpha = 带不透明度）
        _WheatColorA ("Wheat Color A", Color) = (0.66, 0.55, 0.28, 1)
        _WheatColorB ("Wheat Color B", Color) = (0.35, 0.28, 0.15, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    // 命名：三分类前缀 SKY_（星空）/ MOUNT_（山水）/ WHEAT_（麦田）；共享项无前缀。
    // 常量先于 SDF 库 include（线性文本展开，依赖前置）

    // ── 星空（环 + 扇 + 星点）──
    #define SKY_RING_SCALE 250.0      // 环数 = 扇区数 × 值
    #define SKY_POINT_COUNT 4         // 星点数（环心 + 扇心）
    #define SKY_ANGLE_BLEND 0.25      // 扇区间 2NN 角场融合带半宽

    // ── 共享行波（山水行带波；麦田转置调用即竖笔触扭摆）──
    #define ROW_WAVE_AMP 0.05         // 波幅（uv 单位）
    #define ROW_WAVE_FREQ 1.5         // 波频（周期/uv 单位）

    // ── 山水（多层纵向行带；麦田同构，前缀 WHEAT_）──
    #define MOUNT_ROW_SCALE 300.0     // 行带数 = 扇区数 × 值
    #define MOUNT_COL_SCALE 40.0      // 横向格点数 = 扇区数 × 值
    #define MOUNT_COL_STEP 0.02       // 山水横向逐层位移步长
    #define MOUNT_LAYER_COUNT 3       // 层数（i 越大越靠前）

    // ── 麦田（前景多层横带；山水同构，前缀 MOUNT_）──
    #define WHEAT_SCALE 40.0          // 行数 = 扇区数 × 值
    #define WHEAT_COL_SCALE 300.0     // 竖笔触列数 = 扇区数 × 值
    #define WHEAT_LAYER_COUNT 3       // 横带数（i 越大越靠前）
    #define WHEAT_RAG_AMP 0.2         // 顶缘参差幅（行号域：带顶下移 ≤ 幅 × 行数）
    #define WHEAT_RAG_FREQ 16.0       // 参差频（沿扭曲 U 循环数/屏宽）
    // 行纹纵向循环位移步长：逐列错动 frac(列号 × 值) 行，超一行格即回卷 →
    //   位移恒 < 1 行格，砖形错位不整体倾斜；相位进入行门 → 带缘随列漏 ≤1 行（保留质感）
    #define WHEAT_V_STEP 0.2

    // 星点位置（已预置 90° 旋转）
    static const float2 SKY_POINTS[SKY_POINT_COUNT] = {
        float2(0.4, -0.4),
        float2(-0.4, 0.0),
        float2(-0.2, -0.3),
        float2(0.2, -0.1)
    };

    // 逐星半径（缩放径向距离场）
    static const float SKY_POINT_RADII[SKY_POINT_COUNT] = { 0.5, 0.4, 0.6, 0.3 };

    // 山水层裁剪区间（行号域；行带波相位随 MOUNT_ROW_OFFSET 错开）
    static const float2 MOUNT_ROWS[MOUNT_LAYER_COUNT] = {
        float2(0.00, 0.30),  // 层0 远
        float2(0.15, 0.50),  // 层1 中
        float2(0.30, 0.70)   // 层2 近
    };

    // 山水层行波相位偏移
    static const float MOUNT_ROW_OFFSET[MOUNT_LAYER_COUNT] = { 0.0, 1.5, 3.0 };

    // 麦田带裁剪区间（行号域；交叠 → 全域无缝隙，i 越大越靠前）
    static const float2 WHEAT_ROWS[WHEAT_LAYER_COUNT] = {
        float2(0.15, 0.4),  // 带0 远
        float2(0.2, 0.45),  // 带1 中
        float2(0.25, 0.5)   // 带2 近
    };

    // 麦田带相位偏移（与 MOUNT_ROW_OFFSET 同构）：每带列波相位错开（坐标不旋转）
    static const float WHEAT_ROW_OFFSET[WHEAT_LAYER_COUNT] = { 0.0, 2, 4 };

    CBUFFER_START(UnityPerMaterial)
        float _SectorCount;
        float _Softness;
        float _Random;
        float _Speed;
        float4 _SkyColorA;
        float4 _SkyColorB;
        float4 _SkyColorC;
        float4 _MountColorA;
        float4 _MountColorB;
        float4 _WheatColorA;
        float4 _WheatColorB;
    CBUFFER_END

    #include "Assets/Mine/Shaders/Render/VanGogh/TheStarryNightSDF.hlsl"

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 texcoord : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord : TEXCOORD0;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.texcoord = input.texcoord;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Main — 星空基底，叠加山水多层与麦田前景（遮罩 × Color Alpha）
    // ════════════════════════════════════════════════════════════
    half4 Frag_Main(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord - 0.5;
        float time = _Time.y * _Speed;   // 统一动画速度（三类图案同源）

        // 星空基底：环（径向格）+ 扇（周向格）
        float ringCount = _SectorCount * SKY_RING_SCALE;
        float2 rings = ComputeCell(ComputeRadialSDF(uv), ringCount, 1.0, _Softness);
        float random = Hash(rings.y) * _Random;
        float angle = frac(ComputeAngularSDF(uv) + random + time * (1 + random) * 0.1);
        float2 sectors = ComputeCell(angle, _SectorCount, rings.y, _Softness);
        float3 skyColor = ComputeSkyColor(rings.y, sectors.y, ringCount)
                        * (rings.x * sectors.x * 0.5 + 0.5);

        // 山水层叠：逐层行带裁剪 + 行波相位错开
        float rowCount = max(1.0, round(_SectorCount * MOUNT_ROW_SCALE));

        float3 result = skyColor;
        for (int i = 0; i < MOUNT_LAYER_COUNT; i++)
        {
            float2 rows = MOUNT_ROWS[i];
            float start = rows.x * rowCount;
            float end = rows.y * rowCount;
            float2 rowCell = ComputeCell(
                ComputeLongitudinalSDF(uv, time, MOUNT_ROW_OFFSET[i]),
                rowCount, 1.0, _Softness);
            if (rowCell.y >= start && rowCell.y < end)
            {
                float2 colCell = ComputeCell(
                    ComputeHorizontalSDF(uv, rowCell.y),
                    _SectorCount * MOUNT_COL_SCALE, 1.0, _Softness);
                float rowMask = rowCell.x * colCell.x * 0.5 + 0.5;
                float3 layerColor = ComputeRowColor(
                    rowCell.y, colCell.y, rowCount, start, end,
                    _MountColorA, _MountColorB);
                result = lerp(result, layerColor, rowMask * _MountColorA.a);
            }
        }

        // 麦田前景（多层全宽横带 + 竖笔触）：
        //  行门与纹路共用同一行格（含循环错动相位）→ 带缘随列漏 ≤1 行（保留的漏出质感）
        {
            float wheatRows = max(1.0, round(_SectorCount * WHEAT_SCALE));
            float wheatCols = max(1.0, round(_SectorCount * WHEAT_COL_SCALE));
            for (int i = 0; i < WHEAT_LAYER_COUNT; i++)
            {
                float wheatStart = WHEAT_ROWS[i].x * wheatRows;
                float wheatEnd = WHEAT_ROWS[i].y * wheatRows;
                // 本带列场：U 波相位错开（风摆 time + WHEAT_ROW_OFFSET[i]）
                float distU = ComputeLongitudinalSDF(float2(uv.y, uv.x), time, WHEAT_ROW_OFFSET[i]);
                float2 wheatCol = ComputeCell(distU, wheatCols, 1.0, _Softness);
                // 行格：循环错动 frac(列号 × STEP)/行数 行（超一行格即回卷）
                float2 wheatRow = ComputeCell(
                    uv.y + frac(wheatCol.y * WHEAT_V_STEP) / wheatRows,
                    wheatRows, 1.0, _Softness);
                // 顶缘参差门：正弦沿列场（无 time → 门不移动）
                float topRow = wheatStart
                    + WHEAT_RAG_AMP * wheatRows * (0.5 + 0.5 * sin(distU * WHEAT_RAG_FREQ * (2.0 * PI)));
                if (wheatRow.y >= topRow && wheatRow.y < wheatEnd)
                {
                    float wheatMask = wheatRow.x * wheatCol.x * 0.5 + 0.5;
                    float3 wheatColor = ComputeRowColor(
                        wheatRow.y, wheatCol.y, wheatRows, wheatStart, wheatEnd,
                        _WheatColorA, _WheatColorB);
                    result = lerp(result, wheatColor, wheatMask * _WheatColorA.a);
                }
            }
        }
        return half4(result, 1);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "TheStarryNight_Main"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Main
            ENDHLSL
        }
    }

    Fallback Off
}
