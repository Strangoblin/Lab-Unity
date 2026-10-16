---
name: codex-bridge
description: 同步 Claude / Codex 的项目入口、运行时角色路由与共享库指引，核验明文单源路径。适用于平台入口或库结构变更；provider / 认证排障使用 codex-opencode-go。
---

# 双边入口同步

1. 读取 `.agents/agents/README.md` 清单以及本次结构变更记录，核对角色和三库真实路径。
2. 同步根 AGENTS.md、`.claude/CLAUDE.md`、`.codex/AGENTS.md` 和必要运行时 TOML：入口先到 agents 清单，再直接取用需要的实体。只更新路由，不复制角色、规则、技能正文，不创建软链或发现镜像。
3. 按当前平台实际提供的 MCP / CLI 核对执行能力；两侧使用同一 `.mcp` 判定核，不能把“某平台无某工具”写成永久假设，也不借迁移调整权限。
4. 运行 `python3 .codex/tests/06_architecture/verify.py --strict`，验证活动路径、明文入口和零架构软链；确需进程读取冒烟时，在当前已授权范围选择可用 CLI。

共享内容变更而位置未变时不重编平台入口；provider / 模型 / 认证和连接排障直接取用 codex-opencode-go，本文不镜像接入手册。
