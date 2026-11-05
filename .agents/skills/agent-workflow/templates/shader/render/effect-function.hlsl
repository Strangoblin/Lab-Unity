// ═══════════════════════════════════════════════════════════════
//  effect-function.hlsl — 效果私有功能库（同目录 · 单效果专属）
//
//  历史实源参考（仅溯源）: Assets/Mine/Shaders/Render/InteriorMapping/InteriorMappingFunction.hlsl
//            Assets/Mine/Shaders/Render/VanGogh/TheStarryNightSDF.hlsl（合同注释范式）
//
//  使用方式: 随 effect-shader.shader 成对拷贝到
//    <category.Render>/<YourEffect>/, 双文件改名;
//    ⚠️ guard 与文件名绑定 — 重命名文件必须同步改 guard（下方）
//    ⚠️ 拷贝后在 effect-shader.shader 打开 include 行并同步路径
//
//  契约（基线 = 对调用方零要求, 全部输入走参数）:
//    · 库函数不强依赖调用方 CBUFFER/宏; 需要时在头部写
//      「依赖调用方定义:」合同注释（TheStarryNightSDF.hlsl 1-12 行范式）
//    · ⚠️ 依赖前置: include 是线性文本展开, 被引用全局必须在 include
//      行之前声明（2026-08-27 StarryNight 实坑 SDF_POINT_COUNT）
//    · ⚠️ 调用先于定义: 函数调用者必须先于被调用者定义 — Metal 不支持
//      下向引用, Hash 等工具函数置文件顶部（2026-09-01 实坑）
//
//  结构选择:
//    私有库（本形态）: 可自持 CBUFFER_START(UnityPerMaterial) 与
//      TEXTURE2D 声明、static const 常量（InteriorMappingFunction 先例）
//    共享库（library.hlsl.shared 资源目录）: 零 CBUFFER、参数进出
//      （18 库全部先例）; 跨效果复用才抽过去, 见 shader/hlsl/ 家族
// ═══════════════════════════════════════════════════════════════

#ifndef EFFECTFUNCTION_HLSL_INCLUDED
#define EFFECTFUNCTION_HLSL_INCLUDED
// ⚠️ guard 与文件名绑定 — 重命名文件时同步改（Special/HLSL 15/18 主风格:
//   文件名去连字符大写 + _HLSL_INCLUDED; 实源例: ADDITIONALLIGHTFUNCTION_HLSL_INCLUDED）

// ════════════════════════════════════════════════════════════
//  工具 — 纯函数置顶（防下向引用）; ⚠️ YourEffect 前缀全部改为效果名
//  （Interior* / SKY_ MOUNT_ WHEAT_ 前缀先例: 前缀 = 库的命名空间）
// ════════════════════════════════════════════════════════════

/// <summary>绕中心旋转 uv</summary>
float2 YourEffectRotateUV(float2 uv, float2 center, float angle)
{
    float s;
    float c;
    sincos(angle, s, c); // ⚠️ 自含数学用 sincos; 避免引入无用 include
    uv -= center;
    uv = float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
    uv += center;
    return uv;
}

/// <summary>沿 dir 方向的柔边色带（距离场式, 无需外部状态）</summary>
float YourEffectBand(float2 uv, float2 center, float2 dir, float width, float softness)
{
    float2 delta = uv - center;
    float d = abs(dot(delta, normalize(dir)));
    return 1.0 - smoothstep(width, width + softness, d);
}

// ════════════════════════════════════════════════════════════
//  私有库扩展点（按需解除/改写, 非模板激活体）
// ════════════════════════════════════════════════════════════

// ⚠️ 需要自持状态时（InteriorMappingFunction 先例 — 私有库允许）:
//   声明 Texture 在调用方 Properties 同名暴露（_InteriorMap）;
//   常量用 static const（INTERIOR_INV_PI 先例）; CBUFFER 同样引用
//   调用方 Properties 生成的那份 UnityPerMaterial, 不另开

// ⚠️ 依赖调用方全局时（TheStarryNightSDF 先例 — 头部合同注释）:
//   在此列出「调用方需 #define / static const / CBUFFER 提供什么」,
//   并在 effect-shader.shader 的 include 行之前完成声明（依赖前置）

#endif // EFFECTFUNCTION_HLSL_INCLUDED
