# Claude Code .claude 官方结构规范

> 来源：[Claude Code Docs](https://code.claude.com/docs/en/claude-directory)
> 用途：meta-developer 创建/检查 .claude 体系时的参照标准。

---

## 目录结构

```
.claude/
├── CLAUDE.md              # 项目指令（每个会话加载）
├── settings.json          # 权限、hooks、env、model 默认值（提交）
├── settings.local.json    # 个人覆盖（gitignore）
│
├── rules/                 # 路径限定的模块化规则
│   └── <name>.md          # YAML frontmatter + paths: 字段
│
├── agents/                # 子 agent 定义
│   └── <name>.md          # YAML frontmatter (name, description, tools, model...)
│
├── skills/                # 可复用的可调用 prompt
│   └── <name>/SKILL.md    # YAML frontmatter + 指令正文
│
├── hooks/                 # 生命周期钩子脚本
├── commands/              # 斜杠命令（legacy，优先用 skills/）
├── workflows/             # 动态工作流脚本 (*.js)
└── agent-memory/          # 子 agent 持久 memory 目录
```

---

## Agent Frontmatter 必填字段

```yaml
---
name: <kebab-case>           # 必填
description: <一句话描述>     # 必填，≤1024 chars
tools: [Read, Write, Bash]   # allowlist（省略则继承全部）
model: inherit               # 可选：haiku | sonnet | opus | inherit（不写则继承主会话）
background: true             # 后台运行（默认）
---
```

## Skill Frontmatter 关键字段

```yaml
---
name: <kebab-case>           # 必填，≤64 chars
description: <触发+行为描述>  # 必填，≤1024 chars，以 "Use when..." 开头
user-invocable: true         # 用户可手动 /name 调用
---
```

## Rules 格式

```yaml
---
paths:
  - "src/**/*.ts"
  - "**/*.test.ts"
---
# 规则内容（仅编辑匹配路径文件时加载）
```

---

## 核心原则

| 原则 | 说明 |
|------|------|
| **CLAUDE.md ≤ 200 行** | 超过则拆到 rules/ + skills/ |
| **渐进式披露** | rules 路径限定加载，skills 按需 invoke |
| **Skill body ≤ 500 行** | 细节放 references/，按需再读 |
| **Memory ≤ 200 行** | 只加载开头，超出部分需搜索 |
| **Frontmatter 起始于 byte 0** | `---` 前不能有空行 |
| **不重述 README** | link 过去，不复制 |
| **不写心愿式规则** | Claude 会逐字照做，写进规则就要执行 |

---

## 配置优先级 (高→低)

1. CLI flags (`--permission-mode`, `--settings`)
2. Project `settings.local.json`
3. Project `settings.json`
4. User `~/.claude/settings.json`
5. 系统默认
