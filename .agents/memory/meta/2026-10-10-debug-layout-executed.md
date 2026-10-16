---
name: debug-layout-executed
description: Debug 目录候选确认执行后同步共享 skill 与活动 CLI 入口
date: 2026-10-10
---

# Debug 布局执行后的体系同步

用户确认精确清单后，unity-developer 已执行 Assets 迁移。实际文件、GUID、Editor 编译 / PNG 和恢复证据统一见 [执行记录](../../unity-developer/memory/2026-10-10-debug-resource-migration.md)，本文件只登记体系侧变化。

- [unity-render-debug](../../../skills/unity-render-debug/SKILL.md) 与 unity-developer/cli/roslyn.md 的活动 Feature 路径改为 `Assets/Mine/Special/Debug/DebugOutputFeature.cs`。
- [布局 reference](../references/debug-resource-layout.md) 保留确认过的旧来源表供回退核对，状态改为完成；references README 索引同步。
- 新 Assets README 引用共享 skill，流程不在 Assets 或 CLI 再复制；Claude / Codex 沿用同一 SSOT 发现入口。
- 历史 dated memory 保持原快照；没有修改 MCP 判定、策略或平台 hooks。
- 独立只读链接检查及旧路径扫描通过；架构 strict 与 diff 检查在最终收尾再次核验。
