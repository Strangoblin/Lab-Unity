// ════════════════════════════════════════════════════════════════
//  SSPR.shader — Screen-Space Planar Reflection（零步进翻转版）
//  ════════════════════════════════════════════════════════════════
//  平面掩码 + 视空间镜像方向投影采样。无步进、无深度比较。
//
//  原理（镜像相机论据）：
//    平面反射 = 镜像相机所见。水平相机（无俯仰/翻滚）下，镜像相机
//    的成像恰好是原画面垂直反转 → 直接翻转 UV 采样即可，且对任意
//    内容距离精确（不是近似！）。
//    相机带俯仰/翻滚时，反转不再成立，退化为视空间镜像同态变换：
//      Rv = reflect(Vv, Nv) → 方向投影回屏幕 → 采样
//    该变换在内容无穷远（远景）时精确；相机水平时精确退化为 UV 反转。
//
//  为什么镜像"方向"而非"位置"：
//    旧版镜像世界坐标 P' = mirror(P) 在单深度缓冲下退化 —— 平面像素
//    的镜像点就是它自己（uv' = uv → 自采样）。镜像视线方向绕开该
//    退化点，反射方向只由平面法线决定，内容在无穷远假设下不需要
//    任何深度信息 → 零步进。
//
//  角色定位：远景粗略反射。在 maxDistance 之外为平面提供场景反射，
//    近处由 SSR / StochasticSSR 步进负责（_FlipFade 控制淡入区间）。
//
//  流程（Pass 0）：
//    1. 重建世界位置 P + 法线 N
//    2. 场景平面检测：法线 buffer 自动判定水平/竖直面 (dot > 0.95)
//    3. 视空间镜像方向 Rv = reflect(Vv, Nv)
//    4. Rv 投影回屏幕 → 采样场景颜色（无步进）
//    5. 内容在屏幕边缘平滑过渡到天空盒 cubemap，无硬边界
//
//  天空兜底：SampleSH（2 阶球谐辐照度，低频、隐约天空色调）。
//    反射出视锥的内容由 SH 天空兜底，避免越界硬跳变。
//    实测踩坑：unity_SpecCube0 在 URP atlas 模式下不绑定 → 纯黑；
//    urp_ReflProbes_Atlas 在后处理 pass 中 fallback 探针数据不可达 → 淡蓝。
// ════════════════════════════════════════════════════════════════

Shader "PostProcess/SSPR"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

    // ── 反射 ──
    float  _MaxDistance;        // 最大反射距离（世界单位，远景淡入的终点）
    float  _FlipFade;           // 远景淡入起点（0 时默认 0.3 * _MaxDistance）
    float  _Smoothness;         // 反射强度

    // ── Camera ──
    float4x4 _CameraViewMatrix;
    float4x4 _CameraProjectionMatrix;

    // ════════════════════════════════════════════════════════════
    //  天空采样（远景兜底：反射出视锥的内容 → SH 天空）
    //  SampleSH = 2 阶球谐辐照度，低频但方向正确（隐约天空色调）。
    //  方向用 normalWS（漫反射辐照度语义，与 BRDF_Env 一致）；
    //  反射方向 R 对 SH 无意义（低频下 R 与 N 无差别）。
    //  unity_SpecCube0 / urp_ReflProbes_Atlas 均不可靠（见下）。
    //  · unity_SpecCube0：atlas 模式下不绑定 → 纯黑
    //  · urp_ReflProbes_Atlas：后处理 pass 无 _CLUSTER_LIGHT_LOOP，
    //    引擎 fallback 探针数据不可达 → 纯淡蓝（实测）
    // ════════════════════════════════════════════════════════════

    float3 SampleSkybox(float3 normalWS)
    {
        return SampleSH(normalWS);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 0 — 平面像素 → 视空间镜像方向 → 屏幕采样（零步进）
    // ════════════════════════════════════════════════════════════

    half4 Frag_SSPR_Trace(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float rawDepth = SampleSceneDepth(uv);
        if (rawDepth >= 0.9999) return half4(0, 0, 0, 0); // 天空像素不参与

        float3 P = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        float3 N = SampleSceneNormals(uv);

        // ── 场景平面检测（法线 buffer 自动判定水平/竖直面）──
        float horiz = abs(dot(N, float3(0, 1, 0)));
        float vertX = abs(dot(N, float3(1, 0, 0)));
        float vertZ = abs(dot(N, float3(0, 0, 1)));
        float planarity = max(max(horiz, vertX), vertZ);
        if (planarity < 0.95) return half4(0, 0, 0, 0);

        // ── 视线方向 + 背面剔除 ──
        float3 viewDir = normalize(P - GetCameraPositionWS());
        if (dot(N, -viewDir) <= 0.0) return half4(0, 0, 0, 0);

        // ── 视空间镜像方向（内容无穷远时精确；相机水平时 = UV 垂直反转）──
        float3 Vv = mul((float3x3)_CameraViewMatrix, viewDir);
        float3 Nv = normalize(mul((float3x3)_CameraViewMatrix, N));
        float3 Rv = reflect(Vv, Nv);

        // ── 方向投影回屏幕（clip.w = -Rv.z，射向相机后方 → w ≤ 0）──
        float4 clip = mul(_CameraProjectionMatrix, float4(Rv, 0));
        float2 S = (float2(clip.x, clip.y * _ProjectionParams.x) * rcp(max(clip.w, 1e-5))) * 0.5 + 0.5;

        // 内容采样有效度：屏幕边缘 0.9 → 1.0 平滑过渡到天空盒（消除越界硬边界）
        float inBound = 0.0;
        if (clip.w > 0.0)
        {
            float2 e = abs(S * 2 - 1);
            inBound = 1.0 - saturate((max(e.x, e.y) - 0.9) / 0.1);
        }

        // ── 采样 + 衰减（内容 ↔ 天空盒 无缝过渡，alpha 不跳变）──
        float3 sky = SampleSkybox(N);
        float3 c = lerp(sky, SampleSceneColor(S), inBound);

        float  a = _Smoothness;                          // 反射强度
        a *= smoothstep(0.90, 1.0, planarity);           // 平面度渐变（去硬边）

        // 远景角色：随反射体视距淡入（0.3 * maxDistance 起，_FlipFade 可调）
        float viewDist  = distance(P, GetCameraPositionWS());
        float fadeStart = (_FlipFade > 0.0) ? _FlipFade : _MaxDistance * 0.3;
        a *= smoothstep(fadeStart, _MaxDistance, viewDist);

        return float4(c, a);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 1 — Alpha 合成（Blend SrcAlpha OneMinusSrcAlpha）
    // ════════════════════════════════════════════════════════════

    half4 Frag_SSPR_Composite(Varyings input) : SV_Target
    {
        return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
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
            Name "SSPR_Trace"
            Blend One Zero

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SSPR_Trace
            ENDHLSL
        }

        Pass
        {
            Name "SSPR_Composite"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SSPR_Composite
            ENDHLSL
        }
    }
}
