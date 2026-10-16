# Unity 执行契约

本文件统一所有模式的工作区、门禁同步和后果验证。策略档位由 `.mcp/policy.json` 与判定核决定，skill 不自行降级。

## 任务范围

- 禁止 `git stash --all`。保护已有用户工作；重大改动前按 [backup.md](backup.md) 明确 Git 基准和脏改动。
- 项目资源用 `python3 .mcp/validation/project_resources.py path <id>` 解析。`asset.root` 是资源/门禁作用域；不等于本次获准修改的全部范围。
- `[G0.5] Workspace — Path: <用户授权目录> | Source: user`：将现有明确授权的目录交 `gate_set_workspace`。没有目录授权先问用户，不自行决定。tmp 暂存沿用门禁的既有例外。
- 多工作区由 `gate_request_workspace(path, reason)` 登记；扩展须用户批准。`gate_cli.py workspace approve` 是人的通道，agent 不执行。
- 存量删除/移动按根 AGENTS.md 准备精确清单并取得确认；已明确授权的同一清单不重复确认。runner 自建可重建 fixture 按 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 自动收尾。

## 结构化决策与 MCP 同步

每个节点输出作为后续步骤输入；统一配方链为 `[g_entry, g_knowledge]`。先选定模式，任务开始仅调用一次 `gate_set_recipe(mode)`，再通过 g_entry、登记工作区、提交知识。再次初始化会清空已通过门禁，不能在 G1 或迭代中重复调用。

| 节点 | 输出 | 同步 |
|---|---|---|
| G0 | `Framework Check — Agent: unity-developer` | `gate_pass("g_entry", agent=...)`（此前已初始化一次） |
| G0.5 | `Workspace — Path: <path> | Source: user` | `gate_set_workspace(path)` |
| G1 | `Mode Selection — Mode: <mode> | Reason: <why>` | 仅输出已选模式，不重置 recipe；不调用已退役 g_mode |
| G1.5 | `Knowledge Check — Loaded: <真实已读文件> | Status: COMPLETE` | `gate_pass("g_knowledge", loaded_files=..., status="COMPLETE")` |
| G2 | `Decision: USE / CREATE reusable / CREATE tmp / NONE` | `write_gated` 的 script_decision 注解，仅记录 |
| G3 | `FileType + Category + TargetDir + Exists` | `write_gated(path, content, file_type=..., category=..., effect=...)` |

G0 / 高优先级知识漏申报当前为 warn（2026-10-09 档位）；编造文件名仍 block。
G0.5 工作区、G3 内容/落点、删除/移动安全仍是硬闸。放行 warning 不代表流程要求消失；知识漏读应补齐。
`g_mode/g_script/g_file/g_web_search/g_plan` 已退役；不尝试补跑它们。

## 操作通道 — 按域分

**Unity 受管资产**（`Assets/Mine/**` 的受管扩展名、roslyn 脚本）写入走 `write_gated`，删除走 `delete_gated`，
移动/改名走 `move_gated`；不以 write + delete 拼移动。MCP 工具表冻结时用
`python3 .mcp/validation/gate_cli.py write|delete|move ...` —— 与 MCP 共用 `validation/judgement.py`，状态仍经 gate_pass 落盘。

**体系库**（`.agents/{agents,knowledge,memory,skills}/**`）**不在门禁作用域内**，这是刻意边界，由
`.mcp/tests/test_recipes.py::test_meta_bypass_boundary` 钉住：Unity MCP 只服务 Unity 资产，不是通用文件管理通道。
角色、memory、knowledge、skill 用平台文件工具直接维护，改完按 [体系维护流程](../../agent-maintenance/references/maintenance.md) 验证。
体系路径写 `write_gated` 会得到 `PATH_NOT_ALLOWED`，这不是配置故障，不要去拓宽 `ALLOWED_PREFIXES` 来"修"它。

对受管资产不以 Bash 的 rm/mv/cp/写入型 sed/tee 绕开门禁。平台 hook 只封三条红线
（`git stash --all`、`rm -rf Assets/Mine/`、`gate_cli.py workspace approve|reject`），**并不**通用拦截 Bash 写入 ——
约束来自上面这条规则本身，不要因为以为 hook 会兜底而放宽它。

| 操作 | 后果验证 |
|---|---|
| 写 | shader-decl / cs-type-decl / region-added 结构违规拒绝；受管文件须符合 Effects / Scripts / Shaders / Special 落点；category 与路径或分隔线风格不符给 warning |
| 删 | tracked-dirty 或 GUID 被引用拒绝；untracked 放行但注明不可恢复；reason 必填、批量原子；非空目录须 recursive=True；软链和作用域根拒绝 |
| 移 | 零改动预检，不覆盖目标、不重复 GUID、不允许批内链式；源 tracked-dirty 可保留内容搬运；.meta 成对移动保 GUID；reason 必填 |

未设工作区报 `WORKSPACE_NOT_SET`，越界报 `WORKSPACE_VIOLATION`；配置解析不能消除这些检查。
Codex 规范检查为 `python3 .mcp/validation/check_norm.py <file>`。
错误疑似误拦时记录原因交 meta 体系维护，不修改 policy 为本次任务放行。
工具速查与策略事实见 [MCP reference](../../../knowledge/unity/mcp-gate-usage.md)。
