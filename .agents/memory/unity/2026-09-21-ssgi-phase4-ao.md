---
name: AO（SSGI Phase 4）落地与验收
description: AO Feature（SSAO/HBAO）的交付清单、PC_Renderer 持久化与离屏对照验收读数，以及三个新陷阱（跨帧回读全局纹理必得 UnityBlack 4×4 / 后处理色彩分级改写 Debug 灰度 / SaveAssets 回写整个会话态）
date: 2026-09-21
---

# 2026-09-21 — AO（SSGI Phase 4）落地与验收

承接 [2026-09-21-diffusegi-ssgi-phase3.md](2026-09-21-diffusegi-ssgi-phase3.md)。骨架 Phase 4 的 AO 在此
落地为独立 Feature：与 SSR / DiffuseGI 各自配置、互不依赖，只复用 DiffuseGI 引入的
`ScreenSpaceTrace.hlsl`（几何层，无 BRDF 权重）。

## 交付物

`Assets/Mine/Shaders/PostProcess/SSR/` 下新增：

| 文件 | 职责 |
|---|---|
| `AOFeature.cs` | 档位、资源依赖、四阶段编排（Trace → BlurH → BlurV → Composite）、相机筛选 |
| `AO.shader` | uniforms + 四个全屏 Pass（0 Trace / 1 BlurH / 2 BlurV / 3 Composite） |
| `AOFunction.hlsl` | 遮蔽估计（SSAO 半球核 / HBAO 水平线积分）、双边滤波、合成 |
| `AO.md` | 使用 / 算法 / 参数 / 边界 / 已知陷阱 / 验收 |

`SSGI_ScreenSpace_Outline/SSGI_Content_Skeleton.md` 的 Phase 4 段按 Phase 3 同款结构重写
（已实现管线 / 估计量约定 / 与既有 SSAO 的关系 / 与 Bavoil 2008 的差异 / 尚未实施 / 验收记录），
文件结构树补 AO 四文件，步进策略矩阵的 SSAO/HBAO 两行改为实际实现。

## 关键约定（判读前必读）

- **输出是可见度 `V ∈ [0,1]`**（1 = 无遮挡），不是遮蔽量。合成时才乘强度：
  `occlusion = saturate((1 - V) × Intensity)`，`Final.rgb = Scene.rgb × (1 - occlusion)`。
  天空与法线无效像素返回 1。按可见度建模的好处是「滤波 / 上采样 / 暴露给别的 Feature」都可直接用 V。
- RT = **`R8_UNorm` 单通道**、低分辨率（档位分辨率除数 2 / 4）、无 mip；双边 + 双线性上采样到全分辨率。
- 档位 `GetTier` = (分辨率除数, 方向/样本数, 步进预算)：Low (4,4,6) / Medium (2,6,8) / High (2,8,12)。
- **HBAO 是有意简化版**：仰角相对**切线平面**测量（不是原文的视线→水平线完整框架，等价效果是正对相机的面遮蔽为 0）、
  `angleBias` 以弧度正弦进入、无 de-interleaved 分组 / 按方向权重 / 30° 固定偏移。
  **不得当作 Bavoil 2008 的等价实现引用**，差异已在 `AO.md` 与骨架里逐条标注。
- 事件 = `AfterRenderingSkybox`，与 DiffuseGI 同源（见 phase3 memory）：遮挡只需「不透明 + 天空盒」的深度与法线，
  且合成必须早于 URP 的 `_CameraOpaqueTexture` 拷贝，否则透明物体折射到不含 AO 的旧拷贝。
- 与 DiffuseGI 的联动（`IndirectDiffuse × AO`）**未接线**：`_AOTexture` 已发布但当前无线程使用者；
  骨架末尾的统一 Composite 公式留作后续设计，**不能**再把同一 AO 系数乘回本 Feature 已处理过的场景色（会二次衰减）。

## 持久化（用户常驻要求）

- AO Feature 已写入 `Assets/Settings/PC_Renderer.asset`：`m_RendererFeatures` 追加
  `{fileID: 4084880287550926522}`、`m_RendererFeatureMap` 追加 `baee7799fa68b038`、
  `m_Active: 1`、`settings: mode=1 (HBAO) / radius=1 / performance=1 (Medium) / intensity=1 / debug=0`。
- 运行期核对：`PC_Renderer` 下 8 个 Feature，`AOFeature` 已注册、反射读 Pass 事件 = `AfterRenderingSkybox`、
  `ValidateRendererFeatures → True`。

## 验收结论（离屏相机对照，非目视）

