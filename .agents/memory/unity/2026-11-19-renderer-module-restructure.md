# Renderer 模块还原与拆分（2026-11-19）

`Custom` 原本应当承载通用自定义管线能力，互动功能落地时却被直接改成了互动管线：目录、类名、
深度全局都带着 `Custom` 的名字，后续 `TransparentDepth` 又派生出了 `ColoredShadow`。本次把三层职责拆回独立模块。

## 最终结构

```text
Assets/Mine/Special/Renderer/
  Custom/            通用可配置 depth / normal / color 捕获基线，不挂任何 Renderer
  InteractionCapture/互动管线（正交交互相机），挂在 IA_Renderer
  TransparentDepth/  透明深度合并，挂在 IA_Renderer
  TransparentShadow/光空间透射图（2026-11-20 由 ColoredShadow 改名），挂在 PC_Renderer
  Debug/             通用画面诊断工具，并入本目录，挂在 PC_Renderer
  Mask/  ScreenSpaceTrace/
```

2026-11-20 后续：`Debug/` 从 `Special/Debug/` 并入本目录；`Test/` 验证场景与测试材质删除；
`ColoredShadow/` 改名 `TransparentShadow/`，演示收敛到 `RenderScene.unity` + `PC_Renderer`。

`Custom` 与 `InteractionCapture` 不是继承关系：`CustomFeature` 是可直接配置或复制的通用实现，
`InteractionCaptureFeature` 保留互动专用 tag（`InteractionCapture`）、顺序依赖与语义。

## 关键操作

- `Custom/CustomFeature.cs` → `InteractionCapture/InteractionCaptureFeature.cs`；`.cs.meta` GUID `481ca827…` 随文件保留，`Assets/Settings/IA_Renderer.asset` 的 `m_EditorClassIdentifier` 改为 `Assembly-CSharp::Mine.Rendering.InteractionCaptureFeature`，子资产显示名改为 `Interaction Capture`。
- 新建通用 `Custom/CustomFeature.cs`（GUID `105674f2…`），参数化 depth / normal / color 的层掩码、pass tag、输出全局名与队列；默认 tag 为 URP 标准集合，不再含 `InteractionCapture`。
- 互动深度全局 `_CustomDepthTexture` → `_InteractionDepthTexture`；`IA_Renderer` 的 `copyDepth` 为 0，当前无运行时读者。
- `TransparentDepth/ColoredShadow/` → `Renderer/ColoredShadow/`；`ColoredShadowFeature.cs.meta` GUID `1d1550d7…`、`ColoredShadowFunction.hlsl.meta` GUID `627af4e9…` 不变，`ColorfulWindowTestRenderer.asset` 引用未动。
- 两份 Shader include 改到新路径：`ColorfulWindowGround.shader`、`SSL.shader`；SSL / ColorfulWindow / Renderer / InteractionManager 文档同步。
- 删除旧 Cookie 资产 `ColorfulWindow/StainedGlassCookie.asset` 与 `ColorfulWindowLight.cs`（含 meta，进 `.mcp/trash/20261119-113423-4e7360`，可 restore）。

## 验证

- `unityctl asset refresh`：`compilation succeeded`；`logs -l error` 为空，仅保留既有的 `_rendererIndex` 未使用警告。
- Renderer 资产解析：`IA_Renderer` = `InteractionCaptureFeature, TransparentDepthMergeFeature`；`ColorfulWindowTestRenderer` = `SSLFeature, ColoredShadowFeature`；两份资产均无 `null` 子资产。
- `ShaderUtil.ShaderHasError`：`PostProcess/SSL` 与 `Render/ColorfulWindowGround` 均为 `OK`（强制 reimport 后）。
- `CustomFeature` 可 `CreateInstance`，默认 settings 与 tag 数组非空。
- 未验证：重新导入后的实际画面与 Play 模式；通用 `CustomFeature` 未挂任何 Renderer，只有实例化检查。

## 边界

- 互动颜色捕获仍依赖 `TransparentDepthMergeFeature` 先把水面深度并进 `_CameraDepthTexture`；`IA_Renderer` 的 `CopyDepthMode` 必须保持 ForcePrepass。
- `Custom/Shaders/Custom.shader` 是采样模板，`Custom/` 不再被 `IA_Renderer` 引用；新专用管线应拥有自己的目录与 README。
- 历史记录中的 `CustomFeature` / `_CustomDepthTexture` 是当时名称，相关文件已加迁出注记。

相关：[CustomRenderer → 自录 Feature](2026-11-04-customrenderer-to-feature.md)、[彩窗光空间彩色透射图](2026-11-15-colorfulwindow-cookie.md)、[水体互动 Demo](2026-11-04-water-interaction-demo.md)。
