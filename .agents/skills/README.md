# Shared skills

`.agents/skills/` 是项目共享 skills 的唯一编辑位置。`.claude/skills/` 与 `.codex/skills` 仅保留指向共享源的软链；不得维护实体副本。

## Phase 3 drift decisions

| Skill file | Decision |
|---|---|
| `auto-manager/AutoMode.md` | 采用 `.claude` 较新内容，路径归一到 `.agents`。 |
| `auto-manager/capabilities/knowledge.md` | 采用较新的分类与高优先级规则，路径归一到角色 references/templates。 |
| `codex-bridge/SKILL.md` | 改为 `.agents` 共享 SSOT + Claude/Codex 薄适配。 |
| `codex-orchestrate/SKILL.md` | 保留 Claude 编排与 MCP 边界，但知识源改为 `.agents`。 |
| `codex-opencode-go/SKILL.md` | 消除 Claude/Codex 品牌漂移，改用项目 Agent。 |
| `opencode-orchestrate/SKILL.md` | 通过本机 OpenCode CLI 将有明确边界的任务派发给 `deepseek-v4.1-flash`。 |
| `dwsy-project-planner/SKILL.md` | 消除平台品牌绑定，使用 active project architect。 |

Phase 5 负责验证 Claude 对 `.agents/skills` 的发现能力，并决定是否将旧 skill 路径改为软链或生成壳。

## 退役

| Skill | 日期 | 原因 |
|---|---|---|
| `codex-orchestrate` | 2026-09-21 | **前提失效**——该 skill 整体是「Claude 派发给 Codex」的编排约定（codex exec 模板、沙箱、验证流程、任务书）。现 Claude 与 Codex 是**对等并行**的两个开发引擎，不存在派发/从属关系（见 `.codex/INTERFACE.md` §3/§6）。原内容分流：链路与配置 → `codex-opencode-go`；双边契约与入口同步 → `codex-bridge`。项目内实体与 `.claude/skills/` 软链已删（`git rm`，可回溯）；`~/.claude/skills/` 全局副本待删（被 settings 的 `Bash(rm -rf ~/*)` deny 挡住，需人工执行） |
