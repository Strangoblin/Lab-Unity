---
name: unity-render-debug
description: 在 Unity 6 / URP 17 中调试全屏 Shader、RendererFeature 中间输出，或获取完整、固定分辨率的相机 PNG 做画面对照；串联真实 RenderGraph Debug 输出、Game View 观察与 SingleCameraRequest 离屏截图。
---

# Unity 渲染调试与相机截图

用实际相机管线生成调试画面；需要留档或受控对照时，将该画面输出为完整 PNG。
不要求每次 Shader 修改都截图。Editor 窗口布局 / Inspector / UI 截图使用窗口工具；本流程输出相机渲染图。
渲染耗时 A/B 对照使用 [unity-render-benchmark](../unity-render-benchmark/SKILL.md)，不从截图或窗口FPS推导GPU成本。

## 选择调试输出

| 目标 | 使用方式 |
|---|---|
| Feature 自身的 Trace / Mask / Resolve 等中间结果 | 使用其 Debug 分支，将本帧实际中间纹理写到 camera color |
| 独立全屏材质或 Shader | 用 `python3 .mcp/validation/project_resources.py path unity.debug-output-feature` 解析现有 DebugOutputFeature；优先 `settings.debugMaterial`，保留参数、贴图与关键字；没有材质时使用 `debugShader` |
| 当前最终相机画面 | 保持正常输出，不额外启用 Debug Feature |
| 隔离网格材质 / 近远距离对照 | 按 [载体选择](../auto-manager/capabilities/shader-validation.md) 建临时预览场景，继续使用真实 Renderer 配置 |

DebugOutputFeature 执行材质 Pass 0；`additionalPassIndex >= 0` 可追加合法 Pass，默认 -1 复制输出；需要场景深度时开启 `requireDepth`。Shader 输入为当前场景色 `_BlitTexture`，使用 URP `Blit.hlsl` 的 Vert / Varyings。其他 Feature 的专用资源仍需按其真实契约绑定，通用 Debug Feature 不自动补齐。

使用该 Feature 时同时开启 Active 与 `settings.debug`。双 Pass 的第二遍 `_BlitTexture` 是第一遍结果；非法 Pass 索引会退回复制，深度请求也不保证纹理有效，需核对实际 Pass 执行和深度输入。它固定在 `AfterRenderingPostProcessing`，验证原 Feature 的阶段或输入色彩范围时使用原 Feature 的 Debug 分支。

## 运行与观察

1. 按 [unity-editor](../unity-editor/SKILL.md) 检查当前 Editor / bridge；未连接时保留脚本与计划，说明尚未渲染，不为截图自动关闭 / 重启 Editor。
2. 按 [编译与证据](../auto-manager/capabilities/shader-validation.md#共用编译与证据) 检查实际 Shader Pass / 变体；资源刷新成功不等于全部 Shader 已编译。
3. 核对目标相机实际使用的 Renderer、Feature Active、Debug 模式、材质和输入资源。只修改任务需要的调试状态，记录原值；材质参数对照优先使用临时副本。
4. 静态效果可在 Edit Mode 渲染；时间动画、生命周期或资源初始化需要 Play Mode 时沿用当前授权与模式。Game View 中观察目标输出和错误日志。
5. 需要完整截图、固定尺寸或跨档对照时，读取 [相机 PNG 流程](references/camera-capture.md)，复用 [camera-offscreen-capture.cs](scripts/camera-offscreen-capture.cs)，执行 `SingleCameraRequest → RenderTexture → ReadPixels → PNG`。

Renderer / 材质正由其他流程编辑时，先协调具体字段归属，优先使用临时配置；恢复只处理本流程改动且未被对方更新的字段，不整份写回旧快照。发现冲突先保留双方改动并说明，避免覆盖并发工作。

## 对照条件与判读

- 固定相机 pose、投影、aspect、物体、光照、Renderer、输出尺寸、曝光与显示映射；仅改变被测项。文件名区分参数 / 档位 / 近远位置，记录条件。
- 测原始 Mask / 数据输出时，可关闭探针相机的 Volume 后处理；最终合成对照保留任务所需后处理。不能把两种条件混作同一基线。
- Debug 输出之后的透明物体或其他 Pass 可能继续覆盖画面；核对 Feature 事件及顺序，按目标决定是否暂时排除并恢复。
- RenderGraph 全局纹理在帧末归还池，跨帧回读可能得到 UnityBlack；在同帧经 Debug 显示到相机目标，再捕获请求自己的 RT。
- PNG 是显示证据；HDR 可能被裁切，颜色管理 / 量化影响数值。需要线性 HDR / alpha 精确验证时，另取 float RT 数据并注明格式，不从 PNG 反推原始 HDR。
- 回读整张 RT，打开生成图检查尺寸、全画幅、方向和目标是否入镜。上下翻转需依据有方向性的画面确认，不能默认补 FlipY。非空像素只作冒烟检查，不能证明视觉质量。

## 恢复与交付

- `finally` 恢复相机 targetTexture、活动 RT、改过的相机 / Feature / 材质设置与 Active；释放临时纹理、材质、对象，关闭自己创建的预览场景。
- 保留用户原场景、dirty 状态与原 Play 状态；不调用 `SaveAssets` / 保存场景来收尾，避免顺带写回其他流程的设置。
- 展示实际生成的完整 PNG，列出关键对照条件、实际运行 / 编译范围及未验证项。截图服务于用户观察，不能把截图成功当作效果通过。
