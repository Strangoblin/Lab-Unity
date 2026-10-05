# PCSS — Percentage-Closer Soft Shadows

**路径:** `Assets/Mine/Shaders/PostProcess/PCSS/`
**类型:** ScriptableRendererFeature + Compute Shader
**目标管线:** URP 17+ (Unity 6)

---

## 功能概述

自定义阴影管线，绕过 Unity 内置 CSM，完整实现 PCSS 软阴影：

- **多级联 PSSM**：级联数随性能档（2 / 3 / 4）+ 2×2 Tiled Atlas 布局
- **逐级联 Culling**：远近级联独立剔除，远级联自动减少 draw
- **Penumbra Mask**：4-sample 对角线预扫描 + 自适应步长 3×3，快速跳过全亮/全暗
- **Blocker Search**：Vogel disk 采样，估算平均遮挡深度
- **Penumbra 估算**：世界空间 → shadowmap pixels 物理正确转换，随 ShadowDistance 自动缩放
- **变核 PCF**：半影越大 → PCF 核越大 → 阴影越软
- **斜面深度偏移**：PCF 深度偏移随坡度和核大小自适应
- **双边保边模糊**：Compute Shader 实现，仅 penumbra 像素执行
- **时域累积**：仅相机重投影 + 方差钳制，把逐帧旋转的稀疏采样收敛为稳定软边

---

## 文件结构

```
PCSS/
├── PCSS.compute              ← Compute Shader（4 kernel 入口 + #include）
├── PCSSFunction.hlsl         ← 私有库：纹理声明、时域常量、VogelDisk、全部工具函数 + 双边模糊
├── PCSSTemplate.shader       ← 场景物体模板 shader（Forward + DepthNormals + CustomShadowCaster）
├── PCSSFeature.cs            ← ScriptableRendererFeature: CasterPass + PCSSPass
└── PCSS.md                   ← 本文档
```

时域累积的重投影 / 钳制 / 混合函数来自跨效果共享库 `Assets/Mine/Special/HLSL/TemporalFunction.hlsl`（A 档纯函数，零 CBUFFER、零兄弟 include），由 `PCSSFunction.hlsl` 顶部 include。

---

## 参数分层

参数分三组，职责不重叠：

| 组 | 判定标准 | 改它的理由 |
|---|---|---|
| **Technical** | 几何与投影、深度偏移 | 正确性（漏光 / 痤疮 / 覆盖范围） |
| **Performance** | 纯成本旋钮，不需要微调 | 帧率（划档位，一档同时决定多个量） |
| **Artistic** | 每个参数对应一个观感维度 | 画面好看与否 |

**Performance 组只有一个参数**，因为「对帧率影响大、且不需要微调」的参数不该逐个暴露——它们的合理取值是有限几组固定组合，而不是连续可调区间：

| 档位 | atlasRes / tileRes | 采样（blocker/PCF） | cascadeCount | shadowDistance |
|---|---|---|---|---|
| Low | 1024 / 512 | 8 | 2 | 30 m |
| Medium | 2048 / 1024 | 16 | 3 | 40 m |
| **High**（默认） | **2048 / 1024** | **32** | **4** | **50 m** |

High 档逐字段复现整理前的资产配置，故**默认档画面与整理前位级一致**。

`shadowDistance` 是这一表里唯一肉眼可见的量——低档位阴影覆盖范围会更近。`cascadeCount` 从自由参数（原 `[Range(1,8)]`）收进档位后上限为 4，与 HLSL 的 `_CascadeLightVP[4]` 对齐，越界从此不可达。

`pssmLambda` 不进档位：它决定 texel 密度在级联间的**分布方式**，不是总成本量。

**Artistic 组只有三个参数**，各自独立控制一个维度，互不耦合：

| 参数 | 默认值 | 控制的观感 |
|---|---|---|
| `softness` | 1.0 | 半影尺度：0 = 硬边，越大阴影越软 |
| `blur` | 1.0 | 双边保边模糊强度：0 = 关闭 |
| `temporal` | 0.9 | 时域收敛：0 = 关闭（逐帧噪点），越高越稳但响应越慢 |

实现细节（时域钳制半径/σ、深度与法线置信度、PCF 偏移乘数）已内化为 `PCSSFunction.hlsl` 顶部的 `TEMPORAL_*` 常量。需要重新暴露为美术参数时改那一段即可。

