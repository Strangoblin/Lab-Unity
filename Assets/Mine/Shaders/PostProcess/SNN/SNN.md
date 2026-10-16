# SNN — 对称最近邻滤波（Symmetric Nearest Neighbour）

## 概述

全屏保边去噪：对四组关于中心对称的样本对（水平 / 垂直 / 两条对角），各取**与中心色更接近**的那一侧，
与中心像素一起求均值输出。因为只向相似的一侧取色，跨边缘时不会被对侧拉花，所以边缘得以保留。

---

## 架构

```
相机色 ─► Pass 0 Frag_SNN（4 对对称采样 + 中心，5 取 1 均值）─► tempRT ─► 写回相机色
```

| 类型 | 职责 |
|------|------|
| `SNNFeature.Settings` | 面板参数（Artistic / Debug）+ `PropertyToID` 预计算 |
| `SNNFeature.SNNPass` | `ScriptableRenderPass`，建 1 张图内 RT，编排「滤波 + 写回」两次 Blit |
| `SNN.shader` | 单个片元入口 `Frag_SNN`；顶点用 Blit.hlsl 的 `Vert` |

---

## 配置参数

### Feature 面板

| 字段 | 分组 | 范围 | 默认 | 说明 |
|---|---|---|---|---|
| snnShader | Technical | — | — | 指向 `SNN.shader` |
| Radius | Artistic | 1 – 10 | 3 | 每对样本距中心的最大像素距离（半径只缩放偏移） |
| debug | Debug | Off / Selection | Off | 中间结果直出 |

### Shader 参数

| Shader 名称 | 类型 | 来源 | 说明 |
|---|---|---|---|
| `_Radius` | float | Feature（`SetInt`） | 采样偏移缩放 |
| `_DebugMode` | float | Feature | `0` = Off，`1` = Selection |
| `_BlitTexture` | Texture2D | Blit.hlsl | 源纹理（滤波时是相机色，写回时是 tempRT） |

---

## 渲染管线

| 顺序 | Pass 名称 | 源 → 目标 | 说明 |
|:----:|-----------|-----------|------|
| 0 | `SNN` | 相机色 → `_SNNTempRT` | 4 对对称采样后取均值 |
| — | 写回 | `_SNNTempRT` → 相机色 | 纯拷贝（debug 时该结果同时在屏幕上） |

| RT | 规格 | 生命周期 |
|---|---|---|
| `_SNNTempRT` | 相机 target 描述符（`depthBufferBits = 0`） | 帧内，RenderGraph 托管 |

- `RenderPassEvent.AfterRenderingPostProcessing`；该 Shader **不采样深度/法线**，因此不声明 `ConfigureInput`
- 源与目标都声明为 `ReadWrite`，`AllowPassCulling(false)`

---

## 使用方式

**前置条件**：URP 17+；把 Feature 加到当前 Renderer 的 Feature 列表并指定 `SNN.shader`。
该 Feature **当前未接入 `Assets/Settings/PC_Renderer.asset`**，需要手动加入才会生效。

**运行时行为**：每帧 2 次全屏 Blit，参数只有 `_Radius` 与 `_DebugMode`。

**Debug 模式**：

| 档 | 行为 |
|---|---|
| `Off` | 输出 5 点均值（正常滤波结果） |
| `Selection` | 只输出**被选中的四个邻域**均值（扣掉中心），用于观察滤波器在往哪个方向拉色 |

两种输出共用同一次采样循环，不新增 Pass，也不改变采样预算；`Off` 路径的累加与归一化表达式未因 Debug 改变。

---

## 性能

| 指标 | 值 |
|---|---|
| 每帧全屏 Blit | 2 次（滤波 + 写回） |
| 图内 RT | 1 张全分辨率 |
| 每像素纹理读取 | 固定 5 次（中心 1 + 4 对 × 2 侧） |
| 循环边界 | 编译期常量 4 对；`Radius` 只缩放偏移，不进入循环次数 |

---

## 已知限制

- 只有一种采样预算（4 对固定方向），没有质量档；要更高质量需要按
  `.agents/agents/unity-developer/references/shader/postprocess/spatial-filter-budget.md`
  扩展成「档位 → 样本对数量与方向表」。
- 采样偏移方向固定为 0°/90°/45°/-45°，不做旋转抖动，强规则纹理上可能出现方向性伪影。
- 距离比较用平方欧氏距离，对 HDR 高亮像素不敏感（未做色调映射域比较）。
- 半径在面板上是整数（1–10）；Shader 侧按 float 处理。
- 未接入 Renderer，无画面对照与 XR 验证记录。

---

## 扩展点

- 加样本对数量/方向的质量档（Performance 枚举），半径继续只缩放偏移。
- 引入按亮度差加权的混合，进一步减少平坦区域的残留噪声。
- 与 Kuwahara 的样本表统一（两者都是固定 offset 表 + 半径缩放）。

---

## 字段命名速查

- Feature 面板字段：`snnShader` 为 camelCase；`Radius` 沿用搬运期命名（面板语义清晰，未改，避免丢序列化值）
- `PropertyToID`：`PascalCase + ID`（`RadiusID` / `DebugModeID`）
- Pass 索引：`const int FilterPassIndex`
- Shader 常量：`static const float SNNSampleCount` / `SNNPairCount`
