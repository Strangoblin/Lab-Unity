// ═══════════════════════════════════════════════════════════════
//  直写效果材质 · 单 Pass 自包含（渲染特征族 · 普通形态 · 可编译）
//
//  结构规范: references/standard/shader/shader-structure.md
//  实源参考: Assets/Mine/Shaders/Render/ 普通直写线（ObjInCard /
//    InteractorObject / XRay2）—— 存量多为旧写法（无横幅 / lowercase）,
//    本模板按现行规范重写, 不作逐字源
//
//  使用方式:
//    1. 复制到 Assets/Mine/Shaders/Render/<YourEffect>/, 改名 + 替换 ⚠️
//    2. 定位: 直接渲染在物体上的效果材质 —— 无阴影/深度 Pass 义务
//    3. 选型: 需要标准光照/阴影/深度/法线四 Pass → standard-shader.shader
//       需要算法拆库 → 本族 effect-shader.shader + effect-function.hlsl
//    4. 全屏效果 → postprocess 族（RendererFeature）, 本族不做面片直绘
// ═══════════════════════════════════════════════════════════════

Shader "Render/YourEffect" // ⚠️ 重命名为你的效果名（与文件名一致）
{
    // ═══ Properties — 对外参数, [Header] 分组; 单 Pass 直写无其他义务 ═══
    Properties
    {
        [MainTexture] _MainTex ("Main Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        // ⚠️ [Header(Effect)] 效果参数组
        // _Strength ("Strength", Range(0, 1)) = 0.5
        // _Speed ("Speed", Float) = 1.0
    }

    HLSLINCLUDE
    // ═══ include — 顺序: URP 内置库 → 自有功能库（本形态通常零本地依赖）═══
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    // ⚠️ 需要主光/附加光时追加:
    // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    // ═══ 纹理声明 — TEXTURE2D/SAMPLER 成对, 紧跟 include ═══
    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);

    // ═══ CBUFFER — 字段名/类型与 Properties 一致 ═══
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST; // ⚠️ 2D 纹理 tiling/offset 用, 无需纹理时可删
    CBUFFER_END

    // ═══ 顶点输入/输出 — 单文件直写用通用名; 多入口/与其他文件协作时
    //     改前缀式 XxxAttributes/XxxVaryings（InteriorMapping 先例）═══
    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
        // ⚠️ 需要法线/切线: float3 normalOS : NORMAL;（+ Vert 里 TransformObjectToWorldNormal）
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        // ⚠️ 需要世界坐标/法线做光照或视角效果时打开:
        // float3 positionWS : TEXCOORD1;
        // float3 normalWS   : TEXCOORD2;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 对象空间 → 裁剪空间
    // ════════════════════════════════════════════════════════════
    Varyings Vert(Attributes input)
    {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 无光照直写; 效果逻辑写于此, 复杂算法拆 .hlsl
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
        half4 color = _BaseColor * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

        // ⚠️ 效果逻辑直写于此（噪声 / 扭曲 / 时间动画 …）
        // ⚠️ 需要光照时（开 Lighting.hlsl + 上方 normalWS/positionWS）:
        //   Light mainLight = GetMainLight();
        //   color.rgb *= mainLight.color * saturate(dot(input.normalWS, mainLight.direction));

        return color;
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            // ⚠️ Pass 命名 PascalCase（多 Pass 时必需, 如 "XRayReveal"; 参考 XRay2/）
            Name "YourEffectPass"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            // ⚠️ 状态变体（按需替换上面三行）:
            //   Cull Off         — 双面渲染（InteriorMapping 内腔先例）
            //   ZWrite Off + Blend SrcAlpha OneMinusSrcAlpha + Queue Transparent — 半透明
            //   ZTest Always     — 无视深度测试
            // ⚠️ 遮挡显形等模板效果: SubShader 级加 Stencil 块
            //   （实源: Assets/Mine/Shaders/Render/XRay2/）

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