> 命名沿革：`softness` 原指 PCF 深度偏移的乘数（实为防痤疮的技术量），真正的软度叫 `lightSize`。整理后名字还给软度，偏移乘数固定为 1.0。

---

## 渲染管线

```
Frame Start
  │
  ├── CustomShadowCasterPass (AfterRenderingShadows)
  │     ├── PSSM Split → 级联距离（级联数由性能档决定）
  │     ├── 每级联独立球体包围盒 → Light View/Proj（texel-aligned）
  │     ├── 逐级联光源视角 Culling
  │     ├── ImportTexture(RFloat RT + Depth RT)
  │     └── 逐级联 DrawRendererList → 2×2 Atlas（RFloat R 通道存深度）
  │         （原生 pass，无 override material，SRP Batcher 有效）
  │
  └── PCSSPass (AfterRenderingTransparents)
        ├── cmd.DispatchCompute(PCSS_Main)
        │     ├── Frustum Corner Ray → 世界坐标重建
        │     ├── 级联选择 → Atlas UV → 双线性采样
        │     ├── Penumbra Mask（预扫描 + 自适应 3×3）
        │     ├── Blocker Search（Vogel disk）
        │     ├── Penumbra 估算 → 变核 PCF（采样角随 _FrameIndex 逐帧旋转）
        │     └── 输出 alpha 编码 mask（1=penumbra, 0=skip）
        ├── cmd.DispatchCompute(PCSS_Temporal)   [仅 penumbra 像素]
        │     ├── 世界位置重建 → _PrevViewProj 重投影
        │     ├── 深度/法线置信度 × 级联键 × 历史有效位
        │     ├── 历史方差钳制
        │     └── 混合 → _PCSS_Temporal，写回 ping-pong 历史
        ├── cmd.DispatchCompute(PCSS_BlurH)  [仅 mask 像素]
        ├── cmd.DispatchCompute(PCSS_BlurV)  [仅 mask 像素]
        └── Blitter.BlitCameraTexture → 屏幕
```

---

## Pass 编排

| 顺序 | Pass 名称 | 类型 | 目标 | 说明 |
|------|----------|------|------|------|
| — | CustomShadowCaster | RasterRenderPass | `_PCSS_ShadowCacheTex` (RFloat) | 逐级联原生 pass → 2×2 Atlas |
| — | PCSS Compute | UnsafePass (DispatchCompute) | `_PCSS_SoftShadow` (ARGBHalf) | PCSS + mask 编码 |
| — | Temporal Compute | UnsafePass (DispatchCompute) | `_PCSS_Temporal` (ARGBHalf) | 时域累积（仅 penumbra） |
| — | BlurH Compute | UnsafePass (DispatchCompute) | `_PCSS_BlurTemp` (ARGBHalf) | 水平双边模糊（仅 penumbra） |
| — | BlurV Compute | UnsafePass (DispatchCompute) | `_PCSS_SoftShadow` (ARGBHalf) | 垂直双边模糊（仅 penumbra） |
| — | Blit | — | `activeColorTexture` | 最终叠加到屏幕 |

五个 dispatch 在同一个 `UnsafePass` 内顺序发出。时域关闭时 BlurH 直接读 `_PCSS_SoftShadow`，时域开启时读 `_PCSS_Temporal`；下行模糊与 Blit 不受影响。

`temporal = 0` **不跳过 temporal dispatch**：`Temporal_Blend` 的 `lerp` 权重为 0 已等价于关闭，而历史仍需每帧写入——否则调高 `temporal` 的首帧拿到的是陈旧内容。同理，`temporalDebug` 在任何 `temporal` 取值下都可用。

## RT 规格

