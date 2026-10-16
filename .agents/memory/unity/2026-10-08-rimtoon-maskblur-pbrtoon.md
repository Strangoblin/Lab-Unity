---
name: rimtoon-maskblur-pbrtoon
description: RimToon 退役与独立 MaskBlur / PBRToon 接入及用户验收
date: 2026-10-08
---

# RimToon 退役与 PBRToon 遮罩柔光

状态：已完成，用户于 2026-10-08 明确确认“功能无误”。

- `1bfef8a`：新增 `Assets/Mine/Shaders/PostProcess/MaskBlur/`，PBRToon 新增专用 `LightMode="MaskBlur"` Pass，`_MaskBlurWeight` 默认 1、0 不参与，PC_Renderer 正式接入。
- `0011167`：删除旧 RimToon 及隐藏 `.backup_v1_pre_unity6/`，共 49 个文件。24 个资源 GUID 没有目录外引用。共享 BlurFunction.hlsl 和 RimLightFunction.hlsl 保留。
- MaskBlur 在 AfterRenderingSkybox 收集 R8 可见主体遮罩，提取当前 HDR 场景色，Gaussian 模糊后加法合成。物体光照与边缘光由 PBRToon 自身负责；Mask Pass 共用主体几何、采样相机深度筛选遮挡、Max 混合保护可见权重，不依赖共享 Stencil。
- `_MaskBlurMaskTexture` 为原遮罩；`_MaskBlurTexture` 的 RGB 为模糊预乘场景色，A 为软化遮罩。RG 全局纹理只属于本帧、本相机，后续消费者须声明读取依赖。

## 验证

Unity 6000.3.14f1 / URP 17 / Metal：C# 编译成功，PBRToon 六 Pass 与 MaskBlur 四 Pass 的 Shader 诊断均为 0。
SampleScene 当前 Main Camera 的真实 Feature 在 384×216 受控输出下，两对象产生 449 个有效遮罩像素，柔光覆盖 591 个像素。
权重 0、LayerMask 排除、不透明物体完整遮挡三个负控均归零；Intensity=0 与关闭 Feature 逐像素一致；柔光仅增亮。
Low / Medium / High 与 Radius=0 均通过。完整场景 Play Mode 没有新增运行错误，验证后恢复 Edit Mode、临时对象、属性块和设置。

## 边界

后续透明物体会覆盖部分 Debug 输出。尚未覆盖 XR 设备及全部 MSAA / 平台组合；单采样深度筛选不等同于 MSAA 逐样本覆盖。
本记录只对应上述两个完成提交，不将后续像素 / 归一化模式讨论记为已实施。
