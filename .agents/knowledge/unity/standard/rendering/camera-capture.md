# 相机离屏取像事实

本 reference 留 URP 请求与证据边界，具体准备、取像、验证与恢复步骤归 [unity-render-debug 的取像流程](../../../../skills/unity-render-debug/references/camera-capture.md)。

| 事实 | 说明 |
|---|---|
| SingleCameraRequest | 请求指定相机的实际 URP 管线；输出尺寸由请求 RT 决定，独立于 Game View DPI / 窗口 |
| 相机类型 | Feature 可按 Game / Preview / SceneView / Overlay 分类；Renderer 索引不能单独证明执行 |
| Camera.CopyFrom | 不复制 UniversalAdditionalCameraData；从 SceneView 复制还可能保留 SceneView cameraType |
| 单相机边界 | 不能把单相机输出等同于完整 Camera Stack 或 Overlay UI |
| 时域影响 | 请求可触发 RenderGraph / 相机回调并推进历史，不自动等于 Game View 的同一帧 |
| PNG 口径 | 显示/量化证据；HDR、线性值与 alpha 精度需另用合适 RT / 回读 |

共享取像源码由技能持有；项目特有相机与材质由任务输入提供。既有实测条件见 [MaskBlur 记录](../../../../memory/unity/2026-10-09-maskblur-stratified-darkspots.md)。
