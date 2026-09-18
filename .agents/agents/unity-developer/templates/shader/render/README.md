# Render Templates

Render-feature **material** templates — effects rendered directly on geometry, not fullscreen postprocess pipelines. This directory keeps only `README.md` as markdown; everything else is a compilable code template body.

Shader-family division: `standard/` = function-independent skeletons · `postprocess/` = fullscreen Blit pipelines · **`render/` = materials rendered on objects** · `hlsl/` = cross-effect shared function libraries (see [../README.md](../README.md) for the table) · `particle/` reserved.

| Template | Actual role |
|---|---|
| [direct-effect.shader](direct-effect.shader) | 直写效果材质 · 单 Pass 自包含(普通形态;零本地依赖,状态自由) |
| [effect-shader.shader](effect-shader.shader) + [effect-function.hlsl](effect-function.hlsl) | 复杂效果材质 + 同目录私有 Function 库(成对拷贝,近期范式) |

## 形态观察(实源 `Assets/Mine/Shaders/Render/`:24 shader + 5 本地 .hlsl)

- **近期复杂带 hlsl(2026-08-25 后新范式 → 模板主干)**:HyperSpace/HyperTube(08-25)、InteriorMapping(09-02)、TheStarryNight + TheStarryNightSDF(09-03)。特征:═ 横幅、同目录库 + `Assets/...` 全路径 include、前缀 struct、库 guard + 「依赖调用方」合同注释、`Fallback Off`
- **普通直写存量**(Fur/ObjInCard/XRay/Gradient 线)多为旧写法(无横幅、lowercase、无 [Header] 分组)→ 模板按现行规范重写,不作逐字源;Amplify 生成物(Error/PCGUI)不入模板源
- Render/ 内 HyperTube/TheStarryNight 的 `Cull Off ZWrite Off` 面片直绘属旧做法 → 新全屏效果走 postprocess 族(RendererFeature),本族不做该形态

## 选型决策

| 需求 | 起点 |
|---|---|
| 标准材质(光照 + 阴影 + 深度 + 法线四 Pass) | [standard-shader.shader](../../standard/shader/standard-shader.shader)(standard/ 族) |
| 直写单 Pass 效果(自包含,无阴影/深度 Pass) | 本族 direct-effect.shader |
| 复杂效果 + 算法拆同目录私有库 | 本族 effect-shader.shader + effect-function.hlsl 对 |
| 全屏后处理管线 | [postprocess/](../postprocess/README.md) 族 |
| 跨效果复用的函数抽共享库 | `Assets/Mine/Special/HLSL/` → [hlsl/](../hlsl/README.md) 族模板 |

**拆库裁决**:单效果私有数学 / 专属 SDF → 同目录本地库(本族复杂对形态);跨效果横切(模糊 / BRDF / 光照 / 法线混合)→ Special/HLSL 共享库(hlsl/ 族)。

Unity 6 / URP 17 的默认能力、实例化与 XR 边界见 [网格材质能力契约](../../standard/shader/README.md#网格材质能力契约unity-60003--urp-173)；单 Pass 模板保持轻量，不自动增加阴影/深度 Pass。

## 实源(Assets 路径,只读参考)

| 形态 | 实源 |
|---|---|
| 材质对模板同构 | `Assets/Mine/Shaders/Render/InteriorMapping/InteriorMapping.shader` + `InteriorMappingFunction.hlsl`(09-02,最新规范) |
| 库依赖合同 + guard 先例 | `Assets/Mine/Shaders/Render/VanGogh/TheStarryNightSDF.hlsl`(头部合同注释 1-12 行) |
| 同构第二例(带 Shadertoy 映射 .md) | `Assets/Mine/Shaders/Render/HyperSpace/`、`HyperTube/` |

> ⚠️ 模板可编译承诺 = 激活代码零内联 ⚠️,⚠️ 标记只出现在注释行与字符串占位;复杂对需「双文件同拷 + include 路径同步」两步,见模板头横幅。
