---
name: DiffuseGI SSGI Phase 3 落地与验收
description: DiffuseGI 屏幕空间间接漫反射的交付清单、离线 RT 验收结论、事件排序修复（AfterRenderingSkybox）与被撤回的旧结论，以及本轮新增的工具/API 陷阱
date: 2026-09-21
---

# 2026-09-21 — DiffuseGI（SSGI Phase 3）落地与验收

## 交付物

`Assets/Mine/Shaders/PostProcess/SSR/` 下新增（均未提交）：

| 文件 | 职责 |
|---|---|
| `DiffuseGIFeature.cs` | 档位、资源依赖、五阶段编排（Trace / Blur H / Blur V / Resolve / Composite）、相机筛选 |
| `DiffuseGI.shader` | uniforms + 五个全屏 Pass |
| `DiffuseGIFunction.hlsl` | 半球采样、辐亮度估计、保边重建与合成 |
| `ScreenSpaceTrace.hlsl` | 不依赖镜面权重的几何首命中（可与后续 SSR 重构共用） |
| `DiffuseGI.md` | 使用 / 参数 / 边界 / **已知缺陷** / 验收（2026-09-21 大改） |

`SSGI_ScreenSpace_Outline/SSGI_Content_Skeleton.md` 的 Phase 3 段同步了「已实现管线 / 尚未实施 /
验收记录」。原有 SSR 行为未改动。

## 验收结论（离线 RT 对照，非目视）

| 项 | 结果 |
|---|---|
| Intensity=0 且 Debug=Off vs Feature 禁用 | 逐像素位精确一致（`|OFF − Z| = 0.000000`） |
| 屏幕外 / 天空 | Skybox vs 纯亮绿 vs 纯黑：`|ΔGI| = 0.00000000` → gather 完全忽略天空盒，**PASS** |
| 正交投影 | 覆盖 5.70% / 均值 0.03103（透视 13.42% / 0.05008）→ 可用，不再是「需检查」 |
| 三档性能 | 渲染后回读 `_GISourceSize` / `_GIRayCount` / `_GIStepCount`，与档位表一致 |
| 滤波 | GI>0.02 像素上相对变化 ≈ 4.47%；`depthSigma` 0.01→2.0 只再变 1.48e-4 → **保边弱平滑，不是强去噪** |
| 确定性 | `Time.timeScale = 0` 后重复采集位精确一致 |
| Debug 通道 | Trace / Indirect / Confidence 均为全分辨率输出，**未被 SSR Debug 或后续 Pass 覆盖** |
| 事件排序 | **已修复** —— `AfterRenderingSkybox`，见下 |

未测（按收尾策略留作开放项，未做扫描）：薄墙漏光、深度边缘串色、相机运动稳定性。

## 事件排序：已修复，且旧根因分析被推翻

### 现在的正确规则

`RenderPassEvent` 不是全局排序键，但**同事件内自定义 Feature 一定排在 URP 内置 Pass 之前**，
依据是插入点顺序（`Library/PackageCache/com.unity.render-pipelines.universal@*/Runtime/UniversalRendererRenderGraph.cs`）：

```text
1275  RecordCustomRenderGraphPasses(AfterRenderingSkybox)
1283  m_CopyColorPass.Render(...)                      → 写 _CameraOpaqueTexture
1287  RecordCustomRenderGraphPasses(BeforeRenderingTransparents)
1303  m_RenderTransparentForwardPass.Render(...)
```

自定义 Feature 之间按 `rendererFeatures` 数组顺序。**要核对的是内置 Pass 与目标资源之间的先后**，
不是「同事件谁先谁后」。

### 修复内容

- 旧值 `BeforeRenderingTransparents` → 新值 `AfterRenderingSkybox`（`DiffuseGIFeature.cs` 的 `DiffuseGIPass` 构造函数）。
- 原因：透明物体（`Render/Water`、`Render/RainDrops`）用 `SampleSceneColor()` 读 `_CameraOpaqueTexture`
  作为 `transparent` 项再 alpha 混合；旧值让 GI 合成晚于 1283 的颜色拷贝，透明物体折射到**未叠加 GI 的拷贝**，
  把该区域的 GI 盖回原状。选 `AfterRenderingSkybox` 同时满足「输入只有不透明 + 天空盒」与「合成早于颜色拷贝」。
- 运行期核对：反射读 `PC_Renderer` 的 7 个 Feature，`DiffuseGIFeature ... passEvent=AfterRenderingSkybox`；
  intensity 0 → 1 有 28370 px 变亮（平均 +0.03144，最大 +0.57568），`Debug=Indirect` 最大通道 2.2207。

### 被撤回的旧结论（不要再引用）

- ~~「同事件号 ⇒ 顺序不可控，稳定化手段是 `BeforeRenderingTransparents - 1`」~~ —— 顺序是确定的（自定义在前），
  真问题是拷贝时机。`BeforeRenderingTransparents - 1` = `AfterRenderingSkybox`，但当时没意识到这也正是修复值。
