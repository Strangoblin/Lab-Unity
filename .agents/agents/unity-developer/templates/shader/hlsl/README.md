# HLSL Library Templates

Cross-effect **shared function libraries** — the templates mirror `Assets/Mine/Special/HLSL/` (18 live libs). This directory keeps only `README.md` as markdown; everything else is a compilable code template body.

Shared-lib facts from the live source: consumers `#include` with the full `Assets/Mine/Special/HLSL/X.hlsl` path (never a bare relative name); libs never include sibling libs; libs hold **no CBUFFER** (URP generates it from the shader's Properties). A lib that only one effect uses is not shared — keep it next to its shader as a `render/` private lib.

| Template | Actual role |
|---|---|
| [function-lib.hlsl](function-lib.hlsl) | 共享函数库(纯函数零依赖基线;A 档拷贝即用,B/C 档按决策表扩展) |

## 与 render 族私有库的分工

| 归属 | 裁决 | 形态模板 |
|---|---|---|
| 单效果私有数学 / 专属 SDF | 与其 shader 同目录 | [render/](../render/README.md) 的 effect-function.hlsl |
| 跨效果横切(模糊/BRDF/光照/法线/采样包装) | 抽到 `Assets/Mine/Special/HLSL/` | 本族 function-lib.hlsl |

standard-shader.shader 的 HLSLINCLUDE 内已留 ⚠️ include 行指向 Special/HLSL —— 新共享库抽好后从那里链入。

## 依赖三档决策表(本家族核心规范)

共享库按**依赖深度**切档,模板基线 = A 档;升级依赖前先问「能不能参数化」。

| 档 | 形态 | 写法 | 实源例 |
|---|---|---|---|
| **A 纯函数** | 零 URP 依赖、零全局声明,输入输出参数化 | 拷贝 function-lib.hlsl 即用,只写函数 | HSV / SDF / LightFunction / NormalFunction / BlendFunction / PBRFunction / ProjectionFunction(7) |
| **B 依赖调用方上下文** | 函数体调 `GetMainLight()` / `TransformWorldToObject` 等管线函数 | **不自行 include**;头部写「调用方需先 include: Core / Lighting / DeclareDepthTexture」合同注释;要求调用 shader 按序先 include(Water.shader L25-35 即实例) | AdditionalLightsFunction / ShadowFunction / TBN / DepthDiffFunction / CustomLighting / VSUV |
| **C 自带依赖/自声明** | 自带 URP include,或自声明全局贴图 + 与 Properties/C# 的全局命名契约 | 文件内 include Declare* 等;全局资源名 = 契约(`_FGDLut` 由 FGDLutManager C# 注入) | BlurFunction / ENVFunction / ParallaxFunction / RimLightFunction / DeclareCustomTexture(5) |

**选档裁决**:纯数学能参数化 → A;必须访问场景纹理(深度/法线/自备 RT)→ C 自带声明;必须访问管线光照结构 → B 合同注释交调用方。`#pragma multi_compile` 关键字块进库文件是 AdditionalLightsFunction/ShadowFunction 的特殊先例,慎用。

> ⚠️ **MainLight 重复定义地雷**:ShadowFunction 与 AdditionalLightsFunction 内的 `MainLight` 函数体完全相同 —— 同一 shader 同时 include 两者必然重复定义。现状无 shader 同时引入,属潜伏;新库命名避开无前缀通用名(见下)。

## 家族标准契约

- **include guard 统一**:`#ifndef <文件名去连字符全大写>_HLSL_INCLUDED`(Special 15/18 主风格;实源 ADDITIONALLIGHTFUNCTION_HLSL_INCLUDED);⚠️ guard 与文件名绑定,重命名文件必须同步
- **文件头必须 ═ 横幅**(中文职责 + ⚠️ 用法 + 依赖说明)—— 实源 13/18 缺横幅是 2026-05-29 外部导入旧账,存量不改,新库一律按模板
- **函数前缀语义模块化**(`BlendNormal_` / `SDF_` / `BRDF_` / `Diff_Lambert` 先例);避免无前缀通用名(`MainLight` / `GetHeight` / `TBN` 冲突史)
- **零兄弟 include、零 CBUFFER**(家族铁律;URP cbuffer 由 shader Properties 生成,库只读参数 / 或 C 档全局契约)
- **依赖前置 / 调用先于定义**(rules/shader-development.md 铁律;Hash 等工具置文件顶部)
- **SG Custom Function 形态**(`Xxx_float`/`_half` 双包装 + `SHADERGRAPH_PREVIEW` 保护,实源 CustomLighting/VSUV)→ 不落模板,照实源改
- 私有库允许自持 CBUFFER/static const,共享库不允许 —— 边界即本 README 分工表

## 实源(Assets/Mine/Special/HLSL/,18 库只读参考)

- A 档 7:HSV(色彩互转)/ SDF(球盒+平滑布尔)/ LightFunction(非 URP 基础光)/ NormalFunction(压缩量化)/ BlendFunction(6 种法线混合)/ PBRFunction(自研 GGX+毛发双瓣)/ ProjectionFunction(三平面)
- B 档 6:AdditionalLightsFunction、ShadowFunction(光照收集,含 pragma)/ TBN / DepthDiffFunction / CustomLighting、VSUV(SG Custom Function)
- C 档 5:BlurFunction(高斯+双边,自带 Declare*)/ ENVFunction(FGD LUT 路径)/ ParallaxFunction(自带 `_HeightMap` 声明)/ RimLightFunction / DeclareCustomTexture
- 代表消费方:Water.shader(6 库)、PBRToon.shader(5)、GrassInstance.shader(4)、RimToon/RimToonScreen、SSL/DDOF/SSSM/SSR/Kuwahara、BoidInstance.shader、SSC/SSO

> ⚠️ 本族用法 = 拷到 `Assets/Mine/Special/HLSL/` 改名即用(文件名 = guard = 语义域),调用方用全路径 include。`.hlsl` 本层不做真编译(需 Unity 导入),改动后跑 `python3 .mcp/validation/check_api_refs.py <本目录>` 核词法与过时 API。
