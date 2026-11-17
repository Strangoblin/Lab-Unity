// ═══════════════════════════════════════════════════════════════
//  LayeredGround — 单网格混合任意数量的地形层
//
//  层由 LayeredGroundSurface 的数组定义，每层一组 BRDF 参数
//  （颜色 / 阴影色 / 光滑度 / 金属度）。运行时只有两张数据：
//    · _LayerWeights  Texture2DArray，一层一片 R8 alpha，各层和为 1
//    · _LayerCount    有效层数
//
//  混合就是普通 alpha 叠层：笔刷每落一笔，把权重往目标层 lerp 一次。
//  所有层的参数按权重插值后只着色一次——层数不增加光照成本。
//
//  交互（草/雪/沙）同样是**每层的一个参数**：层上选一个 Interaction 源，
//  shader 按「绑定到该场的层权重和」分别合成坡度、视差与明暗，
//  一个像素落在雪上就只有雪在动。详见 LayeredGroundFields 与 Frag。
//
//  权重由 LayeredGroundSurface 从笔画数据生成，见
//  Demo/LayeredGroundSurface.cs；本 shader 只负责混合与一次普通 BRDF。
// ═══════════════════════════════════════════════════════════════
Shader "InteractionManager/LayeredGround"
{
    Properties
    {
        [Header(Blend)]
        _PatchStrength ("Patch Strength", Range(0, 1)) = 0.10

        [Header(Interaction)]
        // 视差映射步数：0 = 关（只用坡度法线），8–16 = 开。只改采样位置，不动顶点也不动深度。
        _ParallaxSteps ("Parallax Steps", Range(0, 32)) = 8
        _RippleScale ("Ripple Scale", Range(0, 8)) = 1.6
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.12

        // 以下参数由 LayeredGroundSurface 每帧通过 MaterialPropertyBlock 写入，
        // 材质上不保存副本，避免出现「材质值 vs 组件值」两份真相。
        [HideInInspector] _LayerCount ("Layer Count", Float) = 3
    }

    HLSLINCLUDE
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionGroundLighting.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionParallax.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Grass/GrassInteraction.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Snow/SnowInteraction.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Sand/SandInteraction.hlsl"

    // 与 LayeredGroundSurface.MaxLayers 保持一致。
    #define LAYER_MAX 8

    // 与 LayeredGroundInteractionSource 保持一致。
    #define LAYER_INTERACTION_NONE  0.0
    #define LAYER_INTERACTION_GRASS 1.0
    #define LAYER_INTERACTION_SNOW  2.0
    #define LAYER_INTERACTION_SAND  3.0

    TEXTURE2D_ARRAY(_LayerWeights);
    SAMPLER(sampler_LayerWeights);

    CBUFFER_START(UnityPerMaterial)
        float4 _LayerBaseColor[LAYER_MAX];
        float4 _LayerShadowTint[LAYER_MAX];
        float  _LayerSmoothness[LAYER_MAX];
        float  _LayerMetallic[LAYER_MAX];
        float  _LayerInteraction[LAYER_MAX];
        float  _LayerImprintDepth[LAYER_MAX];
        float  _LayerNormalStrength[LAYER_MAX];
        float  _LayerDarkening[LAYER_MAX];
        float  _LayerCount;
        float  _PatchStrength;
        float  _ParallaxSteps;
        float  _RippleScale;
        float  _RippleStrength;
    CBUFFER_END

    // 交互场的按层权重汇总。每个场各自一套，互不干扰：
    //   weight — 绑定到该场的层权重和（0..1）
    //   depth  — Σ w·落差，视差映射用
    //   scale  — Σ w·落差·法线强度，坡度用
    //   dark   — Σ w·明暗强度
    // 分量：x = grass，y = snow，z = sand。
    struct LayeredGroundFields
    {
        float4 weight;
        float4 depth;
        float4 scale;
        float4 dark;
    };

    LayeredGroundFields LayeredGround_EmptyFields()
    {
        LayeredGroundFields f;
        f.weight = 0;
        f.depth  = 0;
        f.scale  = 0;
        f.dark   = 0;
        return f;
    }

    /// 把第 index 层（权重 w）的交互参数累加进对应场。source 是层上的枚举值。
    void LayeredGround_AddField(inout LayeredGroundFields f, int index, half w)
    {
        float source = _LayerInteraction[index];
        if (source <= 0.5)
            return;

        half depth = w * _LayerImprintDepth[index];
        half scale = depth * _LayerNormalStrength[index];
        half dark  = w * _LayerDarkening[index];

        if (source < LAYER_INTERACTION_SNOW)
        {
            f.weight.x += w;     f.depth.x += depth;
            f.scale.x  += scale; f.dark.x  += dark;
        }
        else if (source < LAYER_INTERACTION_SAND)
        {
            f.weight.y += w;     f.depth.y += depth;
            f.scale.y  += scale; f.dark.y  += dark;
        }
        else
        {
            f.weight.z += w;     f.depth.z += depth;
            f.scale.z  += scale; f.dark.z  += dark;
        }
    }

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS   : TEXCOORD1;
        float2 uv         : TEXCOORD2;
    };

    // ════════════════════════════════════════════════════════════
    //  参数混合 — 每层取自己的 alpha，加权插值成一份 BRDF 输入
    //  权重之和恒为 1（笔刷每次落笔就是一次 lerp，见 Surface），
    //  这里仍做一次归一化，防止数据异常时整体变暗。
    // ════════════════════════════════════════════════════════════
    void LayeredGround_Blend(float2 uv,
        out half3 albedo, out half3 shadowTint, out half smoothness, out half metallic,
        out LayeredGroundFields fields)
    {
        albedo = 0;
        shadowTint = 0;
        smoothness = 0;
        metallic = 0;
        fields = LayeredGround_EmptyFields();

        half sum = 0;
        int count = (int)_LayerCount;

        [loop]
        for (int i = 0; i < LAYER_MAX; i++)
        {
            if (i >= count)
                break;

            half w = SAMPLE_TEXTURE2D_ARRAY(_LayerWeights, sampler_LayerWeights, uv, i).r;
            albedo     += w * _LayerBaseColor[i].rgb;
            shadowTint += w * _LayerShadowTint[i].rgb;
            smoothness += w * _LayerSmoothness[i];
            metallic   += w * _LayerMetallic[i];
            LayeredGround_AddField(fields, i, w);
            sum        += w;
        }

        if (sum > 1e-4)
        {
            half inv = 1.0 / sum;
            albedo *= inv;
            shadowTint *= inv;
            smoothness *= inv;
            metallic *= inv;
            fields.weight *= inv;
            fields.depth  *= inv;
            fields.scale  *= inv;
            fields.dark   *= inv;
        }
        else
        {
            // 空画布或数据异常时退回第 0 层，而不是黑屏。
            albedo     = _LayerBaseColor[0].rgb;
            shadowTint = _LayerShadowTint[0].rgb;
            smoothness = _LayerSmoothness[0];
            metallic   = _LayerMetallic[0];
            LayeredGround_AddField(fields, 0, 1.0);
        }
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.uv         = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 albedo, shadowTint;
                half smoothness, metallic;
                LayeredGroundFields fields;
                LayeredGround_Blend(i.uv, albedo, shadowTint, smoothness, metallic, fields);

                // 大尺度色斑，避免同层内完全平涂；各层共用这一份。
                float patch = sin(i.positionWS.x * 1.1) * cos(i.positionWS.z * 0.77);
                albedo *= 1.0 + patch * _PatchStrength * 0.5;

                // ── 交互：每个场各自采样、各自视差，再按"绑定该场的层权重"合成 ──
                // 一个像素落在雪上就只有雪在动，落在过渡带就按权重同时吃到两边的痕。
                // 每个场都有 early-out：权重为 0 或该子集不存在时不付采样的钱。
                float3 viewWS = normalize(_WorldSpaceCameraPos - i.positionWS);
                float2 slope = float2(0.0, 0.0);
                half darken = 1.0;

                if (fields.weight.x > 0.002 && _InteractionGrassValid > 0.0)
                {
                    // 草地的痕在草叶上，地面只留一点压暗的暗示。
                    darken *= 1.0 - SampleGrassCrushWS(i.positionWS) * fields.dark.x;
                }

                if (fields.weight.y > 0.002 && _InteractionSnowValid > 0.0)
                {
                    float3 sampleWS = i.positionWS + InteractionParallaxOffsetWS(
                        _InteractionSnowTex, sampler_InteractionSnowTex, i.positionWS,
                        viewWS, fields.depth.y, 1.0, (int)_ParallaxSteps, _InteractionSnowValid);

                    slope += SnowImprintSlopeXZ(sampleWS) * fields.scale.y;
                    darken *= 1.0 - saturate(SampleSnowImprintWS(sampleWS)) * fields.dark.y;
                }

                if (fields.weight.z > 0.002 && _InteractionSandValid > 0.0)
                {
                    // 沙只对平面以下的坑做视差（sign = -1），坑沿的脊仍然只走法线。
                    float3 sampleWS = i.positionWS + InteractionParallaxOffsetWS(
                        _InteractionSandTex, sampler_InteractionSandTex, i.positionWS,
                        viewWS, fields.depth.z, -1.0, (int)_ParallaxSteps, _InteractionSandValid);

                    slope += SandHeightSlopeXZ(sampleWS) * fields.scale.z;

                    // 未扰动沙面的细波纹：只动法线，按沙的权重淡入。
                    float2 rippleDir = float2(0.8, 0.6);
                    float ripple = cos(dot(i.positionWS.xz, rippleDir) * _RippleScale)
                                   * _RippleStrength * fields.weight.z;
                    slope += rippleDir * ripple;

                    darken *= 1.0 + saturate(-SampleSandHeightWS(sampleWS)) * fields.dark.z;
                }

                albedo *= darken;

                // 把坡度叠回网格法线上：网格不是水平面时也不会丢掉原有朝向。
                float3 normalWS = ApplyInteractionSlope(normalize(i.normalWS), slope, 1.0);
                return InteractionGroundShade(i.positionWS, normalWS,
                    half4(albedo, 1.0), half4(shadowTint, 1.0), smoothness, metallic);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertDepth
            #pragma fragment FragDepth

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings VertDepth(Attributes v)
            {
                DepthVaryings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz));
                return o;
            }

            half4 FragDepth(DepthVaryings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
