# Renderer — 渲染管线模块集合

「管线级」RendererFeature 与共用录制的统一落点。效果侧的 Feature、Shader、材质不在这里，仍在各自效果目录（如 [PostProcess](../../Shaders/PostProcess/)）。一个模块 = 一个独立的 Feature（或一组共用 helper）+ 一份 README；模块之间可以互相依赖，但不在目录上互相嵌套。

## 组成

| 目录 | 职责 | 入口 |
|---|---|---|
| `Custom/` | 通用自定义管线能力 / 模板：可配置的 Depth / Normal / Color 层捕获，层掩码、pass tag、输出全局名与事件均可调；不挂任何 Renderer | `Mine.Rendering.CustomFeature`（[CustomFeature.cs](Custom/CustomFeature.cs)）、`Shaders/Custom.shader`（Shader 名 `Custom/Custom`），契约见 [README](Custom/README.md) |
| `InteractionCapture/` | 互动管线：正交交互相机按层捕获 Color（含 `InteractionCapture` pass）与可选 Depth / Normal，输出到相机 `targetTexture` | `Mine.Rendering.InteractionCaptureFeature`（挂在 `Assets/Settings/IA_Renderer.asset`），契约见 [README](InteractionCapture/README.md) |
| `Mask/` | 屏幕空间标量遮罩的共用 RenderGraph 描述符与对象录制 helper | `Mine.Rendering.ScreenMaskRenderer`，契约见 [README](Mask/README.md) |
| `ScreenSpaceTrace/` | 按需构建的惰性深度金字塔服务与正眼深归约 Compute | `Mine.ScreenSpaceTrace.ScreenSpaceDepthPyramid`，契约见 [README](ScreenSpaceTrace/README.md) |
| `TransparentDepth/` | 把透明队列几何的深度合并进 `_CameraDepthTexture`，供后处理统一消费 | `Mine.Rendering.TransparentDepthMergeFeature`（挂在 `Assets/Settings/IA_Renderer.asset`），契约见 [README](TransparentDepth/README.md) |
| `TransparentShadow/` | 从主光视角把 `TransparentWithShadow` 层的玻璃渲染成光空间透射 + 深度图，供地面与 SSL 做前后判断 | `Mine.Rendering.TransparentShadowFeature`（挂在 `Assets/Settings/PC_Renderer.asset`），契约见 [README](TransparentShadow/README.md) |
| `Debug/` | 通用画面诊断工具：用临时 RT 执行调用方材质并输出到相机颜色目标 | `Mine.Rendering.DebugOutputFeature`，契约见 [README](Debug/README.md) |

## 2026-11-19 模块还原与拆分

- `Custom/` 恢复为通用自定义管线能力 / 模板：`CustomFeature` 参数化 depth / normal / color 捕获、层掩码、pass tag、输出全局名与队列；不挂任何 Renderer，可直接配置或作为专用管线的基线。
- 互动管线从 `Custom/` 拆出为 `InteractionCapture/`：`CustomFeature` → `InteractionCaptureFeature`，`.cs.meta` GUID `481ca827…` 随文件保留，`Assets/Settings/IA_Renderer.asset` 只同步 `m_EditorClassIdentifier` 与显示名。
- `ColoredShadow/` 由 `TransparentDepth/ColoredShadow/` 提升为同级模块；`ColoredShadowFeature.cs.meta` GUID `1d1550d7…` 与 `ColoredShadowFunction.hlsl.meta` GUID `627af4e9…` 随文件保留，`ColorfulWindowTestRenderer.asset` 引用不变。两份 Shader include 与 SSL / ColorfulWindow 文档同步改路径。
- 2026-11-20：`ColoredShadow/` → `TransparentShadow/`，类名 `TransparentShadowFeature`、接收函数 `TransparentShadow_Transmission`、全局 `_TransparentShadow*`、Shader Tag `TransparentShadow` 统一改名；`.meta` GUID 不变。玻璃约定放到 `TransparentWithShadow` 层，测试玻璃 Shader 改为普通透明队列（正常排序）。
- 2026-11-20：演示收敛回主管线 —— `TransparentShadowFeature` 与 `SSLFeature` 接入 `PC_Renderer`（玻璃在 Layer 8），`PC_RPAsset` 移除测试 Renderer；`ColorfulWindowTest.unity` / `ColorfulWindowTestRenderer.asset` / 测试材质与 `Test/` 验证场景删除。`Debug/` 并入本目录，`TransparentDepthMergeFeature` 只留在 `IA_Renderer`。
- 互动深度全局由 `_CustomDepthTexture` 改为 `_InteractionDepthTexture`；`IA_Renderer` 里 `copyDepth` 为 0，当前没有运行时读者。
- 删除旧 Cookie 资产 `StainedGlassCookie.asset` / `ColorfulWindowLight.cs`（进 `.mcp/trash`，可 restore）。

## 2026-11-04 Unity 6 迁移记录（CustomRenderer → CustomFeature）

