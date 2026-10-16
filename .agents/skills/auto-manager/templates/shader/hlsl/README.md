# HLSL Library Templates

Cross-effect **shared function libraries** — the templates target the configured `library.hlsl.shared` resource after authorization. The source entries below are representative; inspect live files for current contracts. This directory keeps only `README.md` as markdown; everything else is a compilable code template body.

Shared-lib facts from the live source: consumers `#include` with the resolved full project asset path (never a bare relative name); libs never include sibling libs; libs hold **no CBUFFER** (URP generates it from the shader's Properties). Function ownership follows the semantic boundary below, not the number of consumers.

| Template | Actual role |
|---|---|
| [function-lib.hlsl](function-lib.hlsl) | 共享函数库(纯函数零依赖基线;A 档拷贝即用,B/C 档按决策表扩展) |

## 公共函数归属

| 归属 | 裁决 | 形态模板 |
|---|---|---|
| 单效果私有数学 / 专属 SDF | 与其 shader 同目录 | [render/](../render/README.md) 的 effect-function.hlsl |
| **项目公共函数**：项目/功能族共用流程，仍携带业务上下文、预设或表现策略 | 留项目/功能族目录，即使被多个变体调用 | 项目公共 `.hlsl` / struct / 参数；如 Weather 粒子上下文 |
| **通用公共函数**：脱离业务语义的数学/图形算法，输入输出、单位与依赖可独立说明 | 只有此类可入 `library.hlsl.shared` 解析的目录 | 本族 function-lib.hlsl；如 TRS、采样核、显式尺寸换算 |

入库前问：离开原项目/功能族、只提供合同规定的输入，算法是否仍成立？业务 Profile/枚举、项目专属资源名、表现调参和生命周期约定属于项目层。管线/XR 依赖须明确，但不要求通用函数脱离 Unity/URP；多处调用或改成纯函数均不足以证明通用。存量库不因此自动迁移。

Blur/SNN/Kuwahara 等空间滤波先使用 [后处理采样预算模板](../postprocess/spatial-filter-budget.hlsl)
建立“质量档定成本、半径定范围”的调用契约；满足通用公共函数判据才可入库，实际复用需求用于判断是否值得提炼。

standard-shader.shader 的 HLSLINCLUDE 内已留 ⚠️ include 行等待调用方填写已解析的共享库路径 —— 新共享库抽好后从那里链入。

## 依赖三档决策表(本家族核心规范)

共享库按**依赖深度**切档,模板基线 = A 档;升级依赖前先问「能不能参数化」。

| 档 | 形态 | 写法 | 实源例 |
|---|---|---|---|
| **A 纯函数** | 零 URP 依赖、零全局声明,输入输出参数化 | 拷贝 function-lib.hlsl 即用,只写函数 | HashFunction / HSV / SDF / LightFunction / NormalFunction / BlendFunction / PBRFunction / ProjectionFunction / TRS |
| **B 依赖调用方上下文** | 函数体调 `GetMainLight()` / `TransformWorldToObject` 等管线函数 | **不自行 include**;头部写「调用方需先 include: Core / Lighting / DeclareDepthTexture」合同注释;要求调用 shader 按序先 include(Water.shader L25-35 即实例) | AdditionalLightsFunction / TBN / DepthDiffFunction / CustomLighting / VSUV |
| **C 自带依赖/自声明** | 自带 URP include,或自声明全局贴图 + 与 Properties/C# 的全局命名契约 | 文件内 include Declare* 等;全局资源名 = 契约(`_FGDLut` 由 FGDLutManager C# 注入) | BlurFunction / ENVFunction / ParallaxFunction / RimLightFunction / DeclareCustomTexture(5) |

**选档裁决**:纯数学能参数化 → A;必须访问场景纹理(深度/法线/自备 RT)→ C 自带声明;必须访问管线光照结构 → B 合同注释交调用方。`#pragma multi_compile` 关键字块进库文件是 AdditionalLightsFunction/ShadowFunction 的特殊先例,慎用。

> ⚠️ **MainLight 重复定义历史先例**：ShadowFunction 与 AdditionalLightsFunction 曾有相同 `MainLight` 函数体，同时 include 会重复定义。新库命名避开无前缀通用名(见下)。

## 家族标准契约

- **include guard 统一**:`#ifndef <文件名去连字符全大写>_HLSL_INCLUDED`(Special 15/18 主风格;实源 ADDITIONALLIGHTFUNCTION_HLSL_INCLUDED);⚠️ guard 与文件名绑定,重命名文件必须同步
- **文件头必须 ═ 横幅**(中文职责 + ⚠️ 用法 + 依赖说明)—— 实源 13/18 缺横幅是 2026-05-29 外部导入旧账,存量不改,新库一律按模板
- **函数前缀语义模块化**(`BlendNormal_` / `SDF_` / `BRDF_` / `Diff_Lambert` 先例);避免无前缀通用名(`MainLight` / `GetHeight` / `TBN` 冲突史)
- **零兄弟 include、零 CBUFFER**(家族铁律;URP cbuffer 由 shader Properties 生成,库只读参数 / 或 C 档全局契约)
- **依赖前置 / 调用先于定义**(rules/shader-development.md 铁律;Hash 等工具置文件顶部)
- **SG Custom Function 形态**(`Xxx_float`/`_half` 双包装 + `SHADERGRAPH_PREVIEW` 保护,实源 CustomLighting/VSUV)→ 不落模板,照实源改
- 私有库允许自持 CBUFFER/static const,共享库不允许 —— 边界即本 README 分工表

## 代表实源（由 library.hlsl.shared 解析，契约以当前实源为准）

- A 档:HashFunction(通用浮点/整数 Hash，按用途与输入域选接口；合同见 `<已解析库根>/HashFunction.md`)/ HSV(色彩互转)/ SDF(球盒+平滑布尔)/ LightFunction(非 URP 基础光)/ NormalFunction(压缩量化)/ BlendFunction(6 种法线混合)/ PBRFunction(自研 GGX+毛发双瓣)/ ProjectionFunction(三平面)/ TRS(UV 与等比空间、正逆变换；角度为弧度)
- B 档:AdditionalLightsFunction(光照收集,含 pragma)/ TBN / DepthDiffFunction / CustomLighting、VSUV(SG Custom Function)
- C 档:BlurFunction(高斯+双边,自带 Declare*；调用方先 include Core → HashFunction → BlurFunction，库内不 include 兄弟库)/ ENVFunction(FGD LUT 路径)/ ParallaxFunction(自带 `_HeightMap` 声明)/ RimLightFunction / DeclareCustomTexture
- 代表消费方:Water.shader(6 库)、PBRToon.shader(5)、GrassInstance.shader(4)、RimToon/RimToonScreen、SSL/DDOF/SSSM/SSR/Kuwahara、BoidInstance.shader、SSC/SSO

> ⚠️ 本族用法 = 拷到 `library.hlsl.shared` 解析且已授权的目录后改名(文件名 = guard = 语义域),调用方用全路径 include。`.hlsl` 本层不做真编译(需 Unity 导入),改动后跑 `python3 .agents/skills/agent-maintenance/scripts/check_api_refs.py <本目录>` 核词法与过时 API。
