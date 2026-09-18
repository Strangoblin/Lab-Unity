// ═══════════════════════════════════════════════════════════════
//  复杂效果材质 · 同目录私有功能库分离（渲染特征族 · 近期范式 · 可编译）
//
//  结构规范: references/standard/shader/shader-structure.md
//  实源参考: Assets/Mine/Shaders/Render/InteriorMapping/InteriorMapping.shader
//    （09-02 最新规范）; 同构第二例 HyperSpace/、HyperTube/（08-25）
//
//  使用方式（两步, 缺一不可）:
//    1. effect-shader.shader 与 effect-function.hlsl 成对拷贝到
//       Assets/Mine/Shaders/Render/<YourEffect>/, 两个文件都改名
//    2. 打开下方注释态的库 include 并改成实际路径（第 1 步改名后同步）
//    3. 把代码里所有 YourEffect 替换为你的效果名（含 Shader 名字符串）
//
//  拆库裁决: 单效果私有数学进同目录库（本形态）; 跨效果横切函数
//    抽 Assets/Mine/Special/HLSL/（hlsl/ 家族模板）, Shader 内不写算法细节
// ═══════════════════════════════════════════════════════════════

Shader "Render/YourEffect" // ⚠️ 重命名为你的效果名（与文件名一致）
{
    // ═══ Properties — 参数暴露; 复杂效果把「模式选择」交给 [Enum] ═══
    Properties
    {
        [Header(Base)]
        [MainTexture] _MainTex ("Main Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Effect)]
        [Enum(ModeA, 0, ModeB, 1)] // ⚠️ 枚举模式开关（InteriorMapping [Enum(Box, 0, Hemisphere, 1)] 先例）
        _Mode ("Mode", Float) = 0
        _Strength ("Strength", Range(0, 8)) = 1.5
        // ⚠️ 其余效果参数按需追加（保持与下方 CBUFFER 一一对应）
    }

    HLSLINCLUDE
    // ═══ include — 顺序: URP 内置库 → 自有功能库 ═══
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    // ⚠️ 拷贝步骤 2/2: 打开此行并改为实际全路径（实源同款写法）:
    // #include "Assets/Mine/Shaders/Render/InteriorMapping/InteriorMappingFunction.hlsl"
    // #include "Assets/Mine/Shaders/Render/<YourEffect>/YourEffectFunction.hlsl"
    //   ⚠️ RainDrop 实坑: 裸相对名 "RainDrop.hlsl" 在目录迁移后失效 —
    //     一律写 Assets 全路径; 重命名库文件时 include 行与库内 guard 同步改

    // ═══ 纹理声明 ═══
    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);

    // ═══ CBUFFER — 参数统一管理, 字段与 Properties 一致 ═══
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST;
        float _Mode;
        float _Strength;
    CBUFFER_END

    // ═══ 顶点输入/输出 — 前缀式（效果名 + Attributes/Varyings）═══
    // ⚠️ YourEffectAttributes/Varyings 里的 YourEffect 全部替换为效果名
    //   （同名结构若与库协作不冲突; 前缀是为了多入口/多 Pass 可读性）
    struct YourEffectAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct YourEffectVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS   : TEXCOORD2;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 对象空间 → 裁剪空间 + 世界坐标/法线（库函数消费）
    // ════════════════════════════════════════════════════════════
    YourEffectVaryings Vert(YourEffectAttributes input)
    {
        YourEffectVaryings output = (YourEffectVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv         = input.uv;
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 可编译入口（库 include 打开前不引用库函数）
    // ════════════════════════════════════════════════════════════
    half4 Frag(YourEffectVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
        half4 color = _BaseColor * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

        // ⚠️ 库 include 打开后, 主体形态（InteriorMappingRenderSurface 式 —
        //    多参传入 uv/世界坐标/相机位/导数, 库内不查全局, 见 effect-function.hlsl）:
        // int mode = (int)round(clamp(_Mode, 0.0, 1.0));
        // float hitMask;
        // float3 lit = YourEffectRenderSurface(
        //     input.uv, input.positionWS, GetCameraPositionWS(), input.normalWS,
        //     ddx(input.positionWS), ddy(input.positionWS),
        //     ddx(input.uv), ddy(input.uv),
        //     _Strength, mode, hitMask);
        // color.rgb = lit;

        return color;
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            // ⚠️ Pass 命名 PascalCase, 与功能对应（InteriorMapping 先例）
            Name "YourEffect"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back // ⚠️ 双面/内腔效果改 Cull Off（InteriorMapping 先例）
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            // ⚠️ 需要多光源/阴影变体时追加 #pragma multi_compile（Water/PBRToon 先例）
            ENDHLSL
        }
    }

    Fallback Off
}
