---
name: opencode-go-route-all-agents
description: 用户要求确保 OpenCode Go 的使用路由到达所有 agent：在本机 macOS 重建 2026-10-17 缺失的全局薄路由 skill 与明文入口，并在 Unity Lab 角色清单及两个 AGENT 中加入委派路由；正文仍唯一留在仓库，其他项目经全局入口覆盖。
date: 2026-10-26
---

# OpenCode Go 路由到达所有 agent（macOS 重建）

用户要求：确保 `opencode go` 的使用路由到了所有 agent。

## 现状核查（本机 macOS）

2026-10-17 的全局薄路由只落在 Windows 机器；当前机器缺：

- `~/.claude/skills/opencode-go/`、`~/.codex/skills/opencode-go/` 不存在（两个运行时的全局 skill 发现通道为空）。
- `~/.claude/MEMORY.md` 不存在；本机全局 memory 是 `~/.claude/memory/GLOBAL.md`。
- `~/.claude/CLAUDE.md` 与 `~/.codex/AGENTS.md` 有正文路径，但两侧 `agents/default.md` 只有委派政策、没有手册入口。
- Unity Lab 两个角色 `meta-developer` / `unity-developer` 与 agent 清单都没有 opencode 路由。

正文 `Unity/Lab/.agents/skills/codex-opencode-go/SKILL.md` 本身可用；本机 `ocw` 冒烟 `ocw -m opencode-go/deepseek-v4.1-flash "Reply with exactly: OK"` 退出码 0。

## 改动

### 全局（不在 git）

| 路径 | 形态 |
|---|---|
| `~/.claude/skills/opencode-go/SKILL.md` | 薄路由 skill（新建），与 Codex 侧逐字节相同（sha256 `4e39bb35…`） |
| `~/.codex/skills/opencode-go/SKILL.md` + `agents/openai.yaml` | 同上；Codex 全局发现 |
| `~/.claude/CLAUDE.md` | 全局技能表补薄路由位置；说明 `~/.claude/skills/` 只允许薄路由 |
| `~/.codex/AGENTS.md` | 正文路径下补薄路由 skill 位置 |
| `~/.claude/agents/default.md` / `~/.codex/agents/default.md` | 小任务委派节补正文与薄路由入口 |
| `~/.claude/memory/GLOBAL.md` / `~/.codex/memory/GLOBAL.md` | 委派节补正文路径；跨项目经验补本机重建记录（两文件仍逐字节相同） |

### 仓库

- `.agents/agents/meta-developer/AGENT.md`：取用节补「委派」行（非体系文件的小任务 → `opencode-orchestrate`；接入 / 排障 → `codex-opencode-go`）。
- `.agents/agents/unity-developer/AGENT.md`：同上（边界明确的小任务）。
- `.agents/agents/README.md`：清单后补所有角色共用的小任务委派路由（委派不改变门禁与验收责任）。

## 验证

- `.codex/tests/06_architecture/verify.py --strict` → PASS；`agents_broken_markdown_links=0`、`absolute_path_hits=0`、`skill_drift_files=0`、`agent_ownership_errors=0`、`architecture_symlink_errors=0`。
- 全局 skill 两侧逐字节一致（sha256 `4e39bb35…`）；两平台 `memory/GLOBAL.md` 逐字节一致。
- 本会话已发现 `opencode-go` skill（运行时技能列表出现），说明发现通道生效。
- `ocw -m opencode-go/deepseek-v4.1-flash "Reply with exactly: OK"` → 退出码 0、输出 `OK`。

## 边界与后续

- 其他项目（Lab-T / AmazingEditor / Robot/Bot）的角色文件未逐个改：全局 `~/.claude/CLAUDE.md`、`~/.codex/AGENTS.md`、薄路由 skill 与 global agent 对每台会话生效，符合 2026-10-17「其他项目直接引用正文、不另存副本」的裁决。若需要项目内明文条目，按项目路径规则另加相对链接。
- 手册末尾「本机（Windows）现状」是 2026-10-17 另一台机器的快照；本机 macOS 按正文其余各节执行，薄路由已注明。
- 回退：删除全局新增 skill / 入口行；仓库 `git revert` 对应提交。
