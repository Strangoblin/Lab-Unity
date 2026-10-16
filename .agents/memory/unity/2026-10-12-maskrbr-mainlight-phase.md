---
name: MaskRBR 主光与角度响应简化
description: 移除手动焦点和场景 HDR 提取，采用主光颜色、峰值归一化 HG 与接收角窗；47 GPU／32 真实 URP 控制通过。
date: 2026-10-12
---

# MaskRBR 主光版本

用户要求仅考虑主光，以视线与主光方向的夹角控制放射，取消场景 HDR 高亮提取，并继承主光颜色／光强、保留强度参数。本版本替代 [v1](2026-10-12-maskrbr-v1.md) 的 Manual／HDR 提取设计；v1 记录与证据保留历史语义。正式入口见 [功能 README](../../../../Assets/Mine/Shaders/PostProcess/MaskRBR/README.md)。Git 基准仍为 `f9b3ad8`，本轮未提交。

## 最终设计

- 仅接受 URP 实际主方向光，移除 FocusMode、focusMode、manualFocus、threshold、softKnee 与 Debug Source。缺光、背光、正交相机输出零光束；Debug Mask 仍可工作。Debug Rays 保留序列化值 3。
- 管线为 ObjectMask R8 → 联合 Mask R8 → 径向遮罩 R16_SFloat → Composite RGBA16F。主 Shader 从四 Pass 减为三个（Mask=0、Radial=1、Composite=2），移除 Extract Pass／Source RT；径向中间纹理由 RGBA16F 改为单通道 R16。
- 每个最终接收像素用 inverseVP 重建视线，`mu = dot(viewDir, lightDir)`；复用 Core `HenyeyGreensteinPhaseFunction`，以 `HG(g, mu) / HG(g, 1)` 将峰值归一为 1。半角窗外为零，外侧 20% 平滑淡出。默认 halfAngle=60°、anisotropy=0.7，范围分别为 1–90°、0–0.95。
- HG／角窗在 Composite 的最终分辨率执行，位于 Low 升采样之后。R16 仅存归一化径向遮罩 × focusFade，避免低分辨率插值把角窗扩散到窗外；Debug Rays 显示最终角度响应后的灰度。
- 合成为 `scene.rgb + _MainLightColor.rgb × radialCoverage × phase × cone × intensity`，保留 scene alpha。主光颜色来自 URP 的线性颜色及真实 intensity；场景 RGB 不影响光束生成，不提取亮度门限，也不裁白，实际主光仍可产生 HDR 输出。
- source／layer、16／24／48 tap 预算、输出高度单位 length、归一化权重、越界零贡献及空间稳定 jitter 延续 v1。HashFunction 与物体遮罩 Shader 未修改。

源遮罩只决定谁发光，允许轮廓外拖尾覆盖其他不透明前景；不提供体积深度排序或 receiver 遮挡。源物体仍是 Layer 与 Renderer opaqueLayerMask 交集内的可见实体覆盖材质绘制；透明 source、alpha clipping、自定义顶点位移及特殊剔除尚未支持。

## 验证证据

Unity 6000.3.14f1／URP 17／Metal（Apple M2）。C# 编译通过，两 Shader supported=true，Pass 数 3／1，当前 ShaderUtil 消息均为零；四份更新的 C#／Shader 源各自通过结构规范检查。Shader 热重载期间曾有一次旧 Feature 调用 Pass 3 的历史日志；最终归档入口复跑没有渲染错误，不把整份历史 Console 声明为零错误。

- [GPU 报告](evidence/2026-10-12-maskrbr-mainlight-v2/shader-results-archived.json)：47/47，0 失败，全局恢复。使用实际 R8／RHalf／ARGBHalf 输出及 float 回读；独立径向、视线、HG／角窗 oracle，覆盖三种 source、三档预算、横竖屏、越界及 Clamp 负控、稳定 jitter、主光条件、g 端点、峰值归一、Y 镜像负控、场景 RGB 无关、颜色／强度缩放与 alpha。
- [真实隔离 URP 报告](evidence/2026-10-12-maskrbr-mainlight-v2/pipeline-results-archived.json)：32/32，0 失败／渲染错误，28 请求／28 begin-camera 回调。覆盖可见／完全遮挡来源、天空联合、三档轮廓外扩散、非居中主光、真实颜色／光强、参数端点、窄窗、Low 窗外零、缺光／背光／正交、jitter 和 renderScale=0.1／2 的最终尺寸绑定。pipelineRestored、activeSceneUnchanged、originalRendererUnchanged、lightSettingsRestored 全部为 true。
- GPU 单通道 RT 回读时，Metal 将未存储的 GBA 填 1；夹具修正为只对 R8／R16 的 R 断言数值，Composite 对全部 RGBA 断言，所有返回分量仍检查有限。容差 `0.002 × (1 + |expected|)`。此修正不改变生产算法。
- 完整 640×360 对照图：[baseline](evidence/2026-10-12-maskrbr-mainlight-v2/maskrbr-baseline.png)、[mask](evidence/2026-10-12-maskrbr-mainlight-v2/maskrbr-mask.png)、[rays](evidence/2026-10-12-maskrbr-mainlight-v2/maskrbr-rays.png)、[composite](evidence/2026-10-12-maskrbr-mainlight-v2/maskrbr-composite.png)。主光颜色的蓝色光束从选中红色 cube 向外扩散，未选蓝色 cube 不发光；PNG 用 `c/(1+c)` 后 sRGB 显示，不用于反推 HDR 数值。

验证源码已归入功能 `Validation~/` 并从正式入口实际复跑。尚未验证 XR、动态分辨率、全 MSAA 组合、现有多个 Feature 的排序组合或 GPU 耗时；SingleCameraRequest 不提供实际 RenderGraph 预算证据。目标场景观感需按主光和角度参数调节。

## 产物与状态

证据所有者为 unity-developer，用于复核本版本数值与形态，依赖 [正式源码哈希](evidence/2026-10-12-maskrbr-mainlight-v2/source-sha256.json) 对应版本及上述 Unity／URP／Metal 环境。[artifact-sha256.json](evidence/2026-10-12-maskrbr-mainlight-v2/artifact-sha256.json) 核验归档内容；证据被后续正式版本替代后再按精确清单评估清理。

[asset-preservation.json](evidence/2026-10-12-maskrbr-mainlight-v2/asset-preservation.json) 证明原 Mask Shader、四份原 .meta、SampleScene 与 PC_Renderer 文件相对于本轮开工保持 SHA256；未保存场景／Renderer。PC_Renderer 当前内存已有用户接入且启用的 MaskRBRFeature，本轮保留该状态，不沿用 v1 的“未接入”表述。SSL、MaskBlur 和共享库没有本轮改动。

任务目录 `tmp/sessions/2026-10-12-maskrbr-mainlight-v2/` 清理本轮生成且可重建的 staging、worker 输入副本与输出；保留 contract.md 和 fixtures/previous 三份 v1 输入，因为 MaskRBR 尚未提交，这些是当前唯一已登记的 v1 差异／回退参考，不能按可重建输出删除。其内容已与开工前哈希核对；不是完整历史验证环境。待正式 Git 回退点或其他权威历史输入建立后评估清理，未处理其他会话目录、provider 日志、gate 或 Editor 运行状态。

OpenCode 完成 README 草稿，主 agent 复核并修正最终 Composite 角度位置与验证数据；GPU 验证由主 agent 完成，真实 URP 验证源码由协作者提供并经主 agent 审查／运行。
