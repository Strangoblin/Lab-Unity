---
name: agent-architecture-cutover
description: 2026-09-04 agent 架构解耦收尾 — Codex 交接 P0-P3 执行：MCP live 切流验证、Claude 发现链验证、.codex 适配层切流、skills 软链方案裁决。
date: 2026-09-04
---

# Agent 架构解耦收尾（2026-09-04）

Codex 完成 Phase 2-4(共享层迁移 + MCP 切流)后交接 Claude 验证与收尾。本文记录 P0-P3 执行结论、稳定格式与遗留待办。

## 稳定 knowledge ID 格式(最终)

- `unity/<canonical-relative-path>` → 根 `.agents/agents/unity-developer/references/<canonical-path>`
- `rules/<canonical-relative-path>` → 根 `.agents/rules/<canonical-path>`
- basename 仅作兼容输入;**重复 basename 必须拒绝**(`AmbiguousKnowledgePath`),不再按扫描顺序取第一个
- 高优先级对:`unity/standard/shader/shader-structure.md`、`unity/standard/script/script-structure.md`
- 契约测试:`.mcp/tests/test_knowledge_paths.py`;解析实现:`.mcp/validation/knowledge_paths.py`

## MCP live 服务重载方式

- 启动:`.mcp.json` 注册 `uv run python server.py`,**每客户端会话独立进程** → 重启 = 重开会话/客户端
- 本会话实测:新进程已加载 `.agents` 解析;`gate_pass(g_knowledge)` 返回的 `resolved_details[].canonical_relative_path` 全部以 `.agents/agents/unity-developer/references/` 开头,无旧 `.claude` 路径
- Codex 侧持久化服务返回旧路径 = 进程未重载;修复方式 = 重启进程,不是 gate_reset

## Claude skills 兼容方案(裁决 + 已执行)

- 现状:`.claude/skills/` 保留 9 组**实体副本**(非软链),6 个文件与 `.agents` 漂移;`.agents` 侧为 Phase 3 裁决后较新内容;Claude CLI 不可用,无法外部探针,本会话即发现权威
- 方案一(直接发现 `.agents/skills/`)不成立:平台无自定义 skill 根机制
- 方案三(镜像)= 现状,漂移源头
- **裁决:方案二 — `.claude/skills/<name>` 逐个改为相对软链 → `.agents/skills/<name>`**,发现入口不变、正文单源、漂移归零
- **2026-09-04 同日执行**(人工确认后,提交 `fdf9106`):32 个 tracked 副本删除 + 9 条相对软链;文件集双向一致核对通过;`git rm` 遗留空 references 目录与嵌套软链已清理(教训:`git rm` 后目录仍存时 `ln -s` 会把软链建进目录内部,应先确认目录清空)
- 验证:strict 全绿(drift 6→0、absolute 1→0);**重启会话后最终确认(2026-09-04):9/9 项目技能进入发现清单,其中 7 个(auto-manager/codex-bridge/dirtybitgames-unity-editor/dwsy-project-planner/sickn33-unity-developer/unity-editor/unityctl-plugins)全局 `~/.claude/skills/` 无同名实体 → 只能经项目软链发现,机制实证通过**
- 注意:`codex-opencode-go`、`codex-orchestrate` 与全局 `~/.claude/skills/` 同名物理目录并存,全局同名可能遮蔽项目软链,不能作为项目链独立证据;另有 `~/.agents/skills/` 全局侧 5 项 vs `~/.claude/skills/` 3 项(`xthelper-filter`/`xthelper-skills` 无发现入口),全局迁移未完成,不属本仓库范围
- 回滚 = `git revert fdf9106`

## Codex 适配层切流(Phase 6 完成)

- `.codex/AGENTS.md`、`INTERFACE.md`:薄适配入口,统一"根 AGENTS.md → `.agents/`";删除"直接读 .claude/"叙述
- `.codex/agents/*.toml` ×2:262/249 行内嵌正文副本 → ~40 行运行时薄适配(正文指向共享 AGENT.md),`.Codex` 大小写错误清零
- `auto-developer.md`/`exec-developer.md`:自举读取与命令路径切到 `.agents/`
- `verify.py --strict` 扫描边界:排除平台配置、`.codex/tmp/`、dated memory 快照(历史快照按规则不改),保留 `.claude/skills` 副本在扫描内直至切流
- 实测:`codex_case_hits` 12→0;absolute 130→1;strict 仅剩 skill 切流项;`codex exec` 只读探针能引用 `.agents/README.md` 与根 AGENTS.md 原文

## 旧兼容层保留范围与退役条件

| 层 | 保留范围 | 退役条件 |
|----|---------|---------|
| `.claude/agents/*.md` | 相对软链 → `.agents/agents/<role>/AGENT.md`(实测发现正常) | Claude/Codex/MCP/Unity 四链验证后,人工确认 |
| `.claude/rules/*.md` | 相对软链 → `.agents/rules/*.md`(实测注入正常) | 同上 |
| `.claude/skills/*` | 9 条相对软链(实体已删,`fdf9106`) | 已退役;2026-09-04 重启后 9/9 可发现(7 个无全局同名,实证) |
| `.claude/agents/<role>/` 空目录壳 | memory/references/cli/scripts/templates 空壳 | 与软链退役同批 |

- `.claude/settings.local.json` 为机器本地(gitignored),verify 扫描已豁免
- 退役命令 `find -L .claude -type l -print` 应零输出;根 `AGENTS.md` 保持普通文件(非软链)

## 验证证据(2026-09-04,最终验收全绿)

```bash
bash .codex/tests/06_architecture/run.sh          # PASS(fixture 推进 phase7:tracked_claude 49→26、drift 6→0)
python3 .codex/tests/06_architecture/verify.py --strict  # PASS(切流后全绿:drift 0 / absolute 0 / codex_case 0)
.mcp/.venv/bin/python .mcp/tests/test_knowledge_paths.py  # PASS
.mcp/.venv/bin/python .mcp/tests/test_recipes.py          # All passed
find -L .claude .agents -type l -print | wc -l      # 0(无断链)
codex exec --sandbox read-only                        # 入口探针通过
unityctl status                                        # 见最终验收
```

- 默认 fixture 在 skills 切流后随 `fdf9106` 失效(49/6 对不上 26/0),已新增 `PHASE7_BASELINE` 闭包基线(历史 PRE_MIGRATION/PHASE3/PHASE4 保留为迁移差值说明)
- 提交:`docs(agent-architecture): mark skills cutover and strict acceptance green`(含 verify.py fixture 推进)+ `docs(agent-architecture): refresh cutover memory index row`
- 重启后探针(2026-09-04):unityctl status → Editor [+] Running / Bridge [+] Running(PID 38208)/ Connection [+] connected;四链(Claude 发现/Codex/MCP/Unity)全部闭合。
