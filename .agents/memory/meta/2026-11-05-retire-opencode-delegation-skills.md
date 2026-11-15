---
name: retire-opencode-delegation-skills
description: 退役 codex-bridge / codex-opencode-go / opencode-orchestrate / codex-orchestrate 与全局路由；移除失效 verify.py 引用；auto-manager 更名为 agent-workflow。
date: 2026-11-05
---

# 退役 OpenCode 委派技能与 agent-workflow 改名

用户裁决：当前任务流程为 Claude / Codex 单次跑完并自检，不再经 OpenCode 委派。点名
`codex-bridge`、`codex-opencode-go`、`opencode-orchestrate` 相关内容不再可用，要求整理并清除
过期 skill；同一轮要求把 `auto-manager` 目录改名为 `agent-workflow`。

## 退役与备份

| 对象 | 处理 |
|---|---|
| `.agents/skills/codex-bridge/`（13 行） | `cli.py delete` → 桶 `20261105-150656-89793d` |
| `.agents/skills/codex-opencode-go/`（411 行） | 同上 |
| `.agents/skills/opencode-orchestrate/`（40 行 + `agents/openai.yaml`） | 同上 |
| `~/.claude/skills/opencode-go/` | 移入 `~/.claude/.retired-skills/2026-11-05-opencode-routes/` |
| `~/.codex/skills/opencode-go/` | 同上 |
| `~/.agents/skills/opencode-go/` | 同上 |
| `~/.agents/skills/codex-orchestrate/` | 移入 `~/.claude/.retired-skills/2026-11-05-codex-orchestrate/` |

- 项目内删除先 `--dry-run`，`blocked: []`；实际删除 3 个目录，`restore` 可回退。
- 全局三份是薄路由，正文指向被删的项目技能；不删会留下悬空入口，因此一并移出发现路径。
- 全局 `codex-orchestrate` 原样保留会继续调用已退役的 `codex-bridge` / `codex-opencode-go`；
  用户确认全局退役后移出发现路径，并从 `~/.claude/CLAUDE.md` 的全局技能表移除该入口。
- `~/.claude/CLAUDE.md` 与 `~/.codex/AGENTS.md` 的路由行同步移除。两处是仓库外、无 git 兜底，
  被项目 hook 拒绝后按用户在确认清单中的明确授权用精确脚本替换，并回读校验。
- `~/.claude/memory/GLOBAL.md` 与 `~/.codex/memory/GLOBAL.md`（逐字节相同的两份）：删除
  「OpenCode 小任务委派（2026-10-12）」活动条目及指向已删 `codex-opencode-go/SKILL.md` 的正文路径，
  替换为「任务执行方式（2026-11-05）」：Claude / Codex 单次跑完并自检，不再委派 OpenCode。
  历史 dated 条目保留，仅在 2026-10-26 路由重建条目与 2026-10-21 排障条目中标注退役；改后两文件仍逐字节一致。
- `~/.claude/agents/default.md` 与 `~/.codex/agents/default.md` 的「小任务委派」段改为「任务执行方式」，
  删除 `opencode-go/deepseek-v4.1-flash` 调用规则与指向已删 `codex-opencode-go/SKILL.md` 的排障入口。

## 活动引用同步

- [技能索引](../../skills/README.md)：移除 3 行，历史兼容节补退役说明。
- [Agent 清单](../../agents/README.md)、[meta-developer](../../agents/meta-developer/AGENT.md)、
  [unity-developer](../../agents/unity-developer/AGENT.md)：移除委派段。
- [history.md](history.md)、[memory.md](memory.md)：追加本轮记录。
- 历史 dated memory / evidence 保留原字节，不追改其中的旧路径与旧名称。

## 失效 verify.py 引用

`.codex/tests/06_architecture/verify.py` 已于 2026-11-01 的 `ef0619e` 删除，但三处活动文档仍在引用：

- [agent-maintenance/references/maintenance.md](../../skills/agent-maintenance/references/maintenance.md)
- [structure-selfcheck/SKILL.md](../../skills/structure-selfcheck/SKILL.md)（边界、S0、S5）
- [`.mcp/README.md`](../../../.mcp/README.md) 与 [`.mcp/bin/hooks.sh`](../../../.mcp/bin/hooks.sh)

已移除或改为历史说明。当前可用替代：`inventory.py`（记忆 / 垃圾桶 / 索引）、
`run_isolated.py`（MCP 契约）、`rg` 加逐文件存在性检查（断链与软链）。不再声称存在自动架构核验入口。

## auto-manager → agent-workflow

- 目录改名 `.agents/skills/auto-manager` → `.agents/skills/agent-workflow`。
- 技能身份同步：frontmatter `name: autoagent` → `agent-workflow`，标题 `AutoAgent` → `Agent Workflow`，
  模板 README 标题同步。
- 22 个活动文档的路径与名称引用更新，包含 knowledge 契约、skill 间依赖、`structure-selfcheck`、
  `unity-editor` / `unity-render-*` / `unity-feature-reuse`、`memory/README.md` 与 `.mcp/tests/test_zones.py` fixture。
- 旧名仅保留在历史 memory、`.mcp/state` 账本与历史权限条目中。

## 验证

- 删除预检：`blocked: []`；垃圾桶 `20261105-150656-89793d` 可 restore。
- 活动 Markdown 链接核验：本次新增 / 改名目标 0 断链。另有 9 条既有“仓库根相对路径”被检查脚本
  按文件相对解析而误判，均不在本次改动范围。
- `python3 .mcp/tests/run_isolated.py --python /Users/xiaokangji/.local/bin/python3.11 ...` → 4/4 PASS，
  生产 state / audit 指纹未变。系统 `python3` 为 3.9.6，`test_trash_close.py` 使用
  `Path.write_text(newline=)`（3.10+）会报 TypeError；这是既有环境差异，不是本轮改动。
- `inventory.py --check`：垃圾桶 33 桶触发阈值，属既有存量；本轮新增的桶已在清单中登记。

## 限制与后续

- 未改 `.claude/settings.local.json` 历史权限条目、`.codex/config.toml.bak-*`、
  `.codex/tests/06_architecture/output/`（后者已忽略）。
- 全局 OpenCode Go 的 provider / 凭据配置（`~/.codex/config.toml`、`~/.config/opencode/`）未动：
  本轮只退役技能与路由，不回收运行时配置。
