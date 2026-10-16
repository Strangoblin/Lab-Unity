# Hermes-agent Skill 结构规范

> 来源：[Hermes Agent Docs](https://hermes-agent.nousresearch.com/docs/developer-guide/creating-skills)
> 用途：meta-developer 创建/审查 skill 时的参照标准。

---

## Skill 目录结构

```
skills/<category>/<skill-name>/
├── SKILL.md          # 必填：YAML frontmatter + 指令
├── references/       # 按需读取的补充文档
├── templates/        # 输出格式模板
├── scripts/          # 辅助脚本
├── examples/         # 参考输出样例
└── assets/           # 补充文件
```

---

## SKILL.md 结构模板

```markdown
---
name: skill-name
description: Use when <trigger>. <one-line behavior>.
version: 1.0.0
---

# Skill Title

## Overview
一段话：做什么、为什么

## When to Use
- 触发条件 1
- 触发条件 2
- "Don't use for:" 排除条件

## Quick Reference
常用命令或 API 速查表

## Procedure
逐步指令（agent 跟随执行）

## Common Pitfalls
已知失败模式 + 修复方法

## Verification Checklist
- [ ] 每个步骤的可检查完成标准
```

---

## 渐进式披露（3 级）

| 级别 | 函数 | 返回 | Token 成本 |
|------|------|------|-----------|
| L0 | 列出所有 skill | name + description | ~3k tokens |
| L1 | 查看 skill | 完整 SKILL.md | 按需 |
| L2 | 查看 reference | 具体文件 | 按需 |

---

## 关键设计原则

| 原则 | 说明 |
|------|------|
| **Description 以 "Use when..." 开头** | model 通过 description 判断何时 invoke |
| **写完要可验证** | 每个步骤说清楚"怎么知道做完了" |
| **失败路径比成功路径重要** | 写清楚"如果失败怎么办" |
| **不写空洞建议** | 去掉"be careful"、"be thorough"等无操作语义的短语 |
| **一处一义** | 同一概念不在文件中多处重复 |
| **拆分长文件** | SKILL.md 超过 ~800 行时拆到 references/ |

---

## Skill vs Tool 决策

| 用 Skill | 用 Tool |
|---------|--------|
| 指令 + shell 命令 + 已有工具 | 需要自定义 Python 集成 |
| 文本处理、文件操作、API 调用 | 需要管理 API key、二进制数据 |
| | 需要精确执行保证 |
