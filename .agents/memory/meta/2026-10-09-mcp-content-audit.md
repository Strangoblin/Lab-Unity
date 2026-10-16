---
name: mcp-content-audit
description: MCP 内容查重/过期/归属审计 — 修 9 处，列 4 组待确认删除；新增两平台 hook 漂移检查（带控制组）
date: 2026-10-09
---

# 2026-10-09 — MCP 内容审计：重复 / 过期 / 归属

用户要求：「整理 mcp 内容，看是否有重复、过期、归属不对」。按 P3 + 体系诊断清单做了一轮取证式盘点。
**删除类改动一律未执行** —— 项目 AGENTS.md 要求「列出精确清单并取得人工确认」，清单见答复正文。

## 重复（已修）

| 重复 | 处置 |
|---|---|
| `mcp-gate-usage.md` 自己声明「本页不重复」却重复了 README 的 v2 设计块、6 条规则表、删除/移动摘要 | 删重复（判定理由归 README），154 → **81 行**；每个文件回归单一角色：README=架构与理由、usage=操作速查、enforcement-policy=控制律 |
| `.mcp/README.md` 的 `## Codex 对等`（3 行）被 §传输无关 + §使用 取代 | 删节，内容并入传输节 |
| `.mcp/README.md` §使用 与 §传输无关 各列一遍 CLI | §使用 收敛为唯一命令清单（含 gate_cli 全子命令） |
| `meta-architecture.md` §框架演进 与 `enforcement-policy.md` §5 程序 重叠（调松紧/人工确认/E5/判据） | 规则文件只留**规范条目 + 指针**，程序细节归 reference（该节同时补「两平台同源文件须逐字同步」） |
| DENIED 码表 `MISSING_REASON` **同一 code 两行**（申请扩展 / delete-move 各一行） | 合并；并把 `NO_RECIPE`/`GATE_NOT_PASSED` 标注为「仅策略为 block 时出现」 |

## 过期（已修）

| 过期内容 | 处置 |
|---|---|
| `friction_report.py` 的 E5 程序里**退出码写反**（"exit 0 = 有提案；exit 1 = 健康"） | 改为 lint 风格实际语义（exit 1 = 有提案待办）——这是最危险的一处：照它做会把「有提案」读成「健康」 |
| 活动文档 7 处仍称当前系统为「后果验证 v2」 | 改为 v3（`modes/{experiment,production,research}`、`SKILL.md`、`norms.py` docstring、`test_recipes.py` docstring + print） |
| `capabilities/knowledge.md` 称「生产模式强制阻断」「未全读 → 阻断进入 P2」 | 与 2026-10-09 降级矛盾 → 改为 RELAXED 放行+记账、编造文件名仍 DENY |
| `project_paths.py` 注释称整个咨询层「实际不可达」 | **不准确**：`CATEGORY_BASE`/`VALID_CATEGORIES` 仍被 `check_placement` 用 → 注释改为精确区分死/活 |
| `script_library.py` 登记 4 个旧路径脚本（tmp/.reusable/） | 它们**读不到**（`script_exists` 只查 roslyn）→ 死数据，移除并留说明 |
| `.mcp/README.md` 里错位的孤立说明「（以上为仓库根执行；本节其余示例沿用 .mcp/ 内执行的旧写法）」 | 归属错（讲的是 §使用 却挂在 check_api_refs 节下）→ 删 |
| `gate_cli.py` 用法串笔误 `workflow:` → `workspace:` | 修 |
| `.mcp/tmp/test_prod.shader`（tracked，2026-08-07 建库当天留下的 25B 测试产物，无任何引用） | **待确认删除** |

## 归属（待决，未动）

`validation/check_api_refs.py`（300 行）是 meta 维护 `templates/` 的自查工具，自己声明「不属门禁链」，
却住在 `.mcp/validation/`。meta-developer 无 scripts/ 目录，无更合适的家 → 暂留并如实记账，作决策项上报。

## 机制加固（新增）

两个 `guard-bash.sh`（Claude / Codex）**逐字一致**（C1/C2/C3 红线各写一遍）—— 平台各存一份是必需的，
但**没有任何东西守着它们不漂移**（同类问题见 2026-09-21 codex-opencode-go skill 漂移）。故：

- 两文件加**相同**的互相指针注释（保持逐字一致，不引入差异）
- `.codex/tests/06_architecture/verify.py` 新增 `platform_hook_drift` 检查，并入 `PHASE7_BASELINE`（默认模式也失败）
- **控制组实测**：注入一行漂移 → `platform_hook_drift: 2` + FAIL；还原 → PASS（不是静默空转的检查）

## 删除执行（用户确认「按顺序修复」后）

