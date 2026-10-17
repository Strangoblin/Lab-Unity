# Codex 项目入口

先读 [Agent 清单](../.agents/agents/README.md)，按任务直接取用对应角色与 memory / knowledge / skills。共享正文只在 `.agents/` 编辑；平台层不保留正文副本、软链或发现镜像。

## 平台边界

- 本层只保留：`config.toml`、`hooks.json`、`hooks/guard-bash.sh`、`agents/*.toml` 薄运行时路由，以及 `tests/` 契约。
- 实际写删移共用 `.mcp` 判定核、policy 与状态；已有工作区授权、人工批准通道和 Bash 守卫仍执行。
- 临时产物归根 `tmp/sessions/`；历史产物按 [task-artifacts](../.agents/knowledge/meta/interfaces/task-artifacts.md) 处理。

## 接入与维护

- 模型 / provider / 认证 / 链路排障：[codex-opencode-go](../.agents/skills/codex-opencode-go/SKILL.md)。
- Claude / Codex 双边入口同步：[codex-bridge](../.agents/skills/codex-bridge/SKILL.md)。
- 体系维护：[agent-maintenance](../.agents/skills/agent-maintenance/SKILL.md)。

本文件是 Codex 的唯一明文路由；不再保留 `INTERFACE.md`、`SKILL.md` 或 `agents/README.md` 兼容壳。