| RT | 格式 | 分辨率 | 生命周期 | 说明 |
|----|------|--------|---------|------|
| `_PCSS_ShadowCacheTex` | RFloat | 档位决定（Low 1024² / Medium·High 2048²） | 持久化 | 级联 2×2 Atlas |
| `_PCSS_ShadowDepth` | Depth 16bit | 同 Atlas | 临时 | Caster Pass 深度附件 |
| `_PCSS_SoftShadow` | ARGBHalf (enableRandomWrite) | 全屏 | 持久化 | PCSS 结果 + 模糊输出（alpha=mask） |
| `_PCSS_BlurTemp` | ARGBHalf (enableRandomWrite) | 全屏 | 持久化 | 模糊中间缓冲 |
| `_PCSS_Temporal` | ARGBHalf (enableRandomWrite) | 全屏 | 持久化 | 时域累积输出（alpha 恒 1） |
| history A/B | ARGBHalf (enableRandomWrite) | 全屏 | 持久化，**每相机一对** | 时域历史 ping-pong（alpha=历史有效位） |

### Atlas 尺寸下发契约

Atlas 分辨率由 C# 侧**和** shader 侧共同使用，必须同源：

- **C# 侧**（`PCSSFeature.cs`）：分配 RT、烘焙 texel-aligned 正交投影进 `_CascadeLightVP`、设置逐级联 viewport
- **shader 侧**：全部 texel↔UV 换算，唯一来源是 `_PCSS_AtlasParams`（`float4`）

```
_PCSS_AtlasParams = (tileRes, 1/tileRes, atlasRes, 1/atlasRes)     // .x tile, .y 1/tile, .z atlas, .w 1/atlas
```

沿用工程既有的「尺寸 + 倒数」打包惯例（同 `_ScreenSize`）。2×2 布局固定，故 `tileRes = atlasRes / 2`，布局本身与分辨率无关。

> **为什么单独列这一节**：解析分辨率曾是**半接线参数**——C# 用它做上述三件事，却从未传给 shader，而 shader 里所有 texel↔UV 换算全是硬编码字面量。默认 2048 下数值恰好全对，所以缺陷长期不可见；分辨率一旦可调就是必现的画面错乱。诊断这类缺陷**要 grep 数值字面量，不能 grep 参数名**。

---

## 参数速查

### Technical · Resources
| 参数 | 说明 |
|------|------|
| `pcssComputeShader` | `PCSS.compute`（必填，为空则整个 PCSSPass 跳过） |

### Performance
| 参数 | 默认值 | 说明 |
|------|--------|------|
| `performance` | High | Low / Medium / High，一档同时决定下表全部量 |

| 档位 | atlasRes / tileRes | 采样（blocker/PCF） | cascadeCount | shadowDistance |
|---|---|---|---|---|
| Low | 1024 / 512 | 8 | 2 | 30 |
| Medium | 2048 / 1024 | 16 | 3 | 40 |
| High | 2048 / 1024 | 32 | 4 | 50 |

采样数是 `PCSS_LOW` / `PCSS_MEDIUM` 关键词的依据（两者都未定义 = High），故换档会触发 compute shader 变体切换。

### Technical · Bias & Split
| 参数 | 默认值 | 说明 |
|------|--------|------|
| `pssmLambda` | 0.75 | PSSM 混合因子：越接近 1 越按对数切分（近处 texel 更密） |
| `depthBias` | 0.1 | 沿光源反方向偏移 (m) |
| `normalBias` | 0.0 | 沿法线偏移 (m) |

### Artistic
| 参数 | 默认值 | 说明 |
|------|--------|------|
| `softness` | 1.0 | 半影尺度：0 = 硬边，越大越软。物理上即光源尺寸 |
| `blur` | 1.0 | 双边保边模糊强度，0 = 关闭 |
| `temporal` | 0.9 | 历史权重。0.9 → 有效样本数约 10 帧；**0 等于关闭时域** |

### Debug
| 参数 | 默认值 | 说明 |
|------|--------|------|
| `showShadowMap` | true | 把 `finalTH` 直接 Blit 到屏幕（**画面即阴影图，非合成**） |
| `temporalDebug` | Off | Off / Reprojection / Confidence / HistoryUV，见下 |

`temporalDebug` 仅在 penumbra 像素上可见（非 penumbra 走硬值直通）：

| 模式 | 输出 | 判读 |
|------|------|------|
| Reprojection | `abs(histUV - uv) * 100` | 相机静止时应**全黑**；非黑 = 矩阵约定或 Y 翻转错误 |
| Confidence | 置信度标量 | 稳定表面应接近白；几何边缘应出现暗边 |
| HistoryUV | `float3(histUV, 0)` | 直接查看历史采样落点 |

---