| 项 | 结果 |
|---|---|
| 编译 | `ShaderUtil.GetShaderMessages` → 0 条；`logs -l error` 干净 |
| 可见度范围（Medium） | HBAO 均值 0.983 / 最小 0.69；SSAO 均值 0.984 / 最小 0.61（最大均为 1.00） |
| 天空不被遮蔽 | 图像顶部 5% 行 100% 像素 `V = 1.00`（均值 0.9999） |
| 遮蔽分布 | `V < 0.9` 占 HBAO 6.5% / SSAO 6.6%；**无像素 < 0.5**（未见「整屏变黑」） |
| 合成方向 | `intensity 0.0001 → 1` 只变暗不变亮（正差值 0 px）：HBAO 21.6% 像素变暗、均值 −1.77/255；SSAO 5.8%、−5.21/255 |
| 合成公式 | 逐像素 `lin(composite) / lin(base) == V` 中位误差 0.0000；残差为 `ARGB32` 回读的 8 bit 量化放大（集中在暗部），非公式偏差 |
| 确定性 | `timeScale 1` 与 `0` 两次采集六张图读数逐字节一致（无时域项） |
| 档位接线 | 静态核对 `GetTier → PassData → SetInt`，与档位表一致（**未做运行期 uniform 回读**） |

采集方法同 Phase 3 的离屏对照：复制主相机（642×522）到 `ARGB32` RT、**关掉该相机的后处理**、
逐配置 `Render()` + `AsyncGPUReadback`；脚本 `.codex/tmp/ao/verify3.cs`（+ `verify4.cs` 只加 `timeScale = 0`）。
「开 / 关」对照固定用 `intensity 0.0001`（最小非零）而不是 0，因为 `0` 是 `AddRenderPasses` 里
「整体跳过」的哨兵值，两侧管线路径不同（见 phase3 memory 的判读教训）。

## 三个新陷阱

1. **跨帧回读 `SetGlobalTextureAfterPass` 发布的纹理，恒定读到内置 `UnityBlack`。**
   发布的是 RenderGraph 托管纹理，帧末归还池；下一帧 `Shader.GetGlobalTexture("_AOTexture")` 拿到的是
   4×4 `R8G8B8A8_SRGB` 全 0（`instance=-1362`），同一次调用里 `_GITexture` 返回的是同一个 `UnityBlack`。
   2026-09-21 据此一度误判为「AO 输出全 0 且分辨率只有 4×4」。判定效果只能在同帧内（Debug 视图）
   或走离屏相机对照采集。**「跨帧读全局纹理必为空」是事实，不是故障。**
2. **Debug 灰度会被场景后处理改写。** `Debug = AO` 写在同一颜色目标上，随后的
   `Assets/Settings/DefaultVolumeProfile.asset`（ColorAdjustments / SplitToning / ShadowsMidtonesHighlights /
   Tonemapping，全部 `active: 1` 且有 override）把灰度上限压到 209/255；关掉探针相机的
   `renderPostProcessing` 后立刻出现 255。**`Tonemapping.mode = 0`(None) 不等于没有色彩分级**，
   要拿原始可见度必须整项关闭后处理，并在结论里标注后处理状态。
3. **任何 `AssetDatabase.SaveAssets()` 会把整个会话的内存态写回 `PC_Renderer.asset`。**
   本轮一次 Feature 注册脚本顺带把 DiffuseGI 的 `m_Active` 写成 0（HEAD 为 1），已用
   `.codex/tmp/ao/restore.cs`（`LoadAssetAtPath` → `SetActive(true)` → `SetDirty` → `SaveAssets`）修回。
   **凡在 Editor 里动过 Feature 状态，收尾必须 `git diff Assets/Settings/` 核对**（新增 Feature 之外的
   既有条目也可能被顺带改写）。

## 未决 / 下一步

- 骨架 Phase 5（逐帧抖动 + 时域累积）未实现 → 静止画面有静态噪声，运动时无重投影。
- 未测（按收尾策略留作开放项）：SSAO/HBAO 目视质量对比、大半径下的屏幕边缘行为、运动稳定性、
  正交投影下的 `radius` 手感、档位的运行期 uniform 回读。
- `_AOTexture` 的跨 Feature 消费契约未定：全局纹理只在同相机同帧有效，且 AO Feature 未启用时槽位无绑定（读成黑色）。
- 提交：AO 四文件（代码）/ `PC_Renderer.asset`（持久化）/ 文档三件（`AO.md`、skeleton、本 memory），分三步。
