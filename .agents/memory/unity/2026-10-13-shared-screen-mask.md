---
name: 共享屏幕遮罩库与双消费者迁移
description: 公共 raw-depth／覆盖度函数与 R8 RendererList 流程；22 全图对照零误差，47 GPU／32 URP 回归通过。
date: 2026-10-13
---

# 共享屏幕遮罩首版

用户批准将 MaskBlur／MaskRBR 的共同 Mask 能力提炼为公共库，未来支持 Stencil、颜色与其他来源。本轮实现对象遮罩、深度可见性、背景判断和标量组合；Stencil／颜色保留为扩展策略。正式契约与复跑入口见 [Screen Mask README](../../../../Assets/Mine/Special/Rendering/Mask/README.md) 和 [MaskFunction](../../../../Assets/Mine/Special/HLSL/MaskFunction.md)。Git 基准 `f9b3ad8`，原有脏改动保留，本轮未提交。

## 实现

- `Special/HLSL/MaskFunction.hlsl` 提供 raw-depth 可见性、远端／清屏背景判断、max 并集、min 交集、反相和饱和相减。无资源／CBUFFER／兄弟 include，调用方提供 Core 的 UNITY_REVERSED_Z 和同域深度／容差。
- `Special/Rendering/Mask/ScreenMaskRenderer.cs` 为 `Mine.Rendering` 静态 helper，入口 CreateDescriptor／RecordObjects。保持 R8_UNorm、单采样、无深度附件、清黑、Bilinear／Clamp、输入纹理尺寸与 XR 布局；RendererList 沿用 opaque 排序、Layer 交集和有序 ShaderTags，显式读取深度 handle 与全局相机深度。
- `AllowPassCulling(false)` 保障空 RendererList 的清黑输出；返回本图／本相机帧 TextureHandle，可发布调用方自己的全局 ID，不引入长期纹理、公共全局名、缓存或 RendererFeature。
- MaskBlur 继续调用原材质 MaskBlur Pass；PBRToon 仅将相同 raw-depth 判断替换为公共调用，顶点、权重及其他材质逻辑保持。MaskRBR 继续用原覆盖材质 Shader，保留资产／菜单／GUID；联合 Pass 0 保留绑定与薄封装，复用公共描述符、背景和 max 算子。
- 两 Feature 的非 Mask 方法逐字保持；Settings、全局名、Pass 数／索引、时点、质量预算、模糊／径向、HG／角窗和材质生命周期保持。Pass／RT 配置预算没有增加，未测量 GPU 耗时。

公共输出仅表达 [0,1] 覆盖度，source／receiver 用途由消费者决定；当前两效果沿用 source，扩散可超出原轮廓。背景表示无不透明深度／远端区域，不保证绘制 Skybox。覆盖材质只保证实体 opaque，不自动继承 alpha clipping、位移或特殊剔除；这些需原材质 Pass。Stencil 需要独立深度模板附件／位分配策略，颜色筛选需要明确输入阶段与距离，离散 ID 另设格式／采样契约。

## 验证

Unity 6000.3.14f1／URP 17／Metal（Apple M2），Edit Mode 的真实 Game 相机 SingleCameraRequest。使用克隆 Renderer／URP、预览场景及显式 Shader 引用，没有保存现有场景或资产。

- [旧实现基线](evidence/2026-10-13-shared-screen-mask/baseline.json)：22/22 控制通过，生产修改前采集完整 320×180 RGBAFloat。
- [迁移对照](evidence/2026-10-13-shared-screen-mask/equivalence.json)：正式归档入口实际运行，22/22 通过，1,267,200 个 RGBA 像素最大数值误差为 0。覆盖 MaskBlur 权重 1／0.35／0、完全遮挡、空图层、两核 × 三档、半径 0；MaskRBR 三种 source、偏轴主光 Rays／Composite、缺光／背光和空选择。有限值、alpha、有效来源非空、零来源及轮廓外扩散控制全部通过。
- 两次各 25 请求／25 相机回调，无渲染错误；pipelineRestored、activeSceneUnchanged、originalRendererUnchanged、lightSettingsRestored、trackedGlobalsRestored 全部 true。全局恢复范围是登记的遮罩／深度／主光状态。
- [MaskRBR GPU](evidence/2026-10-13-shared-screen-mask/rbr-gpu.json)：47/47；[真实 URP](evidence/2026-10-13-shared-screen-mask/rbr-pipeline.json)：32/32，28 请求／28 回调，恢复与错误检查通过。
- [当前 Shader 诊断](evidence/2026-10-13-shared-screen-mask/shader-status.json)：四 Shader 均 supported=true、ShaderUtil 消息 0，Pass 数保持 4／3／1／6；修改源码规范检查通过。

容差为 `0.002 × (1 + |expected|)`，本次实际误差为零。线性 float 回读无显示映射。不声明 XR、动态分辨率、全 MSAA、多 Feature 排序、实际 RenderGraph 预算或 GPU 耗时通过。

## 归档与回退

功能源码在公共模块／原消费者目录。对照脚本及 22 份必要旧输入归入 `Special/Rendering/Mask/Validation~/`；gzip 后 Base64 编码适配受管文件通道，解码数值与原捕获字节一致。[输入哈希](evidence/2026-10-13-shared-screen-mask/baseline-input-sha256.json) 记录编码和 float 两层 SHA256。原基线报告 `.bin.gz` 名称对应归档 `.bin.gz.b64`；不能在新版重新生成基线来证明旧版等价。

证据所有者为 unity-developer，用于复核迁移及回归；依赖 [源码／输入哈希](evidence/2026-10-13-shared-screen-mask/source-sha256.json) 对应版本、固定场景与上述环境。被后续正式行为版本替代后再按精确清单评估清理。[保留检查](evidence/2026-10-13-shared-screen-mask/asset-preservation.json) 15 项相对开工 SHA256 一致，涵盖原 .meta、SampleScene、PC_Renderer 及未修改输入；[归档哈希](evidence/2026-10-13-shared-screen-mask/artifact-sha256.json) 核验报告。

任务目录 `tmp/sessions/2026-10-13-shared-mask-v1/` 的可重建 staging、worker 文档副本、原始数值捕获副本与输出已在归档核验后清理。最终复核 8 项证据哈希、55 项正式源码／输入哈希和 22 份编码／解码基线一致，副本与正式归档一致。保留 6 份独有未提交旧输入（MaskBlur Feature／README 与 MaskRBR Feature／两个 Shader／README），[contract.md](../../../../tmp/sessions/2026-10-13-shared-mask-v1/contract.md) 登记精确清单、用途及正式 Git 回退点／权威旧输入建立后的清理条件；PBRToon 原 Shader 可从登记的 Git 基准恢复。其他任务、provider 日志、gate 和 Editor 状态未清理。

两项工作目录扩展由用户批准，gate_status 确认后才合入；Assets 操作均经 gate。OpenCode 完成文档草稿，主 agent 修正精确 API、尺寸语义和最终证据；协作者提供 staged C#／HLSL 和对照脚本，主 agent 审查、合入和实际运行。
