# TransparentDepth — 半透明几何写入总深度图

`TransparentDepthMergeFeature` 把透明队列的几何**再画一遍**（只带深度附件、强制 ZWrite），合并进 URP 已经解析好的 `_CameraDepthTexture` 拷贝。之后所有读深度纹理的后处理（SSGI、景深、MaskBlur、MaskRBR……）都会把半透明表面当成真实表面处理，不需要各自再走一遍透明绘制。

## 组成

| 文件 | 作用 |
|---|---|
| `TransparentDepthMergeFeature.cs` | Feature 本体：`TransparentDepthPass`（合并）+ 可选 `DebugPass`（灰度视图） |
| `TransparentDepthDebug.shader` | `Hidden/Mine/TransparentDepthDebug`，把合并后的深度线性化后映射成灰度 |

已挂到 `Assets/Settings/PC_Renderer.asset`（index 6）。`settings.debug` 默认 `Off`，`debugShader` 已指到上面的 shader。

## 工作方式

1. URP 的 `CopyDepthPass`（或在 `ForcePrepass` 模式下直接写入的 `_CameraDepthTexture`）先给出不透明深度。
2. 本 pass 在 `AfterRenderingOpaques` 把 `_CameraDepthTexture` 绑成**深度附件**（`AccessFlags.ReadWrite`），用 `RenderStateBlock{ Depth: ZWrite = true, ZTest = LessEqual }` 画一遍透明队列。
3. 硬件深度测试完成 min 合并，不需要额外 RT，也不需要全屏 min shader。
4. `SetGlobalTextureAfterPass(_CameraDepthTexture)` 让后续 pass 走全局纹理也拿到合并后的版本；RenderGraph 的版本化保证后录制的读者读到写入后的内容。

**相机深度附件没有被碰过**：透明物体的颜色绘制、排序、混合与原实现完全一致（测试场景里 Feature 开/关的彩色输出逐像素相同）。

`ConfigureInput(ScriptableRenderPassInput.Depth)` 是必需的：它让 URP 把 `CopyDepthPass` 提前到本 pass 之前，即使 RP Asset 的 Copy Depth Mode 是 `AfterTransparents` 也一样。

## 配置契约

| 设置 | 约束 |
|---|---|
| `layerMask` | 参与深度合并的层；`0` = 关闭（用于 A/B 对照），默认全部 |
| `shaderTags` | 需要匹配透明材质的 pass 标签。URP Lit 用 `UniversalForward` / `UniversalForwardOnly`；**URP Unlit 的 forward pass 没有 LightMode 标签，只有 `SRPDefaultUnlit` 能匹配**，漏了它 Unlit 透明物体会静默不画 |
| `passEvent` | 必须在深度拷贝之后、第一个深度读者之前；默认 `AfterRenderingOpaques` |
| `debug` / `debugShader` | 灰度可视化，`AfterRenderingTransparents` 全屏 blit 到当前颜色目标 |

## 限制

- **半透明表面在深度上是不透明的**：这正是目的，但软边 alpha（粒子、雾片）会变成硬边遮挡。需要按层掩码挑选参与对象。
- **多一次绘制**：透明几何被画两遍（一次颜色、一次深度），顶点/剔除开销翻倍，片元只有深度。
- 透明 shader 自己若采样 `_CameraDepthTexture`，读到的是合并后的版本，可能自遮挡。
- 未验证：MSAA > 1 时的调试 blit、XR 单 pass instanced、Deferred 渲染路径。

## 测试场景

`Test/TransparentDepthTest.unity`（不随工程默认打开）：

- 相机在原点朝 +Z，`far = 30`；不透明背景墙 z = 20，不透明方块 z ≈ 14；
- 两层重叠的 URP Unlit 透明面：近 z = 6、远 z = 10，`renderQueue = 3000`、`ZWrite Off`。

复现 A/B（Editor 内，任选其一）：

1. 打开场景，选中 `PC_Renderer` → 该 Feature 把 `Debug` 设为 `Depth`：
   - `layerMask = 0` → 只看到墙（浅灰）与方块；
   - `layerMask = Everything` → 透明面各按自身深度显示（近面更亮）。
2. 关掉 `Debug` 后对比彩色画面，Feature 开关不改变输出。

2026-11-01 的验证截图保存在 `tmp/sessions/2026-11-01-transparent-depth/`（`B-depth-off.png`、`C-depth-on.png`、`A1/A2-color-*.png`）。