## 时域累积

### 为什么需要

PCSS 的成本天然是 O(r²)：半影越大核越大。而档位的 32 个 Vogel 点在 100+ 像素的核上是**极度稀疏**的，带状走样是结构性缺陷，不是采样数不够。加采样数按平方增长，时域摊销是唯一便宜的解法。

选 PCSS 作为时域的第一个落点，是因为平行光下的阴影可见性**只依赖世界位置**——重投影不会引入偏差（对比 StochasticSSR 的 BRDF 被积函数依赖视线方向，重投影是有偏的）。

**独立条件**：`PCSS_Main` 的采样角由 `_FrameIndex * 0.618`（黄金比）逐帧旋转。不做这一步，时域累积只是一遍遍累加同一份偏差，**不收敛**。

### 有效性判定

历史被采纳需同时满足：

1. `Temporal_ReprojectUV` 成功（`clip.w > 0` 且落点在 `[0,1]²`）
2. 置信度非零：深度差 `exp(-|Δd| * depthScale)` × 法线一致性 `dot(N,N')^power`
3. `SelectCascade(P) == SelectCascade(P_hist)`（级联键一致）
4. 历史 alpha > 0.5（历史本身是 penumbra 累积，非硬值直通）
5. `_TemporalReset == 0`

任一不满足 → `confidence = 0` → 输出当帧值，不进历史。

### 钳制策略

固定为**方差钳制**（`Temporal_ClampVariance`），半径 2（5×5 邻域）、σ 倍数 1.5。盒式钳制对孤立离群点敏感，方差钳制用邻域一阶/二阶矩构造包围盒更稳。

要换回盒式，把 `PCSS.compute` 里的调用换成 `Temporal_ClampNeighborhood`（函数仍保留在共享库中），或调整 `PCSSFunction.hlsl` 的 `TEMPORAL_*` 常量。

### 历史重置触发条件

| 触发 | 原因 |
|------|------|
| 历史 RT 首次分配 / 尺寸变化 | 内容无意义 |
| 光源转向 > 5° | shadow map 整体变化，旧历史全错 |
| 相机实例首次出现 | 新相机无上一帧 VP |

### alpha 通道的复用

`_PCSS_SoftShadow` 四通道已满（RGB=阴影值，A=penumbra mask），无余量放置信度。方案让 **A 通道兼作历史有效位**：

- 非 penumbra 像素：RGB 保持硬 0/1 常量，alpha=0 → 历史标记为「此处无有效累积」
- penumbra 像素：alpha 恒为 1

alpha **全程不参与混合**。这保持了 `PCSS.compute` 早退分支与 `PCSSFunction.hlsl` 双边模糊中全部 mask 语义不变（模糊只在中心 `alpha<0.5` 时早退，且输出强制保留中心 alpha），下行模糊 pass 的行为与接入时域前完全一致。

### 多相机隔离

历史按 `camera.GetInstanceID()` 分桶。Game View 与 Scene View 的相机矩阵不同，共用一份历史会互相污染。超过 120 帧未刷新的相机历史予以回收（Scene View 重建时 RT 会泄漏）。

### 逐帧矩阵与约定

`_PrevViewProj` 取 `camera.projectionMatrix * camera.worldToCameraMatrix`——**Unity/GL 约定，不是 `GL.GetGPUProjectionMatrix`**。这与 `ReconstructWorldPosition` 的 Frustum Corner Ray 同源；用 GPU 矩阵会导致 Y 翻转与 Z 范围双重错位。

---

## Shader 常量速查

