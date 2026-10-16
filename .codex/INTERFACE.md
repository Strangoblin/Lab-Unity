# Codex 平台接口

## 路由

自动入口为根 AGENTS.md；平台入口和运行时 TOML 先指向 `.agents/agents/README.md`，再取用角色和三库。共享正文不复制，不保留发现软链；具体 provider / 模型能力以当前配置和会话为准。

## 分工

`.codex` 只持有 config / auth、hooks、运行时适配和平台测试。Claude / Codex 各自是开发引擎，使用同一共享库；按当前提供的 MCP 或等价 CLI 选择通道，不假定某平台永久没有 MCP。
实际写删移共用 `.mcp` 判定核、policy 与状态；已有工作区授权、人工批准通道及 guard-bash 保持。并行任务声明文件所有权，不能整份写回旧快照覆盖别人工作。

## 接入与产物

模型 / provider / 认证 / 链路排障取用 `.agents/skills/codex-opencode-go/SKILL.md`（唯一权威，git 留档）；双边入口同步取用 codex-bridge。这里不镜像接入手册。
临时与历史产物按 `.agents/knowledge/meta/interfaces/task-artifacts.md` 管理；新输出进入根 tmp/sessions。
