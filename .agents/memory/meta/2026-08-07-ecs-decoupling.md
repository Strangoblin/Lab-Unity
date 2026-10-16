---
name: ecs-decoupling-refactor
description: .claude 架构 ECS 式解耦 — Mode(编排)/Capability(能力)/Routing(路由) 三层分离
date: 2026-08-07
metadata:
  type: project
---

# .claude 架构 ECS 式解耦

## 动机

Capability 层存在跨层泄漏：compile.md/runtime.md/cleanup.md 直接引用 agents/ 路径，experiment.md 全内联 107 行无 capability 引用，cleanup.md 硬编码 scripts/roslyn/ 具体文件名。

## 三层架构

| 层 | 职责 | 能引用 | 不能引用 |
|----|------|--------|---------|
| Mode（编排） | 门禁序列 + @capabilities 引用 | @capabilities, agents/ | 不嵌入 capability 逻辑 |
| Capability（能力） | 决策分支树 | rules/, 领域路径 | agents/, 具体文件名 |
| Routing（路由） | mode 名, agent 名 | SKILL.md, CLAUDE.md | 不嵌入 domain 逻辑 |

## 修复内容

- compile.md → "暂停，报告 mode 层处理退出"（删除 agents/ 引用）
- runtime.md → 只保留 rules/ 引用
- cleanup.md → 删除 agents/ 引用 + 硬编码脚本路径
- experiment.md → 107→84 行，0→5 @capabilities 引用
- 新增 capabilities/web-search.md（WebSearch+Plan 分支树）
- production.md 门禁格式统一为 @capabilities 引用

## 关联

- 与 Unity Gate MCP Server 形成 .claude(脑) + .mcp(骨架) 并行架构
- MCP 工具名不在 .claude/ 中出现（CLAUDE.md 仅一行路由）
- 门禁分支树模式已记入 harness-patterns.md
