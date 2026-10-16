---
name: codex-agent-temp-cleanup
description: Retired Codex exec/auto replacement agents and internalized then cleaned temporary work products.
date: 2026-09-04
---

# Codex 替代 agent 与临时产物清理（2026-09-04）

## 变更

- 删除 `.codex/agents/auto-developer/` 与 `.codex/agents/exec-developer/`；它们只是旧的工作流替代品，不是共享角色。
- Codex 编排改为直接指定共享 `unity-developer` / `meta-developer` 角色，并显式选择 Production / Research / Experiment 模式。
- 更新 `.codex/AGENTS.md`、`.codex/INTERFACE.md`、`.agents/skills/codex-bridge/SKILL.md`、`.agents/skills/codex-orchestrate/SKILL.md` 与 `.agents/interfaces/project-structure.md`，清除活动文档中的替代 agent 路由。
- `.codex/tests/06_architecture/verify.py` 新增 retired-path 契约，防止两个替代 agent 重新出现。

## 临时产物归档判断

以下内容已有共享 memory 或正式文档承接后清理：

- 窗口归位记录 → `meta-developer/memory/2026-09-04-fgd-window-placement.md` 与 `2026-09-04-interior-map-window-placement.md`。
- InteriorMapping 归档 → `unity-developer/memory/2026-09-04-interior-mapping-archive.md`。
- Bridge 隔离诊断 → `meta-developer/memory/2026-09-03-unityctl-bridge-host-isolation.md`。
- Agent 架构迁移计划/交接 → `meta-developer/memory/2026-09-04-agent-architecture-cutover.md` 与相关阶段提交历史。
- 截图捕获脚本、SampleScene 快照、InteriorMapping 回退包属于一次性验证/回退产物，不进入共享知识库。

`.codex/tmp/README.md` 保留作为临时目录契约；`.codex/tests/01–06` 保留作为 Codex 与架构回归测试。

## 原则

- P1：共享知识只进入 `.agents/`，不在 `.codex/tmp/` 留第二份权威正文。
- P2：平台目录只保留入口、配置、hooks、tests、tmp 和兼容适配。
- P3：临时产物按“已有归档 / 可复用知识 / 一次性产物”分类，删除前保留可追溯的共享 memory 记录。
