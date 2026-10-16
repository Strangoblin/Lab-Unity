---
name: architecture-init
description: .claude 7 层闭环架构初始化 + meta-developer agent 创建
date: 2026-07-28
metadata:
  type: project
---

# 架构初始化

## 变更

- 全局 `CLAUDE.md` — 更新路由 + lifecycle 感知
- 全局 `memory/GLOBAL.md` — 用户全局上下文
- 全局 `agents/default.md` — 默认 agent 路由
- 全局 `platforms/terminal.md` + `ide.md`
- 全局 `cli/commands.md` + `memory/INDEX.md`
- 项目 `CLAUDE.md` — 项目身份 + C1-C7 + 循环生命周期图
- 项目 `agents/unity-developer.md` — Unity agent 宪法 + 退出条件 + 完整性门禁
- 项目 `agents/meta-developer.md` — 体系维护 agent
- 项目 `platforms/unity-editor.md` + `terminal.md`
- 项目 `cli/unityctl.md` + `roslyn.md`
- 项目 `scripts/roslyn/` — 3 个 C# 脚本
- 项目 `learnings/` — error-patterns + safety
- 项目 `memory/` — 迁移自全局 projects/

## Agent 层状态

| Agent | 状态 |
|-------|------|
| unity-developer | ✅ 宪法 + 会话收尾 + 自描述 |
| meta-developer | ✅ references + memory + tools |
