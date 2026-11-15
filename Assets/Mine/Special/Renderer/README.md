# Renderer — 渲染管线模块集合

正交相机管线、屏幕遮罩与屏幕空间追踪三类「管线级」代码的统一落点。效果侧的 Feature、Shader、材质不在这里，仍在各自效果目录（如 [PostProcess](../../Shaders/PostProcess/)）。

## 组成

| 目录 | 职责 | 入口 |
|---|---|---|
| `InteractionCapture/` | 正交相机专用的层掩码捕获：Feature 自己录制 Depth / Normal / Color 并输出到相机 `targetTexture` | `Mine.Rendering.InteractionCaptureFeature`（挂在 `Assets/Settings/IA_Renderer.asset`）、`Shaders/Custom.shader` |
| `Mask/` | 屏幕空间标量遮罩的共用 RenderGraph 描述符与对象录制 helper | `Mine.Rendering.ScreenMaskRenderer`，契约见 [README](Mask/README.md) |
| `ScreenSpaceTrace/` | 按需构建的惰性深度金字塔服务与正眼深归约 Compute | `Mine.ScreenSpaceTrace.ScreenSpaceDepthPyramid`，契约见 [README](ScreenSpaceTrace/README.md) |
| `TransparentDepth/` | 把透明队列几何的深度合并进 `_CameraDepthTexture`，供后处理统一消费 | `Mine.Rendering.TransparentDepthMergeFeature`（挂在 `Assets/Settings/PC_Renderer.asset`），契约见 [README](TransparentDepth/README.md) |
| `Test/` | 管线验证场景与测试材质，不参与默认播放 | `TransparentDepthTest.unity` |

## 2026-11-04 Unity 6 迁移记录（CustomRenderer → InteractionCaptureFeature）

- 症状：Unity 6 / URP 17 下互动物体的 `_InteractionOriginTex` 为空，其余效果正常。根因是自定义 `ScriptableRenderer` 只能靠 `Setup(ScriptableRenderContext, ref RenderingData)` 记账，而该重写在 RenderGraph 路径下不再被调用；`ScriptableRenderer.OnRecordRenderGraph` 是 `internal`，第三方子类无法录制 RG Pass。工程里 `URP_COMPATIBILITY_MODE` 虽然有定义（Standalone），但 RenderGraph 兼容模式开关为关，`useRenderGraph = true`，于是 `Setup()` 一次都没跑。
- 取舍：没有全局打开兼容模式。全项目其余 Feature（SSGI、MaskBlur、TransparentDepth 等）只实现了 `RecordRenderGraph`，开兼容模式会让它们静默失效。
- 方案（B）：`Assets/Settings/IA_Renderer.asset` 由 `CustomRendererData` 换成 `UniversalRendererData`，`opaqueLayerMask` / `transparentLayerMask` / `prepassLayerMask` 全部置 0 —— URP 自身不画任何 opaque/transparent/prepass，相机目标归 Feature 所有。
- 迁移：深度 Pass 在 `BeforeRenderingPrePasses` 录制到 `_CustomDepthTexture`（D32_SFloat_S8_UInt，Point/Clamp，写后发布全局）；法线 Pass 可选（`_CameraNormalTexture`，用私有深度附件，不覆盖已发布的深度）；颜色 Pass 在 `BeforeRenderingOpaques` 绑定 `activeColorTexture`，清成透明黑后再画颜色层。透传模式把两个事件分别移到 `AfterRenderingTransparents` / `BeforeRenderingTransparents`。
- `RenderScene.unity` 里 `InteractionCamera` 的 Clear Flags 由 Skybox 改为 SolidColor（黑）：该相机输出 RFloat RT，Skybox 会污染 R 通道的按压深度；清屏值必须是 0。
- 相机输出 RT 必须带深度缓冲：`_InteractionOriginTex` 的 Depth Stencil Format 由 None 改为 `D24_UNorm_S8_UInt`（92），`InteractionManager` 新建 RT 的 depth 由 0 改为 24。否则 Render Graph 每帧告警「output Render Texture must have a depth buffer」并回退默认深度格式（功能仍可用，但控制台会刷屏）。
- 删除：`CustomRenderer.cs`、`CustomRendererData.cs`、`Editor/CustomRendererDataEditor.cs`；`Custom.shader` 随模块搬到 `InteractionCapture/Shaders/`（GUID 不变，无资产引用）。
- 遗留：`ProjectSettings.asset` 里 `URP_COMPATIBILITY_MODE` 仍在（`ProjectSettings/` 未入库）。它现在只让 URP 多编译一套兼容路径，没有工程代码依赖；删掉之前先全仓确认没有别处使用 `Setup` / `OnCameraSetup` / `ConfigureCameraTarget`。

## 2026-11-01 归并记录

- `Assets/Mine/Scripts/CustomRenderer/` → `CustomRenderer/`：与 `Mask/`、`ScreenSpaceTrace/` 并列，三条管线同层。
- `Assets/Mine/Special/Rendering/{Mask,ScreenSpaceTrace}/` → 同名子目录；`.meta` 随文件搬移，GUID 不变，`IA_Renderer.asset` 与 `PC_Renderer.asset` 的引用无需重指。
- `Assets/Mine/Special/UniversalFunction/PreparationRenderer/` 删除：`PreparationRenderer` / `PreparationRendererData` / `PreparationFeature` 早已标注 `[Obsolete]` 指向 `CustomRenderer`，是本分支 Unity 2022 旧版；无场景、Prefab 或 Renderer 资源引用其 GUID，删除前已进垃圾桶。

## 维护约定

- 命名空间保持原样（`Mine.Rendering`、`Mine.ScreenSpaceTrace`），文件夹重组不连带改名；移动目录时 `.meta` 必须成对搬移。
- 代码与文档中以字符串写死的资产路径需要同步：SSGI 的三份 `Validation~/*.cs`、本目录两份 `Validation~`、`Special/HLSL/RayStepFunction.hlsl` 注释与相关 README 的链接。再次移动前先 `grep "Special/Renderer"` 全仓核对。
