# SSO — 屏幕空间轮廓（Screen Space Outline）

## 概述

全屏轮廓描边：用深度与法线差分检测边缘，叠加「投影条纹阴影」，最后与场景色合成。
差分方式（`DDXY` / `Basic` / `Sobel`）与阴影方式（`None` / `Hard` / `Soft`）都是 Shader 关键字，由 Feature 每帧切换。

---

## 架构

```
相机色 ─► 留档 tempMain ─┬─ Pass 0 Frag_SSO（深度/法线差分 + 条纹）─► diff（可降采样）
                         │                                               │
                         └──────── Pass 1 Frag_Composite ────────────────┴─► 相机色
                                    ▲ _BlitTexture = tempMain
                                    ▲ _SSOTex      = diff（全局纹理发布）
```

| 类型 | 职责 |
|------|------|
| `SSOFeature.Settings` | 面板参数（Technical / Performance / Artistic / Debug）+ `PropertyToID` 预计算 |
| `SSOFeature.OutlineParams` / `ShadowParams` | 深度 / 法线 / 阴影三组子参数 |
| `SSOFeature.SSORenderPass` | `ScriptableRenderPass`，建 2 张图内 RT，编排 3 次 Blit 与关键字切换 |
| `SSO.shader` | 两个片元入口 `Frag_SSO`（检测）与 `Frag_Composite`（合成） |

---

## 配置参数

### Feature 面板

| 字段 | 分组 | 范围 | 默认 | 说明 |
|---|---|---|---|---|
| ssoShader | Technical | — | — | 指向 `SSO.shader` |
| ssoType | Technical | DDXY / Basic / Sobel | Basic | 差分方式 |
| depthParams.intensity / thickness / threshold | Technical | 0–1 / 0–5 / float2 | 1 / 1 / (0.1, 0.2) | 深度轮廓强度、采样步长（× texel）、smoothstep 阈值 |
| normalParams.* | Technical | 同上 | 1 / 1 / (0.1, 0.2) | 法线轮廓同组参数 |
| shadowParams.shadowType | Technical | None / Hard / Soft | Soft | 条纹阴影方式 |
| shadowParams.Intensity / Sharpness / Thickness / Density | Technical | 0–1 | 0.5 / 1 / 0.1 / 0.5 | 条纹强度、投影锐度、条纹粗细、条纹疏密 |
| downsample | Performance | 0 – 4 | 0 | 轮廓 RT 的分辨率降级指数（`>> n`） |
| jitter | Artistic | 0 – 1 | 1.0 | 差分采样偏移的随机抖动幅度 |
| OutlineColor | Artistic | Color | white | 描边色（alpha 为混入比例） |
| debug | Debug | Off / Outline | Off | 中间结果直出 |

> `ShadowParams` 的 `Intensity` / `Sharpness` / `Thickness` / `Density` 沿用搬运期的 PascalCase 命名，改名为 camelCase 会丢序列化值，故保留。

### Shader 参数

| Shader 名称 | 类型 | 来源 | 说明 |
|---|---|---|---|
| `_DepthIntensity` / `_DepthThickness` / `_DepthThreshold` | float / float / float2 | Feature | 深度轮廓 |
| `_NormalIntensity` / `_NormalThickness` / `_NormalThreshold` | float / float / float2 | Feature | 法线轮廓 |
| `_ShadowIntensity` / `_ShadowSharpness` / `_ShadowThickness` / `_ShadowDensity` | float | Feature | 条纹阴影 |
| `_Jitter` / `_OutlineColor` | float / float4 | Feature | 抖动与描边色 |
| `_SSOTex` | Texture2D | Feature 全局纹理 | 轮廓 / 条纹中间结果（R = 轮廓强度，G = 受光因子，B = 条纹） |
| `_BlitTexture` | Texture2D | Blit.hlsl | 两个 Pass 的源色都是它（均为 `tempMain`） |

---

## 渲染管线

| 顺序 | Pass 名称 | 源 → 目标 | 说明 |
|:----:|-----------|-----------|------|
| — | 拷贝 | 相机色 → `_SSOTempMainRT` | 原图留档 |
| 0 | `SSO` | `_SSOTempMainRT` → `_SSODiffRT` | 深度 / 法线差分 + 条纹，输出 4 通道中间结果 |
| 1 | `SSO_Composite` | `_SSOTempMainRT` → 相机色 | 用中间结果的 R+B 混入描边色，G 调制条纹亮度 |
| — | Debug | `_SSODiffRT` → 相机色 | `debug = Outline` 时覆盖最终输出 |

