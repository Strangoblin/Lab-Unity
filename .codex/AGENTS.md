# Codex 项目入口

先读 [Agent 清单](../.agents/agents/README.md)，按任务直接取用对应角色和 memory / knowledge / skills。共享正文只在 `.agents/` 编辑。

本层只维护 config、hooks、运行时角色 TOML 与平台测试；不保留共享目录软链或正文镜像。实际工具以当前会话提供的 MCP / CLI 为准，两者共用 `.mcp` 判定核；角色适配只声明先读取清单。
