# 项目结构参考 — Agent 架构域

> 仓库级 agent 体系的分层结构与引用关系速查。Unity 工程域（`Assets/`、`Packages/`、`ProjectSettings/` 等）不在此列，只经门禁与 `unity-developer` 角色接入。
> 本文件是长期事实参考，不是事件记录；每次结构变更后同步更新。决策与迁移记录见 `.agents/agents/meta-developer/memory/`。

## 分层模型

正文单源在 `.agents/`；平台层只做发现/适配；`.mcp/` 做执行门禁。引用方向单向，平台目录出现正文副本 = 违规。

```
根入口(AGENTS.md 普通文件 / .mcp.json)
        │
   .agents/  共享 SSOT —— 唯一可编辑正文副本
     ▲              ▲              ▲
  .claude/      .codex/        .mcp/ + 平台配置
  发现软链      薄适配入口       知识解析 + 门禁执行
```

各平台引用机制不同：

- **Claude**：`.claude/` 内相对软链（发现机制只认 `.claude/{agents,rules,skills}` 路径，软链让正文仍单源）。
- **Codex**：入口与运行时 toml 指向 `.agents` 路径（目录软链化进行中，见下）。
- **MCP**：`.mcp/validation/knowledge_paths.py` 把 knowledge ID 解析成 `.agents` 规范路径。

## 各层内容

### 根

- `AGENTS.md` — 普通文件（非软链），平台无关渐进入口。
- `.mcp.json` — MCP 注册（`uv run python .mcp/server.py`，每客户端会话独立进程）。

### `.agents/`（共享核心，165 tracked）

| 子层 | 内容 | 唯一编辑位 |
|---|---|---|
| `agents/` | 角色正文 `AGENT.md`：`unity-developer`（references/templates/cli/scripts/memory）、`meta-developer`（references/memory）；`agents/README.md` 角色索引 | `.agents/agents/<role>/` |
| `rules/` | 项目硬规则 4 个（shader-development、compute-shader、csharp-renderpass、meta-architecture）+ README | `.agents/rules/` |
| `skills/` | 共享技能唯一编辑位置（9 个）+ README | `.agents/skills/` |
| `knowledge/` | 领域路由索引（`unity/`），不复制正文 | `.agents/knowledge/` |
| `interfaces/` | 跨平台契约：`knowledge-paths.md`（知识 ID/resolver）+ 本文件（拓扑） | `.agents/interfaces/` |

### `.claude/`（Claude 适配层，26 tracked，正文零副本）

- `CLAUDE.md` — 薄入口（读根 AGENTS.md → `.agents/`）。
- `agents/` — `unity-developer.md`、`meta-developer.md` 软链 → `.agents/agents/<role>/AGENT.md`；`<role>/` 子目录（memory/references/cli/scripts/templates）软链 → `.agents` 同层。
- `rules/*.md` ×4 — 软链 → `.agents/rules/*.md`（paths 自动注入）。
- `skills/` — 9 条软链 → `.agents/skills/<name>`（实体副本已删，`fdf9106`；重启后 9/9 可发现）。
- `hooks/guard-bash.sh`、`settings.json`（+`settings.local.json` 本地 gitignored）— 平台配置，非共享内容。
- 全层无断链（`find -L .claude -type l` 零输出）。

### `.codex/`（Codex 适配层，33 tracked）

- `AGENTS.md`、`INTERFACE.md` — 薄适配入口；`SKILL.md` — codex-opencode-go 接入手册。
- `config.toml`（+bak）、`hooks.json` + `hooks/guard-bash.sh` — 平台配置。
- `agents/` — 运行时薄适配：`unity-developer.toml`、`meta-developer.toml`（~40 行，正文指向共享 AGENT.md）；不再维护 exec/auto 替代 agent。
- `tests/01_smoke … 06_architecture` + `run_all.sh`；`06_architecture/verify.py` 契约扫描（默认 fixture `phase7` 闭包基线）。
- `tmp/` — 工作文档，不入扫描与提交。
- 目录级软链 `.codex/{agents/<role>,rules,skills,knowledge,interfaces}` → `.agents` 已提交；`verify.py` 通过 `CODEX_SHARED_LINKS` 契约固定目标与断链检查。

### `.mcp/`（执行门禁，24 tracked）

- `server.py` 入口 + `gate_center.py`；`gates/`：g_entry / g_knowledge / g_file / g_mode / g_plan / g_script / g_web_search。
- `validation/`：`knowledge_paths.py`（resolver）、`norms.py` + `check_norm.py`（写入内容规范）、`deletion.py`（删除不可恢复性）+ `delete_gated.py`（删除门禁 Bash 通道）、`project_paths.py`、`script_library.py`。
- `tests/`：`test_knowledge_paths.py` + `test_recipes.py`。
- `state.json` — 门禁状态持久化（进程空闲重启不丢）；`.mcp.json` 注册每会话独立进程。
- 链：`[g_entry, g_knowledge]` → write_gated 内容规范检查 / delete_gated 不可恢复性判定；Codex 侧对等走 `check_norm.py` CLI。

## 维护规则（改结构必读）

- 共享正文只在 `.agents/` 编辑；写回 `.claude/`、`.codex/` = 违规。
- 新增内容先 grep 全库查重（同名/同主题合并或链接），不另起权威副本。
- 软链一律仓库内相对路径；平台目录内的软链本体随发现路径放置。
- 知识 ID：`domain/<canonical-relative-path>`（例 `unity/standard/shader/shader-structure.md`）；重复 basename 拒绝。详见 `knowledge-paths.md`。
- 结构变更后验证：`.codex/tests/06_architecture/run.sh`（default + `--strict`）、`.mcp/tests/` 双套件、`find -L .claude .agents .codex -type l` 零断链。
- 历史 dated memory 是当时快照，引用旧路径不改；只更新活动文档并记 dated memory。

## 退役与遗留

- agent/rules/skills 软链 = 发现机制保留，退役条件 = Claude/Codex/MCP/Unity 四链验证后人工确认（2026-09-04 四链已全绿）。
- 权威状态与决策记录：`.agents/agents/meta-developer/memory/2026-09-04-agent-architecture-cutover.md`。
- 角色更名、长文档拆分不属本阶段。
