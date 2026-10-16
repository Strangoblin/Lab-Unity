---
name: interior-screen-debug-retired
description: 按用户指示删除 InteriorMapping 专用全屏验证 Shader，通用 Debug 区保持工具归属
date: 2026-10-12
---

# InteriorMapping 专用验证退役

用户明确指出 `Assets/Mine/Special/Debug/InteriorMappingScreenDebug.shader` 属于不需要保留的专用验证；已在本轮列出精确两文件清单并沿用用户删除授权。

- 经 delete_gated 删除该 Shader 及同名 `.meta`（GUID `3736fe151a1e2425899953bf2da34343`）。Assets 内无资源 GUID、Shader 菜单名或完整路径的运行引用。
- 删除前核对：正文与 Git HEAD 的迁移前 PostProcess/Debug 路径相比仅菜单前缀不同，meta 逐字节相同；可按旧 Git 路径恢复，无实体备份包。
- 通用 Debug 区保留 DebugOutputFeature / DebugOutput.shader；README 移除专用 Shader 条目，布局 reference 区分迁移历史与当前退役状态。历史 dated memory 与已留存 PNG 保持原快照。
- 图像验证流程仍为 [skill](../../../skills/unity-render-debug/SKILL.md) → [取像细节](../references/standard/rendering/camera-capture.md) → [共享 helper](../scripts/roslyn/camera-offscreen-capture.cs)，不依赖已删 Shader。
- 本轮 Editor 未运行，只做删除、引用与文档检查；没有操作场景 / Renderer 或执行新 GPU 验证。

资源清单、引用及文档链接检查通过；其它并发工作未回退。