| 组 | 执行 | 通道 | 结果 |
|---|---|---|---|
| D1 | `.mcp/gates/{g_mode,g_script,g_file,g_plan,g_web_search}.py` 5 个死模块 | **直接删**（`.mcp/**` 属 meta 作用域，绕过 unity-gate；以测试验收） | `gates/` 只剩 `g_entry` / `g_knowledge`；`GATE_REGISTRY` 里这 5 条移除 `module` 键（保留 `name`/`requires`/`retired`） |
| D2 | `project_paths.py` 咨询层：`resolve_target_dir()` + `VALID_FILE_TYPES` + `VALID_CATEGORIES` + `CATEGORY_EXAMPLES` | 代码编辑 | 260 → **188 行**；`CATEGORY_BASE` 并入执行层（`check_placement` 在用）；顺带移除未使用的 `Optional` 导入 |
| D3 | `tmp/.reusable/{query_scene,organize_scene,check_pipeline}.cs` | **`gate_cli.py delete`**（`tmp/` 在门禁作用域内 → 走设计好的删除通道，得可恢复性判定 + 审计落盘） | 三个均 `tracked-clean` / `recoverable: true`，审计落 `.mcp/deletes.jsonl`；`query_framedebugger.cs` 作为唯一副本保留 |
| D4 | `.mcp/tmp/test_prod.shader` + 空目录 `.mcp/tmp/` | **直接删**（meta 作用域；该路径不在 `validate_path` 允许集内，门禁工具会误判 PATH_NOT_ALLOWED） | 建库当天残留清除 |

删除前取证：5 个模块 `tracked=True` 且全库无 `import gates.g_*`；`_load_gate()` 唯一调用点
（`gate_center.py:169`）位于「不在任何配方 → 立即返回」之后 ⇒ 永不导入。删除后残留引用复核为空
（仅剩注册表键与测试断言，属预期）。

**通道选择的判据**（值得记下）：文件在 `validate_path` 作用域内且属业务资产 → 走门禁删除通道（有判定 + 审计）；
属 `.agents/**` / `.mcp/**` 等 meta 作用域 → meta 直接操作、以交叉引用复核 + 测试验收。混用会出错：
用门禁工具删 `.mcp/tmp` 会被 `PATH_NOT_ALLOWED` 误拦。

## 归属修正：check_api_refs.py 迁出 .mcp（用户确认「迁移该文件」）

**问题**：`validation/check_api_refs.py`（300 行）是 meta-developer 维护 `templates/` 的自查工具、
自己声明「不属门禁链」，却住在 `.mcp/validation/`（门禁执行层）。首轮只记账上报，这轮执行迁移。

**落点判据**（按 `layer-conventions.md`）：`cli/` = 命令参考**文档**，`scripts/` = **可执行脚本**。
故去 `scripts/`。unity-developer 用 `scripts/roslyn/` 是按运行时命名空间分（unityctl 执行）；
本工具无此约束，单文件平铺即可。

**改动**：

| 项 | 处理 |
|---|---|
| 文件本体 | `.mcp/validation/check_api_refs.py` → `.agents/agents/meta-developer/scripts/check_api_refs.py` |
| 路径假设 | `PROJECT_ROOT` 从 2 层改 4 层（`..\..\..\..`）；**实测** `PROJECT_ROOT == 仓库根`、`Library/PackageCache` 可达（过时 API 检查未静默降级） |
| 自身 docstring | 用法路径更新 + 开头标注「不属门禁链」 |
| 新增索引 | `scripts/README.md`（镜像 `scripts/` 的索引惯例；说明与 `cli/` 的分工） |
| `.mcp/README.md` | **整节删除**（架构图行 + §模板 API 核验 + §使用 的调用行）—— 该工具已不属 `.mcp`，权威归 `template-conventions.md` |
| 引用同步 | 4 处 → 新路径：`templates/shader/{render,hlsl}/README.md`、`references/template-conventions.md`（4 处） |
| 归属清单 | `AGENT.md` 自描述加 `scripts/` 依赖；`interfaces/project-structure.md` 的 meta-developer 内容清单加 `scripts` |
| Claude 适配层 | 补 `.claude/agents/meta-developer/scripts` 软链（unity-developer 早有 `scripts`，meta 侧此前没有——新目录不补会让两侧形状不一致）|

**验证**：迁移后工具实跑 `exit 0`（16 个模板全绿）；全库残留旧路径引用为 0；无硬编码绝对路径 / 无 `.claude` 耦合；
`test_recipes.py` + `test_knowledge_paths.py` PASS；`06_architecture/verify.py` PASS（default + strict：
断链 0 / 绝对路径 0 / hook 漂移 0）。

**一处自查方法的坑**（值得记）：我的临时断链脚本把带 `#fragment` 的链接误报为断链
（`.agents/.../templates/standard/shader/README.md` 实际存在）—— 仓库权威检查 `verify.py` 会剥锚点，
判 0 断链是对的。**结论以 `verify.py` 为准，别信临时脚本的一票。**

## 同步清单

`.mcp/README.md`、`validation/{friction_report,project_paths,script_library,gate_cli,norms}.py`、
`tests/test_recipes.py`、`meta DEVELOPER AGENT.md` + `enforcement-policy.md`、`rules/meta-architecture.md`、
`unity-developer/references/mcp-gate-usage.md`、`auto-manager/{SKILL.md,capabilities/knowledge.md,modes/*.md}`、
`.claude/hooks/guard-bash.sh` + `.codex/hooks/guard-bash.sh` + `06_architecture/verify.py`。

## 验证

- `.mcp/tests/test_recipes.py` 全绿；`test_knowledge_paths.py` PASS
- `06_architecture/verify.py` PASS（default + strict）：`broken_markdown 0` / `skill_drift 0` / `platform_hook_drift 0` / `phase2 0`
- 新增断链复核：改动文档零断链（改稿时自己引入过一条 `../skills/...` 相对路径错误，被复核抓出）
- 规则表重复命中数：`mcp-gate-usage.md` 7 → 1（仅顺带提及），README 为唯一权威表
