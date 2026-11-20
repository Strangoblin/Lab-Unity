# InteractionCapture — 互动管线（正交交互相机捕获）

`InteractionCaptureFeature` 拥有 `InteractionCamera` 的 `targetTexture`：按层绘制互动几何的
`InteractionCapture` Pass，把穿透深度写进颜色 RT（`_InteractionOriginTex`）。深度 / 法线通道可选，
用于需要参照面的处理器。

本模块是从 `Custom/` 拆出的专用管线；`Custom/` 保持通用能力，不再承载互动语义。

## 组成

| 文件 | 作用 |
|---|---|
| `InteractionCaptureFeature.cs` | `Mine.Rendering.InteractionCaptureFeature`：Depth / Normal / Color 三个捕获 Pass |
| `Validation~/InteractionCaptureSmokeTest.cs` | 64×64 RFloat 冒烟测试：Feature 必须拥有相机目标，关闭时目标保持空 |
| `Validation~/MigrateInteractionRenderer.cs` | 一次性迁移脚本：旧 `CustomRendererData` → `UniversalRendererData` + 本 Feature |

## 挂载与配置

挂在 `Assets/Settings/IA_Renderer.asset`（`PC_RPAsset` 的 Renderer List index 1），
显示名 `Interaction Capture`。该 `UniversalRendererData` 的 `opaqueLayerMask` / `transparentLayerMask` /
`prepassLayerMask` 全部为 0，URP 自身不画任何 opaque / transparent / prepass。

| 设置 | 当前值 | 说明 |
|---|---|---|
| `copyDepth` | 0 | 开启时发布 `_InteractionDepthTexture`；Demo 关闭，参照面用 `_CameraDepthTexture` |
| `copyColor` | 1 | 颜色层输出到相机目标 |
| `copyNormal` | 0 | 可选法线捕获，发布 `_CameraNormalTexture` |
| `depthLayerMask` | 1<<6（Interaction） | 深度捕获层 |
| `colorLayerMask` | 1<<7（Interactor） | 颜色捕获层 |
| `transparentMode` | 0 | 不透明队列；Color 在 `AfterRenderingTransparents`，Depth / Normal 在 `BeforeRenderingPrePasses` |

## 与 TransparentDepth 的顺序依赖

`CustomFeature` 时代的顺序契约继续成立：`IA_Renderer` 同时挂 `TransparentDepthMergeFeature`，
它先把水面（透明队列、`DepthOnly`）的深度并进 `_CameraDepthTexture`，互动颜色 Pass 再把这张全局
当作参照面。`IA_Renderer` 的 `CopyDepthMode` 必须是 **ForcePrepass（2）**；`copyDepth` 关闭时
不能再把 `_CameraDepthTexture` 同时挂成颜色 Pass 的深度附件再采样。

## Pass 标签优先级

颜色捕获的 tag 列表为 `SRPDefaultUnlit` → `UniversalForward` → `UniversalForwardOnly` → `InteractionCapture`，
**靠后的 tag 优先**。互动物体同时有 `UniversalForward` 和 `InteractionCapture` 两个 Pass，必须让
`InteractionCapture` 排在最后，否则捕获到的是物体的可见颜色而不是穿透深度。

## 边界

- Color Pass 清空相机目标为透明黑，Overlay 相机直接跳过。
- `Validation~` 目录以 `~` 结尾，Unity 不导入；脚本通过 Roslyn 入口执行。
- 本模块只负责捕获；穿透深度的语义（`_Intensity`、`_ContactDepth`、压满深度）在
  `Assets/Mine/Scripts/InteractionManager/` 的 Shader 与处理器里。
