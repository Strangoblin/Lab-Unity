# ScreenWarp v1 — 2026-10-19

## 授权与范围

用户先评估透视/正交/移轴/Dolly Zoom/Curved World，随后明确先做独立ScreenWarp，避免修改相机及依赖参数。深度引导的Dolly Zoom近似留后续；Curved World意指远处地平线下弯。用户本轮确认工作目录 `Assets/Mine/Shaders/PostProcess/ScreenWarp/`。

## 已交付

- `ScreenWarpFeature.cs` / `ScreenWarp.shader` / `ScreenWarpFunction.hlsl` / `README.md`，功能专属验证归 `Validation~/`。
- Barrel、Pincushion、Fisheye、Panini；共享Strength、Center、Zoom、Clamp/Color边界与SourceUV/OutOfBounds调试。零强度完全跳过；强度插值采样坐标，包括Zoom的影响。
- 固定AfterRenderingPostProcessing，一个Raster RG Pass、一张输出、一次RGBA颜色采样；不请求opaque/depth/normal，不修改Camera、几何缓冲或已有时域。
- 基线沿Weather的descriptor、源RTHandle有效视口与参数快照绑定；等比坐标调用共享TRS；Panini为URP单位距离解析简式。私有策略未扩大共享库。
- 接入 `Assets/Settings/PC_Renderer.asset` 末尾，保存显式Shader引用，默认Barrel/0.35/Zoom1/黑边/SceneView关闭，Feature map同步Local File ID。其余四个已有Feature与开关保持。

## 实际验证

- Unity6000.3.14f1 / URP17.3.0，C#刷新编译通过。Shader实际Metal单Pass绘制。
- GPU Shader：432组 / 2,082,816像素，0失败；最大绝对RGBA误差3.629923e-5。覆盖宽/竖/方、偏心、强度端点、Zoom、边界、Debug、HDR/Alpha、有效视口padding及一个禁用负控制。归档入口复跑通过。
- 真实URP隔离PreviewScene：30次SingleCameraRequest / 362断言 / 0渲染错误，四模式、透视/正交、横竖屏、主PP路径；Off=zero，Camera矩阵和参数不变；原Renderer数组/实例、Feature开关、场景dirty、活动RT恢复。归档入口复跑通过。
- 当前Main Camera640×360开/关PNG生成并完整查看；投影和姿态保持。输出仅用于观察，不作为全场景艺术效果或性能保证。
- 报告：[Shader](evidence/2026-10-19-screenwarp/shader-checks.json)、[URP](evidence/2026-10-19-screenwarp/pipeline-checks.json)。本轮PNG与草稿在 `tmp/sessions/2026-10-19-screenwarp-a91f/`。
- 一次OpenCode `opencode-go/deepseek-v4.1-flash`只读复核长时间未收敛，主会话中止（exit130）；没有采用其未完成结论，最终审查与验收由主会话及独立URP fixture完成。

## 边界与后续

- Camera Stack、XR、硬件动态分辨率、其他Feature组合及耗时未验证；首版跳过非末端Base/Overlay/Preview/Reflection/立体相机。
- 屏幕Overlay UI在最终合成，Camera/World UI与已有场景颜色一起变形；SceneView的后绘Gizmo不变形。
- GPU Picker/鼠标反向坐标尚未接入；后续深度效果须匹配变形后的颜色坐标。颜色重映射无法补充遮挡后或视锥外内容。
- 用户原SampleScene脏改动、四个未跟踪HDR资产保留；未保存场景，不调用SaveAssets。只定向保存PC_Renderer。
- 门禁沿Production，源码通过write_gated对等CLI，知识与工作区已登记；没有修改门禁策略。
