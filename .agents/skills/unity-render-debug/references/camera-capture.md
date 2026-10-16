# 相机离屏 PNG 流程

用于 [unity-render-debug](../SKILL.md) 的固定尺寸取像步骤。输出来自实际 URP 相机请求，不受 Game View 窗口大小、缩放或屏幕 DPI 裁切影响。

## 准备相机

- 使用已确认的 Game / Base 相机及其实际 Renderer 索引。目标 Feature 可能跳过 Preview / Reflection / Overlay 相机，不能只依据物体存在判断执行。
- 受控截图显式设定完整 `rect=(0,0,1,1)`、目标宽高比、pose / FOV / 裁剪面，并关闭动态分辨率；记录或在临时相机上设置，结束恢复。
- 隔离实验使用 `EditorSceneManager.NewPreviewScene()`，将临时网格、材质、相机与光源放入该场景，绑定 `camera.scene`；相机类型仍为 Game。`Camera.CopyFrom` 不复制 UniversalAdditionalCameraData，单独设置 Renderer、Base 类型、抗锯齿和后处理。
- 真实场景集成验证保留实际输入与 Feature 顺序。需要排除其他效果时记录并恢复；不要将隔离通过写成完整集成通过。
- 从 SceneView 复制姿态时，`Camera.CopyFrom` 也会保留 `cameraType=SceneView`（Unity 6000.3.14f1 已实测）；临时 Game/Base 请求显式设置 `camera.cameraType=CameraType.Game` 并核对实际 Renderer/Feature 执行，不能仅依据组件上的 Renderer 索引认为已经走临时管线。
- SingleCameraRequest 只渲染指定相机。Camera Stack / Screen Space Overlay UI 的整帧截图需另选适合的请求或窗口方式，不把单相机 PNG 当作完整 Game View UI。

## 执行取像

复用 [camera-offscreen-capture.cs](../scripts/camera-offscreen-capture.cs)。权威脚本只维护通用取像；按需复制到会话 `staging/`，定制 Main 的相机、输出路径、尺寸，或在临时驱动中调用 `OffscreenCameraCapture.Capture`。默认 Main 使用 Camera.main、640×360，并创建不覆盖的任务输出路径；实际相机配置仍由调用方确认。

| 阶段 | API / 条件 |
|---|---|
| 请求支持 | `RenderPipeline.SupportsRenderRequest(camera, request)`；不支持时停止并报告当前管线 |
| 目标 | 显式宽高的 2D ARGB32 RT，单采样、无 mip / dynamicScale；PNG 使用 RGBA32 Texture2D |
| 渲染 | `RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt })` |
| 回读 | 保存 `RenderTexture.active`；切到请求 RT；`ReadPixels(new Rect(0,0,width,height),0,0)`，再 Apply |
| 落盘 | `EncodeToPNG` 写调用方指定可写路径；不要覆盖既有对照文件 |
| 恢复 | finally 恢复相机 targetTexture / 活动 RT，销毁 Texture2D，Release 并销毁 RT |

unityctl 的 `script execute` 运行带 `Script.Main()` 的完整文件；`script eval` 用方法体片段，并以 `-u` 显式导入所需命名空间。不要把类声明直接粘进 eval 方法体。签名及宿主连接处理见 [unityctl CLI](../../unity-editor/references/cli/unityctl.md#script-execution)。

```bash
unityctl script execute tmp/sessions/2026-10-10-render-debug-example/staging/capture.cs
```

没有 Editor 连接时只做静态 / 离线编译检查，不能声称捕获完成。当前共享脚本是显示 PNG 路径；线性数值实验另用 float RT / float 回读，记录显示映射与色彩空间。

## 验证与记录

- 读取真实文件确认 PNG IHDR 宽高等于请求尺寸；打开整图看方向、全画幅和被测物体。工具返回路径或分辨率不代替文件证据。
- 对照固定输出尺寸和 aspect，记录相机 / Renderer、Debug 模式、参数、后处理、采样与 RT 格式；按需要记录 seed / 迭代，避免两轮截图各自换曝光。
- 本次请求可执行 RenderGraph / 相机回调并更新其他效果的历史；不能把重复请求默认当作时域效果的同一帧。涉及历史或时间时按目标明确推进 / 冻结策略。
- 临时相机、预览场景、Feature 状态及外部材质由驱动负责恢复；通用 helper 不保存资产、不改变 Feature 配置。
- MaskBlur 的近远 / 质量对照曾通过该链路获取完整 640×360 图；具体数据与条件留在 [实验记录](../../../memory/unity/2026-10-09-maskblur-stratified-darkspots.md)，不将特定遮罩阈值内置到通用截图脚本。
