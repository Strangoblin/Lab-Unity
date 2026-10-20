---
name: global-unity-route
description: 用户要求将全局路由中的 Unity 任务导向 Lab/.agents：Codex 全局 AGENTS.md 新增 Unity 节，Claude 全局 CLAUDE.md / MEMORY.md 同步明文路由；正文仍唯一留在仓库，资产权限不随路由扩展。
date: 2026-10-17
---

# 全局 Unity 路由

用户要求：检查全局路由，将有关 Unity 的任务导向 `C:\Programs\Unity\Unity Project\Lab\.agents`。

## 现状核查

- `~/.codex/AGENTS.md`（Codex 全局）：只有 OpenCode Go 与项目目录，无 Unity 路由。
- `~/.claude/CLAUDE.md`（Claude 全局）：Blender + OpenCode Go，无 Unity 路由。
- `~/.claude/MEMORY.md`：与 CLAUDE.md 同构的薄索引，同样缺 Unity。
- 项目入口（Lab `AGENTS.md` → `.agents/agents/README.md`）已经完整，缺的是跨项目的全局可达性。

## 改动（机器级，不在 git 内）

| 文件 | 改动 |
|---|---|
| `~/.codex/AGENTS.md` | 新增 `## Unity` 节：读 `.agents/agents/README.md`，首次分派走 `.agents/skills/auto-manager/SKILL.md`，并声明资产 / Editor 操作留在当前工程 |
| `~/.claude/CLAUDE.md` | 新增 Unity 明文段落（同上路径） |
| `~/.claude/MEMORY.md` | 新增 `## Unity` 薄索引节 |

- 三处都只写路由，不复制角色 / 知识 / 技能正文；根仓库仍是唯一正文。
- 明确「其他 Unity 工程不得把 Lab 的资产路径当作目标」：路由的是能力库，不是项目载荷；写入 / Editor 操作仍受当前工程授权与既有门禁约束。
- 保留原文件编码与换行：Codex 侧 UTF-8 无 BOM / CRLF，Claude 侧 UTF-8 无 BOM / LF。

## 验证

- 三个文件按字节复核：无 BOM、问号替换 0、换行风格未变。
- 引用目标存在：`.agents/agents/README.md`、`.agents/skills/auto-manager/SKILL.md`。
- `python .codex/tests/06_architecture/verify.py --strict` → PASS，全项 0 finding（本机无 `python3`，用 `.mcp/.venv/Scripts/python.exe`）。

## 限制

- 全局文件不在 git 内，回退需手工删除三个 Unity 节；本文件仅记录改动。
- 路由不改变任何门禁 / 权限；跨工程读取 Lab 知识成立，但跨工程写 Lab 资产仍应被拒绝。
