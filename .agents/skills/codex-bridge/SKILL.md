---
name: codex-bridge
description: Claude ↔ Codex 双边桥接接口（Claude 侧）。Codex 直接读取 `.agents/` 共享权威源，根 `AGENTS.md` 提供跨平台入口，`.codex/` 维护 Codex 专属适配契约。触发词：codex, 桥接, 同步, AGENTS.md, 双边接口。
---

# Codex Bridge — Claude 侧接口

> 双边接口架构：Claude 侧（本文件）↔ Codex 侧（`.codex/INTERFACE.md` + `.codex/AGENTS.md`）。**单一共享权威源为 `.agents/`，Codex 直接读取**；根 `AGENTS.md` 只做跨平台入口指引，不编译或复制知识正文。

## 架构

```
┌─────────────────────────────┐          ┌─────────────────────────────┐
│  Claude Code（平台侧）        │  契约对应  │  Codex（平台侧）                │
│  .claude/                   │          │  .codex/                     │
│   ├─ CLAUDE.md / settings   │          │   ├─ AGENTS.md / INTERFACE.md │
│   └─ hooks / 兼容入口         │          │   └─ agents / tests / tmp     │
│              ┌──────────────┼──────────┐                              │
│              │ .agents/     │          │                              │
│              │ 共享 SSOT    │ 直接读取  │                              │
│              ├─ agents/     │          │                              │
│              ├─ rules/      │          │                              │
│              ├─ knowledge/  │          │                              │
│              └─ skills/     │          │                              │
└─────────────────────────────┘          └─────────────────────────────┘
```

> **Codex 侧角色**：`.codex/agents/unity-developer.toml` 与 `.codex/agents/meta-developer.toml` 是运行时薄适配；正文统一读取 `.agents/agents/<role>/AGENT.md`。角色与模式由任务发起方在 prompt 中显式指定（两侧均可发起，无派发关系）。

## 核心机制：Codex 如何读 `.agents`

Codex 自动加载**项目根 `AGENTS.md`**。入口是平台无关的普通文件，内容是**读取指引**（列出 `.agents/` 权威文件清单），不搬运知识：

```
项目根 AGENTS.md ──▶ 读取 `.agents/README.md` 与角色/规则索引
Codex 任务开始时 ──▶ 读根入口 → 按清单读 `.agents/` 权威源
```

- **共享权威源**：`.agents/README.md`、`.agents/agents/<role>/AGENT.md`、`.agents/rules/`、`.agents/knowledge/`、`.agents/skills/`
- **Claude 平台入口**：`.claude/CLAUDE.md`、settings、hooks 与兼容软链
- **Codex 平台入口**：`.codex/AGENTS.md`、`INTERFACE.md`、config、hooks、tests 和 tmp

**好处**：消除编译镜像漂移；共享内容变更后，两个客户端按同一 `.agents/` 入口读取，不需要再复制一份知识正文。

## 入口同步流程（共享结构变更 → 更新适配入口）

1. 检查根 `AGENTS.md` 和 `.codex/AGENTS.md` 的入口清单是否与实际 `.agents/` 结构一致。
2. 只同步结构变化：知识源路径、读取清单、开发规则要点、角色路由。
3. 知识内容本身变更 → 不复制到平台层；客户端按共享源自然读取。
4. 验证：根入口为普通文件，关键软链无断链；必要时运行 `codex exec -C <project-root> "Reply with exactly: BRIDGE-OK"`。

## 与其他 skill 的分工

| Skill | 职责 |
|-------|------|
| **codex-bridge**（本文件） | 双边契约 + 根入口同步（低频） |
| **codex-opencode-go** | 链路搭建与排障（config.toml / auth.json / opencode CLI 接入与派活） |

联动：**入口清单变更后跑本 skill 同步**——两侧都读根入口 → 读 `.agents/` 共享源 → 带最新项目规则工作。两侧是**对等并行**的开发引擎，不存在派发关系（见 `.codex/INTERFACE.md` §3/§6）。

## 验证命令

```bash
# 根入口应为平台无关普通文件
test -f AGENTS.md && test ! -L AGENTS.md

# Codex 端能读到入口（需要 CLI 可用）
codex exec -C <project-root> "Reply with exactly: BRIDGE-OK"
```
