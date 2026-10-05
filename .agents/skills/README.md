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