| 常量 | 值 | 用途 |
|------|-----|------|
| `TEMPORAL_CLAMP_RADIUS` | 2 | 钳制邻域半径（像素） |
| `TEMPORAL_CLAMP_SIGMA` | 1.5 | 方差包围盒半宽（σ 倍数） |
| `TEMPORAL_DEPTH_SCALE` | 10.0 | 深度置信度衰减（世界单位） |
| `TEMPORAL_NORMAL_POWER` | 8.0 | 法线一致性幂次 |
| `_PCSS_AtlasParams` | `(tile, 1/tile, atlas, 1/atlas)` | Atlas 尺寸，全部 texel↔UV 换算的唯一来源 |
| `searchPixels` | `20 * halfW0 / halfWci` | Blocker 搜索半径（tile 内像素），远级联自动缩小 |
| `preRadiusWS` | `0.15 * (tile/2) / halfWci` | Mask 预扫描半径（固定 0.15m 世界空间） |
| `maskPixels` | `clamp(roughPenumbra * 0.67, 2, max(2, preRadiusWS))` | Mask 自适应步长 |
| `penumbraWS` | `depthDiff * zDist / halfW * softness` | 半影世界空间尺寸 |
| `penumbraPixels` | `penumbraWS * (tile/2) / halfW`，上限 `100 / halfW` | 半影像素数（物理正确，与分辨率档位解耦） |
| `pcfBias` | `baseBias * (penumbraPixels / 100)` | PCF 深度偏移（乘数已固定为 1.0） |
| `baseBias` | `(20 * halfW0 / tile) * √(1-N·L²)/N·L / zDist` | 斜面基础偏移 |

`tile` 即 `_PCSS_AtlasParams.x`。除以 tile 的项在换档时会同步缩放，故 penumbra 的**世界空间**尺寸与档位无关——换档只改变它落地成多少个 texel。

---

## 使用方式

### 前置条件

1. URP Renderer 的 **Renderer Features** 中添加 `PCSSFeature`
2. 拖入 `PCSS.compute` → **Pcss Compute Shader**
3. 场景物体使用 `PCSSTemplate` shader（或任何含 `LightMode=CustomShadowCaster` pass 的 shader）
4. 场景中必须有 Directional Light（`RenderSettings.sun`）

### 自定义 shader 接入

物体 shader 只需添加一个 `CustomShadowCaster` pass，参考 `PCSSTemplate.shader`：
- `LightMode = "CustomShadowCaster"`
- Vertex 施加 shadow bias
- Fragment 返回 `positionCS.z`

---

## 性能与调参

接入时域后，同样的画面质量可以用更低的档位达到。换档**留到人眼验证之后**在 Editor 里试：先在 High 档确认 `temporal` 收敛，再逐步下调 `performance` 直到 penumbra 开始出现可察觉的蠕动。

换档同时改变三件事，判读时要分清：

| 变化 | 表现 |
|---|---|
| atlasRes 降低 | 阴影边缘整体变粗（texel 变大） |
| 采样数降低 | penumbra 噪点更多，时域收敛后更易蠕动 |
| cascadeCount / shadowDistance 降低 | 阴影覆盖范围变近，远处无阴影 |

代价是**延迟**：PCSS 的信号是对数式收敛而非一次到位，几何变化需要数帧才稳定。`temporal` 决定有效样本数上界（0.9 → 约 10 帧），越低响应越快但噪声越大。

---

## 已知限制

- 仅 Directional Light
- 场景物体需使用含 `CustomShadowCaster` pass 的 shader
- Compute Shader 无法访问 `UNITY_MATRIX_I_VP`，改 Frustum Corner Ray 替代
- 不劫持 URP 阴影，其他物体仍使用 Unity 内置阴影
- **仅相机重投影**，不申请 `ScriptableRenderPassInput.Motion`：
  - 接收物移动 → 深度校验不通过 → 自动回退当帧，无错误重投影
  - 投影物（遮挡物）移动 → 阴影表现为数帧滞后，由钳制逐步收敛
  - 快速旋转相机 → 边缘可能出现数帧拖影
- `_CascadeLightVP[4]` / `_CascadeAtlasOffset[4]` 的越界已随 `cascadeCount` 收进性能档（上限 4）**不可达**
- `_LightDirection` 为 `float3` 而 C# 侧按 `Vector4` 绑定（只读 `.xyz`，未报错；观察项）
- `showShadowMap` 是**调试视图**：直接 Blit 阴影图到屏幕，不参与场景光照合成

## 扩展点

- 劫持 `_ScreenSpaceShadowmapTexture` 零侵入替换（届时 `showShadowMap` 需改为合成路径）
- 级联轮换更新（奇数帧 0/2，偶数帧 1/3）
- 把时域累积提到 `AfterRenderingShadows`，与级联轮换共用同一份历史
- 点光源 / 聚光灯 PCSS
- `cascadeCount < 4` 时 2×2 布局会浪费 atlas 空间，可引入档位相关的可变布局
