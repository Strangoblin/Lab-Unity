---
name: feature-screen-debug
description: RendererFeature 屏幕调试流程，使用真实 RenderGraph 中间 RT 和 Game View，替代 Probe/Pixel Probe
date: 2026-08-25
metadata:
  type: project
---

# RendererFeature 屏幕调试

## 决策

废弃 `Assets/Probe` 场景夹具、复制 shader、外部数学复算和 `ReadPixels` Pixel Probe 流程。后处理效果应在真实 RendererFeature 和真实场景中提供 Debug 输出，直接显示中间 RT 到相机颜色目标，然后在 Editor Game View 观察。

## Feature 契约

- Feature 提供可序列化的 Debug 枚举或开关，例如 `Off / Trace / Resolve`。
- Debug 分支复用实际 RenderGraph 生成的中间纹理，不创建第二套测试管线。
- 结构验证使用 `asset refresh`、`ShaderUtil.GetShaderMessages`、`snapshot` 和 `logs`。
- 视觉验证由人工观察 Game View。

## 工具与流程

完整 how-to（命令序列、Feature 侧约定、失败定位顺序）：[script-structure.md](../references/csharp-dev/script-structure.md)「屏幕调试方法」。
2026-08-27 起临时脚本已退役：调试独立全屏 Shader 使用固定 Feature `Assets/Mine/Scripts/Debug/DebugOutputFeature`（Inspector 指定 shader + 勾选 debug，无需 json / Roslyn）。验证后恢复 Debug=Off 和原始 Active 状态。

## HyperSpace

HyperSpace 已回到最小 Shadertoy 材质移植，不再需要 2D/3D Probe 场景或 4D SDF 诊断夹具。普通材质 shader 直接通过真实场景中的网格观察；只有后处理 Feature 才使用本流程。