| RT | 规格 | 生命周期 |
|---|---|---|
| `_SSOTempMainRT` | 相机 target 描述符（`depthBufferBits = 0`） | 帧内，RenderGraph 托管 |
| `_SSODiffRT` | 同上，宽高各 `>> downsample` | 帧内，RenderGraph 托管 |

- `RenderPassEvent.AfterRenderingOpaques`，`ConfigureInput(Depth | Normal)`（源色来自 `_BlitTexture`，不需要 Color/不透明纹理）
- `AllowPassCulling(false)`；三个纹理都是 `ReadWrite`

---

## 使用方式

**前置条件**：URP 17+；把 Feature 加到当前 Renderer 的 Feature 列表并指定 `SSO.shader`。
该 Feature **当前未接入 `Assets/Settings/PC_Renderer.asset`**，需要手动加入才会生效。

**运行时行为**：每帧 3 次全屏 Blit；Feature 每帧设置全部材质参数与两个关键字组。

**Debug 模式**：

| 档 | 行为 |
|---|---|
| `Off` | 正常合成 |
| `Outline` | 用 `_SSODiffRT` 顶掉最终输出，可单独观察轮廓强度 / 受光因子 / 条纹三个通道 |

---

## 性能

| 指标 | 值 |
|---|---|
| 每帧全屏 Blit | 3 次（1 拷贝 + 2 Pass） |
| 图内 RT | 2 张（其中轮廓 RT 可按 `downsample` 降级） |
| 每像素纹理读取 | `Basic`：深度 6 + 法线 5；`Sobel`：深度 10 + 法线 5；`DDXY`：深度 4 + 法线 3（含基础 2 次深度 + 1 次法线） |
| 条纹阴影 | 读取 1 次主光阴影图；三平面投影走 `SAMPLE_CUSTOM` 宏，只算条纹不采样贴图 |
| 循环边界 | 采样偏移表长度是编译期常量（2 / 4 / 8），`thickness` 与 `jitter` 只缩放偏移 |

`downsample` 只影响轮廓 Pass 的渲染目标分辨率，合成 Pass 仍按全分辨率执行。

---

## 已知限制

- **`SAMPLE_CUSTOM` 宏覆盖依赖 include 顺序**：`SSO.shader` 必须在 `#include ProjectionFunction.hlsl` **之前**定义
  `SAMPLE_CUSTOM`，把三平面投影的取纹素换成 `ShadowLine`。调整 include 顺序会静默改变行为。
- 轮廓 Pass 需要至少一个阴影关键字（`SSO_SHADOW_NONE` / `HARD` / `SOFT`）；三者都不定义时 `DiffShadow` 未赋值。
  Feature 每帧会设置其中一个，手工建材质调试时要注意。
- `Sobel` 模式只对**深度**用 Sobel 核，法线仍走 `Basic` 差分（与搬运前一致，未改）。
- `_SSOTex` 是本帧本相机的全局纹理，跨帧回读只能拿到 `UnityBlack`；判定效果要在同帧 Debug 视图或离屏相机对照下做。
- 轮廓 RT 降采样后由合成 Pass 用 `sampler_PointClamp` 取样，`downsample > 0` 时描边会出现台阶（这是原实现的选择，未改）。
- 未接入 Renderer，无画面对照与 XR 验证记录。

---

## 扩展点

- 把 `SAMPLE_CUSTOM` 的宏 hack 换成显式的条纹函数参数，去掉对 include 顺序的隐式依赖。
- 采样偏移表可继续加差分方式（关键字 + 表 + 分支）。
- 轮廓 RT 的降采样率可并入 Performance 档位（与 `jitter` / 差分方式一起构成质量档）。

---

## 字段命名速查

- Feature 面板字段：多为 camelCase（`ssoType` / `downsample` / `jitter`）；`OutlineColor` 与 `ShadowParams.*` 沿用搬运期命名
- `PropertyToID`：`PascalCase + ID`（`DepthIntensityID` / `SSOTexID`）
- Pass 索引：`const int OutlinePassIndex` / `CompositePassIndex`
- 关键字：`const string XxxKeyword`，与 Shader `#pragma multi_compile` 一一对应
