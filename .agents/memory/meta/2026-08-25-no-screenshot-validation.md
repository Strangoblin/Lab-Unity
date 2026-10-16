---
name: no-screenshot-validation
description: 2026-08-25 — 移除活动流程中的截图验证提示，视觉结果改由 Editor 人工观察
date: 2026-08-25
metadata:
  type: project
---

# 移除截图验证提示

用户明确要求项目不再采用截图验证，避免把截图误认为 Shader、场景或 RendererFeature 的必需验收步骤。

## 活动流程调整

- 活动验证只保留编译日志、运行日志、结构化 snapshot/script eval，以及 Unity Editor/Game View 人工观察。
- Feature Screen Debug 不再包含截图留档提示。
- Codex 入口和 auto-developer 流程不再建议截图。
- 历史 memory 保留原始变更记录，不作为当前流程指令。