- ~~「调试 RT 被污染（12.16% 色度像素），机制是 `SetGlobalTextureAfterPass` 的 in-place 读写别名」~~ ——
  复合 Pass 在 `Debug=3` 时无条件返回 `float4(indirect.aaa, 1.0)`（`DiffuseGI.shader:80`），按构造是灰度，
  不可能产生色度。屏幕上的色度只能来自**在本效果之后绘制**的透明几何覆盖（1275/1287 都早于 1303），
  属调试视图的取景伪影。
- ~~「追踪读到的本帧深度/场景色含透明几何（隐藏 Water/RainDrops 后均值 0.419 → 0.103）」~~ ——
  那是**截屏**测量的结果，水面覆盖约 12% 屏，其色值混进 debug 输出即可解释落差。
  要判定追踪是否命中透明几何，必须回读 `SetGlobalTextureAfterPass` 发布的 `_GITexture` 纹理。**开放项。**

### 残留现象：启用 Feature 会让水面变暗（开放项）

`AddRenderPasses` 有 `intensity <= 0f && debug == Off` 短路，所以 intensity 0 → 0.0001 等于「整体启用」：

| 对照 | 负差值像素 | 均值 |
|---|---|---|
| 0 → 0.0001（仅启用） | 197 | −0.02722 |
| 0.0001 → 1（GI 真实增量） | **0** | 0.00000 |
| 0 → 1 | 162 | −0.02482 |
| 0 → 0.0001，隐藏 `Plane (1)`（Water） | **0** | 0.00000 |
| 0 → 0.0001，事件改回 `BeforeRenderingTransparents` | 197 | −0.02722 |

⇒ 负差值**与 GI 无关、与事件顺序无关**，只源于「Feature 启用」带来的管线路径变化
（`ConfigureInput` 请求法线 → DepthNormals prepass；自写 `activeColorTexture` → 中间色 RT）。
Water 同时采样 `_CameraDepthTexture` 与 `_CameraOpaqueTexture`，故只有水面像素受影响。
量级 0.027 亮度 / 约 200 px，不影响可用性；prepass 与中间纹理哪一项是主因尚未区分。

**红旗排除（保留）**：`Plane (2)`（`Render/SimpleParallax`，AlphaTest + `ZWrite 0`）与此无关。

**判读教训（保留）**：做「减法对照」时必须先确认对照两侧的**管线路径**一致，否则测到的是路径差异而不是效果差异。
本次的「0 → 0.0001」对照恰好把 Feature 从跳过切成启用，两侧路径不同，不能用来当作 GI 的污染证据。

## 方法论：离屏 RT 对照采集

Play Mode 下 `ScreenCapture.CaptureScreenshotAsTexture()` 不可用。可行做法是复制主相机到一张
RenderTexture（`Camera.CopyFrom` + 反射关掉 `m_RenderPostProcessing`、按需设 `m_VolumeLayerMask` /
`m_RendererIndex`），改状态后 `await Task.Delay` 等一帧再读。读数差异按「减法对照」聚合。
目标相机的 `pixelRect` 与 Game View 尺寸无关，采集前先核对分辨率（本次 642×522）。

## 本轮新增的工具 / API 陷阱

1. **`unityctl script execute <file>` 忽略 `-u`，且只自动注入 `System.Text` + `UnityEngine`。**
   于是 `BindingFlags` / `Resources` / `ScriptableRendererData` 全部编译失败。多程序集需求一律走
   `script eval -u ... "$(cat file.cs)"`。已补进 `cli/unityctl.md`。
2. **`Texture2D.GetPixels()` 的 row 0 是图像底部。** 用 C# 聚合按 `i/w` 当下标是 y-自底向上，
   而拿 PNG 用 `sips` + 手写 BMP 解析按文件行序是 y-自顶向下。两套约定混用会把「左下角的异常」
   报成右上角，或让聚合对不上。比对前必须先声明约定。
3. **eval 环境是反射注入的独立程序集**：`internal` 成员（如 `DiffuseGIFeature.GetTier`）不可达，
   只能内联常量；带 lambda / 局部函数的内插字符串与 `x.Sum(p => ...)` 会被拒绝。
4. 同一天早些时候的 `Report()` 直接平均内存里的 `Color[]`，报出的均值差 −0.00863 无法从落盘 PNG 复现。
   **聚合要以落盘产物为准**，内存统计只作交叉验证（呼应 [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md)）。

## 未决 / 下一步

- 持久化：**已完成** —— DiffuseGI Feature 已写入 `Assets/Settings/PC_Renderer.asset`（运行时读回可见
  `PC_Renderer` 下 7 个 Feature，DiffuseGIFeature 在末位），不再是运行时临时挂载。
- 提交与清理：`.codex/tmp/diffusegi/` 约 50 个脚本、`Screenshots/diffusegi-*` 19 个目录（≈24 MB）
  需要先出清单再删，人工确认。`DiffuseGIFeature.cs.new` 是 gate 写入前的草稿副本，内容与正式文件一致，
  待删。
- 水面变暗的 prepass / 中间纹理归因；`_GITexture` 全局回读判定 gather 是否命中透明几何；
  透明物体在修复后的实际观感需在 Game View 人工确认。
