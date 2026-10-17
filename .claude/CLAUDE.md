# Claude 项目入口

先读 [Agent 清单](../.agents/agents/README.md)，按任务直接取用对应角色和 memory / knowledge / skills。共享正文只在 `.agents/` 编辑。

本层只维护 `settings.json` 与 `hooks/guard-bash.sh`。不保留 agents / rules / skills 发现路由或正文副本；现有权限与 MCP 门禁仍执行。接入与体系维护技能按清单取用。
