# Kuwahara 桑原滤波 — 使用说明

## 概述

`Kuwahara.shader` 合并了三种桑原滤波算法，通过 C# 脚本 `KuwaharaFeature.cs` 选择不同的 Pass 执行。
Basic / Generalized 共用 `Radius`，权重公式（`Sharpness` / `Hardness` / `WeightScale`）只在 Generalized 与 Aniso 中参与。

## Pass 管线总览

| Index | Name | 模式 | 算法 |
|:-----:|------|------|------|
| **0** | Basic | 普通桑原 | 4 象限分区域 → 硬选方差最低区 |
| **1** | Generalized | 广义桑原 | 8 扇形分区域 → 加权混合（权重 = 反方差） |
| **2** | StructureTensor | Aniso 张量 | ddx/ddy 计算结构张量 (Sxx, Syy, Sxy) |
| **3** | BlurHorizontal | Aniso 模糊 | BlurFunction 水平 5‑tap |
| **4** | EigenDecomp | Aniso 特征分解 | 垂直 5‑tap + 2×2 特征分解 → (cosφ, sinφ, φ, A) |
| **5** | AnisoFilter | Aniso 滤波 | 椭圆核各向异性桑原滤波 |

> 各模式之间的 Pass 互斥，C# 按 `KuwaharaType` 选择对应的 Pass 或 Pass 链。

---

## 参数说明

### Quality — 质量档（共用）

| 档 | 采样点数 | 张量降采样 | 说明 |
|:--:|:--------:|:----------:|------|
| Low | 9 | ÷2 | 预算最低；风格化区域的采样起伏最明显 |
| Medium | 16 | ÷1 | 折中档 |
| **High（默认）** | **25** | **不降采样** | **逐字段复现升档前的面板值 (SampleQuality=High, Downsample=0)** |

档位由 `Settings.ResolveQuality(quality, out sampleQuality, out downsampleLevel)` 纯函数展开，
一次决定**采样预算与张量链分辨率**（两者同源，分开暴露会让「High」名不副实）。
采样点数以 Shader 侧固定 offset 表实现，档位只选表、不进循环边界；Basic / Generalized 只用采样点数，Aniso 两者都用。

### Radius — 核半径（共用）

| 范围 | 默认 | 作用 |
|:----:|:----:|------|
| 1 – 10 | 5 | 滤波核的像素半径 |

**各模式下的含义：**
- **Basic/Generalized**：圆形采样区域的半径
- **Aniso**：椭圆核的基础半径，会被 anisotropy 拉伸/压缩

### Sharpness Q — 权重陡度（Generalized / Aniso）

| 范围 | 默认 | 作用 |
|:----:|:----:|------|
| 1.0 – 18.0 | 8.0 | 控制方差→权重的映射曲线陡度 |

**权重公式（Generalized / Aniso 共用）：**
```
w = 1 / (1 + pow(var × Hardness × WeightScale, Q × 0.5))
```

| Q 值 | 效果 |
|:----:|------|
| Q = 2 | `1/(1 + var)` — 线性衰减，过渡平滑 |
| Q = 4 | `1/(1 + var²)` — 拐点明显，低方差区权重高 |
| Q = 8 | `1/(1 + var⁴)` — 更锐利的截止（Aniso 默认风格） |

**各模式取值：**
- **Generalized**：C# 固定为 `Q = 2`（匹配原版 `1/(1+var)` 行为），面板 Sharpness 对它无效
- **Aniso**：使用面板设定的 Sharpness 值
- **Basic**：不做加权混合（硬选最低方差区），不读取 Q

### Hardness — 方差惩罚力度（Generalized / Aniso）

| 范围 | 默认 | 作用 |
|:----:|:----:|------|
| 1.0 – 100.0 | 8.0 | 对方差的惩罚系数 |

| Hardness | 效果 |
|:--------:|------|
| 较低 | 各区域混合更多，过渡平滑 |
| 较高 | 低方差区域几乎独占，风格化更强烈 |

### Weight Scale — 方差放大倍率（Generalized / Aniso）

| 范围 | 默认 | 作用 |
|:----:|:----:|------|
| 1 – 2000 | 1000 | 方差进入权重公式前的放大倍率 |

