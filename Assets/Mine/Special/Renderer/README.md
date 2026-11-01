# Renderer — 渲染管线模块集合

正交相机管线、屏幕遮罩与屏幕空间追踪三类「管线级」代码的统一落点。效果侧的 Feature、Shader、材质不在这里，仍在各自效果目录（如 [PostProcess](../../Shaders/PostProcess/)）。

## 组成

| 目录 | 职责 | 入口 |
|---|---|---|
| `CustomRenderer/` | 正交相机专用最小渲染管线：按层掩码录制 Depth / Normal / Color，输出到相机 `targetTexture` | `CustomRendererData`（`Assets/Settings/IA_Renderer.asset` 引用）、`Custom.shader` |
| `Mask/` | 屏幕空间标量遮罩的共用 RenderGraph 描述符与对象录制 helper | `Mine.Rendering.ScreenMaskRenderer`，契约见 [README](Mask/README.md) |
| `ScreenSpaceTrace/` | 按需构建的惰性深度金字塔服务与正眼深归约 Compute | `Mine.ScreenSpaceTrace.ScreenSpaceDepthPyramid`，契约见 [README](ScreenSpaceTrace/README.md) |
| `TransparentDepth/` | 把透明队列几何的深度合并进 `_CameraDepthTexture`，供后处理统一消费 | `Mine.Rendering.TransparentDepthMergeFeature`（挂在 `Assets/Settings/PC_Renderer.asset`），契约见 [README](TransparentDepth/README.md) |
| `Test/` | 管线验证场景与测试材质，不参与默认播放 | `TransparentDepthTest.unity` |

## 2026-11-01 归并记录

- `Assets/Mine/Scripts/CustomRenderer/` → `CustomRenderer/`：与 `Mask/`、`ScreenSpaceTrace/` 并列，三条管线同层。
- `Assets/Mine/Special/Rendering/{Mask,ScreenSpaceTrace}/` → 同名子目录；`.meta` 随文件搬移，GUID 不变，`IA_Renderer.asset` 与 `PC_Renderer.asset` 的引用无需重指。
- `Assets/Mine/Special/UniversalFunction/PreparationRenderer/` 删除：`PreparationRenderer` / `PreparationRendererData` / `PreparationFeature` 早已标注 `[Obsolete]` 指向 `CustomRenderer`，是本分支 Unity 2022 旧版；无场景、Prefab 或 Renderer 资源引用其 GUID，删除前已进垃圾桶。

## 维护约定

- 命名空间保持原样（`Mine.Rendering`、`Mine.ScreenSpaceTrace`、全局 `CustomRenderer`），文件夹重组不连带改名；移动目录时 `.meta` 必须成对搬移。
- 代码与文档中以字符串写死的资产路径需要同步：SSGI 的三份 `Validation~/*.cs`、本目录两份 `Validation~`、`Special/HLSL/RayStepFunction.hlsl` 注释与相关 README 的链接。再次移动前先 `grep "Special/Renderer"` 全仓核对。
