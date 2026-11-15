// ═══════════════════════════════════════════════════════════════
//  function-lib.hlsl — 共享函数库模板（A 档纯函数零依赖基线 · 可编译）
//
//  定位: 跨效果横切复用库 → library.hlsl.shared 资源目录
//  历史实源参考（仅溯源）: Special/HLSL/ 18 库（HSV / SDF / LightFunction /
//    NormalFunction / BlendFunction / PBRFunction / ProjectionFunction
//    为 A 档同型例）
//
//  使用方式:
//    1. 拷贝到 library.hlsl.shared 解析且已授权的目录, 按语义域改名
//       （XxxFunction.hlsl 或域名词, 实源两种均有）
//    2. ⚠️ guard 与文件名绑定 — 改名必须同步改下方 guard
//    3. 替换所有 YourDomain 前缀（你的域前缀, 实源例: BlendNormal_/
//       SDF_/BRDF_ 语义前缀 — 避免无前缀通用名: MainLight 重复定义地雷）
//    4. 调用方 include 一律 Assets 全路径:
//       #include "YOUR_AUTHORIZED_LIBRARY_PATH/YourDomain.hlsl"
//
//  家族铁律（完整决策表见同目录 README.md）:
//    · 零兄弟 include —— 需要 URP/场景资源时按 B/C 档扩展（README 决策表）
//    · 零 CBUFFER —— URP cbuffer 由调用 shader 的 Properties 生成,
//      库只读参数; 需要贴图/开关时走 C 档全局命名契约
// ═══════════════════════════════════════════════════════════════

#ifndef FUNCTIONLIB_HLSL_INCLUDED
#define FUNCTIONLIB_HLSL_INCLUDED
// ⚠️ guard 与文件名绑定（Special 15/18 主风格: 文件名去连字符全大写 +
//   _HLSL_INCLUDED）; 本模板文件改名后同步为 e.g. RIMLIGHTFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  工具纯函数 — 置文件顶部（Metal 不支持下向引用, 2026-09-01 实坑）
// ════════════════════════════════════════════════════════════

/// <summary>2D 哈希（-1..1）</summary>
float2 YourDomainHash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

// ════════════════════════════════════════════════════════════
//  纯函数 demo — 零 URP 依赖、零全局声明, 输入输出参数化
//  ⚠️ demo 函数按你的域改写/删除, 只留本域需要的
// ════════════════════════════════════════════════════════════

/// <summary>区间重映射（线性, 输出已 clamp; 视用途决定是否 clamp）</summary>
float YourDomainRemap(float value, float2 inMinMax, float2 outMinMax)
{
    float t = (value - inMinMax.x) / max(inMinMax.y - inMinMax.x, 1e-6);
    return lerp(outMinMax.x, outMinMax.y, saturate(t));
}

/// <summary>三平面投影权重（纯数学; 采样循环在调用方写）</summary>
float3 YourDomainTriplanarWeight(float3 normalWS, float sharpness)
{
    float3 w = pow(saturate(abs(normalWS)), float3(sharpness, sharpness, sharpness));
    return w / (w.x + w.y + w.z);
}

// ════════════════════════════════════════════════════════════
//  B / C 档扩展（注释骨架 — 需要时按同目录 README 三档决策表改写）
// ════════════════════════════════════════════════════════════

// ⚠️ B 档 · 依赖调用方上下文（AdditionalLightsFunction / ShadowFunction /
//     TBN 先例）: 函数体调 GetMainLight() 等管线函数 —— 不自行 include,
//     头部补「依赖调用方」合同注释, 例:
//   // 依赖调用方先 include: Core.hlsl + Lighting.hlsl
//   //   （调用 shader 的 include 顺序: URP 内置 → 本库）
// ⚠️ C 档 · 自带依赖/自声明（BlurFunction / ENVFunction /
//     ParallaxFunction / RimLightFunction 先例）: 文件内自带 URP include
//     （DeclareDepthTexture / DeclareNormalsTexture / Lighting）或自声明
//     全局贴图 —— 资源名与调用 shader Properties / C# 管理器同名契约:
//     ENVFunction: TEXTURE2D(_FGDLut) + _UseFGDLut（FGDLutManager 注入）
//     ParallaxFunction: TEXTURE2D(_HeightMap)(Properties 同名暴露)

#endif // FUNCTIONLIB_HLSL_INCLUDED