| Weight Scale | 效果 |
|:-----------:|------|
| 较小 (1–10) | 方差差异被压缩 → 各区域权重接近 → 更模糊 |
| 中等 (100) | 中等敏感度 |
| 较大 (1000) | 最小方差区权重远大于其他区 → 风格化强烈、不模糊 |

> 面板值在三种模式下都会写入材质，但只有 Generalized / Aniso 的片元用到它（Basic 只做方差硬选）。

### Alpha — 各向异性敏感度（Aniso 专用）

| 范围 | 默认 | 作用 |
|:----:|:----:|------|
| 0.01 – 2.0 | 1.0 | 控制边缘方向对椭圆核形状的影响 |

椭圆长轴：`a = Radius × (α + A) / α`
椭圆短轴：`b = Radius × α / (α + A)`

| Alpha | 效果 |
|:-----:|------|
| 较小 (0.1) | 弱梯度也能拉长椭圆 → 更多区域走向异性 |
| 中等 (1.0) | 默认平衡 |
| 较大 (2.0) | 只有强边缘才产生椭圆 → 接近各向同性 |

### BlurScale — 张量模糊半径（自动）

不暴露在面板。由质量档的降采样率推导，用于抵消降采样带来的块状感：
```
BlurScale = downsampleLevel + 1.0      // Low 3 / Medium 2 / High 1
```

> 张量是低频方向信息，降采样后由 Pass 5 的采样器自动做 bilinear 插值恢复，质量损失很小。

---

## 算法对比

| 维度 | Basic (Pass 0) | Generalized (Pass 1) | Aniso (Pass 2→5) |
|------|:--------------:|:--------------------:|:-----------------:|
| 分区数 | 4 | 8 | 8 |
| 分区方式 | 轴对齐象限 | atan2 角度扇形 | 多项式权重（抛物面） |
| 采样区域 | 圆形 | 圆形 | **椭圆**（沿边缘） |
| 融合方式 | **硬选最低方差** | **加权混合** | **加权混合**（含高斯衰减） |
| 风格化效果 | 最强烈（油画/卡通） | 适中 | 最平滑（保留纹理） |
| 性能 | 最快 | 中等 | 最慢（4 Passes） |

---

## 典型调参思路

### Generalized — 风格化保留边缘
- `Radius = 5–7`，`Sharpness = 2`，`Hardness = 4–8`
- 默认即可，效果类似"水彩风格"

### Generalized — 柔化过渡
- `Radius = 3–5`，`Sharpness = 1–2`，`Hardness = 1–4`
- 各区域权重更接近，模糊更均匀

### Aniso — 智能降噪（保留纹理方向）
- `Radius = 3–5`，`Alpha = 1.0`，`Quality = Medium`
- 沿边缘平滑，跨边缘保持

### Aniso — 风格化
- `Radius = 6–8`，`Alpha = 0.3–0.6`，`Sharpness = 8–12`，`Hardness = 8–16`
- 更强的各向异性 + 更锐利的权重截止

---

## Debug 视图

`Settings.debug` 是面板 Debug 组的枚举开关，默认 `Off`：

| 档位 | 行为 |
|------|------|
| `Off` | 正常输出滤波结果 |
| `StructureTensor` | 用 Pass 4 输出的特征向量场顶掉最终画面：RG = 主特征向量方向，B = 角度 φ，A = 各向异性强度 |

`StructureTensor` 取自张量链已有的中间纹理（Pass 2→4），不额外增加 Pass；**只有 `KuwaharaType = Anisotropic` 会生成该纹理**，
Basic / Generalized 下该档不生效（面板会保留选择但不改变输出）。

---

## C# Inspector 模式选择

1. 拖入 `Kuwahara.shader` 到 Inspector 的 `Kuwahara Shader` 槽
2. 在 `Kuwahara Type` 下拉选择 Basic / Generalized / Anisotropic
3. 其余参数根据所选模式决定是否生效：
   - **Quality**：三种模式都生效（Aniso 另用它决定张量链分辨率）
   - **Alpha**：仅 Aniso 生效
   - **Sharpness / Hardness / Weight Scale**：仅 Generalized 与 Aniso 生效（Basic 硬选方差最低区，不读权重公式）
   - **Debug**：`StructureTensor` 仅 Aniso 生效；默认 `Off`