- 症状：Unity 6 / URP 17 下互动物体的 `_InteractionOriginTex` 为空，其余效果正常。根因是自定义 `ScriptableRenderer` 只能靠 `Setup(ScriptableRenderContext, ref RenderingData)` 记账，而该重写在 RenderGraph 路径下不再被调用；`ScriptableRenderer.OnRecordRenderGraph` 是 `internal`，第三方子类无法录制 RG Pass。工程里 `URP_COMPATIBILITY_MODE` 虽然有定义（Standalone），但 RenderGraph 兼容模式开关为关，`useRenderGraph = true`，于是 `Setup()` 一次都没跑。
- 取舍：没有全局打开兼容模式。全项目其余 Feature（SSGI、MaskBlur、TransparentDepth 等）只实现了 `RecordRenderGraph`，开兼容模式会让它们静默失效。
- 方案（B）：`Assets/Settings/IA_Renderer.asset` 由 `CustomRendererData` 换成 `UniversalRendererData`，`opaqueLayerMask` / `transparentLayerMask` / `prepassLayerMask` 全部置 0 —— URP 自身不画任何 opaque/transparent/prepass，相机目标归 Feature 所有。
- 迁移：深度 Pass 在 `BeforeRenderingPrePasses` 录制到 `_InteractionDepthTexture`（D32_SFloat_S8_UInt，Point/Clamp，写后发布全局）；法线 Pass 可选（`_CameraNormalTexture`，用私有深度附件，不覆盖已发布的深度）；颜色 Pass 在 `BeforeRenderingOpaques` 绑定 `activeColorTexture`，清成透明黑后再画颜色层。透传模式把两个事件分别移到 `AfterRenderingTransparents` / `BeforeRenderingTransparents`。
- `RenderScene.unity` 里 `InteractionCamera` 的 Clear Flags 由 Skybox 改为 SolidColor（黑）：该相机输出 RFloat RT，Skybox 会污染 R 通道的按压深度；清屏值必须是 0。
- 相机输出 RT 必须带深度缓冲：`_InteractionOriginTex` 的 Depth Stencil Format 由 None 改为 `D24_UNorm_S8_UInt`（92），`InteractionManager` 新建 RT 的 depth 由 0 改为 24。否则 Render Graph 每帧告警「output Render Texture must have a depth buffer」并回退默认深度格式（功能仍可用，但控制台会刷屏）。
- 删除：`CustomRenderer.cs`、`CustomRendererData.cs`、`Editor/CustomRendererDataEditor.cs`；`Custom.shader` 随模块搬到 `Custom/Shaders/`（GUID 不变，无资产引用）。
- 2026-11-05 改名：模块目录 `InteractionCapture/` → `Custom/`，`InteractionCaptureFeature` → `CustomFeature`（文件名与类名必须同时改，否则 Unity 认为脚本里没有同名类）；`.cs.meta` 的 GUID 随 `git mv` 保留，`IA_Renderer.asset` 里的子资产仍指向同一个 MonoScript，只需同步 `m_EditorClassIdentifier`。`Validation~` 无 `.meta`（`~` 后缀被 Unity 忽略）。2026-11-19 撤回该改名：互动实现迁回 `InteractionCapture/`，`Custom/` 恢复为通用模块（见上）。
- 遗留：`ProjectSettings.asset` 里 `URP_COMPATIBILITY_MODE` 仍在（`ProjectSettings/` 未入库）。它现在只让 URP 多编译一套兼容路径，没有工程代码依赖；删掉之前先全仓确认没有别处使用 `Setup` / `OnCameraSetup` / `ConfigureCameraTarget`。

## 2026-11-01 归并记录

- `Assets/Mine/Scripts/CustomRenderer/` → `CustomRenderer/`：与 `Mask/`、`ScreenSpaceTrace/` 并列，三条管线同层。
- `Assets/Mine/Special/Rendering/{Mask,ScreenSpaceTrace}/` → 同名子目录；`.meta` 随文件搬移，GUID 不变，`IA_Renderer.asset` 与 `PC_Renderer.asset` 的引用无需重指。
- `Assets/Mine/Special/UniversalFunction/PreparationRenderer/` 删除：`PreparationRenderer` / `PreparationRendererData` / `PreparationFeature` 早已标注 `[Obsolete]` 指向 `CustomRenderer`，是本分支 Unity 2022 旧版；无场景、Prefab 或 Renderer 资源引用其 GUID，删除前已进垃圾桶。

## 维护约定

- 命名空间保持原样（`Mine.Rendering`、`Mine.ScreenSpaceTrace`），文件夹重组不连带改名；移动目录时 `.meta` 必须成对搬移。
- 代码与文档中以字符串写死的资产路径需要同步：SSGI 的三份 `Validation~/*.cs`、本目录两份 `Validation~`、`Special/HLSL/RayStepFunction.hlsl` 注释与相关 README 的链接。再次移动前先 `grep "Special/Renderer"` 全仓核对。
