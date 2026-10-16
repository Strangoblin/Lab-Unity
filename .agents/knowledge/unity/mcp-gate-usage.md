# MCP 门禁工具速查（unity-gate server）

> **`Assets/Mine/` 写入必须走 `write_gated`、删除走 `delete_gated`、移动/改名走 `move_gated`**。
> 原生 Write/Edit 已被 settings deny，且连带命中 Bash（`rm`/`mv`/`cp`、写入型 `sed` 被拦，只读 `sed -n` 放行；MCP 调用与子进程不受该规则约束）。会话内工具名带前缀 `mcp__unity-gate__*`。
> **权威来源**：`.mcp/README.md`（架构、判定理由、规则数据）。**本页只做操作速查，不重复理由。**

## 工具

| 工具 | 作用 | 何时 |
|------|------|------|
| `gate_status()` | 配方 + 工作目录 + 已过门禁 + 审计 | **任务第一步** |
| `gate_set_recipe(name)` | 声明模式：Production/Research/Experiment/Debug/Minimal/Quick | 任务第一步 |
| `gate_set_workspace(path)` | 声明**本次任务的工作目录**（写/删/移边界） | 任务第一步；未指定先问用户 |
| `gate_request_workspace(path, reason)` | 申请扩展工作目录（**只登记，不授权**） | 需动边界外文件时 |
| `gate_pass(gate_id, ...)` | 通过 `g_entry` / `g_knowledge` | 各一次 |
| `gate_reset()` / `gate_list()` | 清空状态 / 列全部门禁与配方 | 切任务 / 不确定时 |
| `script_list()` | 列出 scripts/roslyn/ | 需要脚本决策时 |
| `write_gated` / `delete_gated` / `move_gated` | 写 / 删 / 移的**唯一通道**；`moves=[{"from","to"}]`；删目录传 `recursive=True` | 对应操作时 |

## 标准流程（详见 `../../../skills/auto-manager/SKILL.md` 的 [G0]–[G3]）

```
gate_set_recipe → gate_set_workspace → gate_pass(g_entry) → gate_pass(g_knowledge) → write_gated
  模式声明     →      任务边界       →      身份       →   知识证据（须命中真实文件）  →   全量内容，可连续写
```

## 当前档位（2026-10-09 降级后）

| 档位 | 检查 | 行为 |
|---|---|---|
| **block（硬闸）** | `scope.path`、`scope.workspace`、`norm.shader-decl` / `norm.cs-type-decl` / `norm.region-added`、`placement.root.new`、`deletion.*`、`move.presafety`、**`ceremony.knowledge_resolvable`（编造文件名）** | 违规 → DENIED |
| **warn（放行 + `warnings`）** | `ceremony.recipe_gate`（链未过）、`ceremony.knowledge_high_priority`（漏申报）、`norm.divider-added`、`placement.category`、`placement.root.existing` | 放行并记账 |

含义：**未过配方链也能写**（携带 `ceremony.recipe_gate` warning、`recipe` 记 `null`）—— agent 自主性档位，观察期一个任务周期，
质量下滑即升回 block；硬闸（作用域/工作目录/规范/落点/删除移动安全/编造文件名）不变。被放宽的检查仍留 warning 与 `friction.jsonl`（降级不静默）。

## 工作目录边界（任务级）

`Assets/Mine/**` 的写/删/移须落在 `gate_set_workspace` 声明的目录内（`tmp/` 暂存不受限）；边界**只能收窄**；
移动**两端**都要在边界内。未声明 → `WORKSPACE_NOT_SET`；越界 → `WORKSPACE_VIOLATION`。
扩展：`gate_request_workspace(<目录>, <原因>)` 登记 → **由用户** `workspace approve <id>`。
**agent 不得自行 approve/reject**（hook C3 封闭 + 写入摩擦账本）。

## g_knowledge

- **先真实读取**再申报 `status="COMPLETE"` —— 声明条目会被解析校验，不是仪式
- 高优先级必读（两者都申报）：`unity/standard/shader/shader-structure.md` + `unity/standard/script/script-structure.md`；参考实现用项目相对路径声明
- 编造文件名 → `G15_UNRESOLVED_FILE` **仍 DENIED**（防幻觉未随仪式降级）

## DENIED 响应解读

| error | 含义 | 处理 |
|-------|------|------|
| `WORKSPACE_NOT_SET` | 未声明工作目录 | 先 `gate_set_workspace(<用户指定目录>)` |
| `WORKSPACE_VIOLATION` | 超出本次工作目录 | 改回边界内；确需扩展走 `gate_request_workspace` + 人工批准 |
| `WORKSPACE_NOT_ALLOWED` | 工作目录本身越出基础作用域 | 只能声明基础作用域内的目录（如 `Assets/Mine/**`） |
| `NORM_VIOLATION` | 违反结构规范或文件落点 | 按 `violations[].id` / `.source` 修正后重试 |
| `G15_UNRESOLVED_FILE` | 声明条目解析不到真实文件 | 对照 references/ 或项目路径修正（防编造的硬闸） |
| `MISSING_REASON` | delete / move / 申请扩展没给原因 | 补 `reason`（必填，进审计） |
| `DELETION_UNSAFE` | 删除不可恢复 / 目录形状不成立 | `tracked-dirty` 先提交或还原；`guid-referenced` 先解除引用；`directory-not-empty` 加 `recursive`；`symlink` / `protected-root` 改走明确目标 |
| `DELETE_FAILED` | 预检后路径被改动或 I/O 失败 | 整批零改动或部分完成见 `deleted`；重新核对后再执行 |
| `MOVE_UNSAFE` | 移动预检不通过 | `TARGET_EXISTS` 要替换走 write_gated；`GUID_TAKEN` 先解除占用；`CHAINED_MOVE` 拆批 |
| `BAD_MOVE_SHAPE` | `moves` 元素缺 `from`/`to` | 传 `[{"from": ..., "to": ...}]` |
| `NO_RECIPE` / `GATE_NOT_PASSED` | **仅 `ceremony.recipe_gate` 为 `block` 时**出现 | 当前已放宽为 warn，正常见不到；见到即策略被收紧，按 `missing` 补过链 |
| `GATE_NOT_IN_RECIPE` / `INVALID_*` | 调了退役门禁 / 名字拼错 | 模式走 recipe，脚本分类走注解；名字对照 `gate_list` |

## CLI 对等通道（Codex / MCP 工具表冻结时）

```bash
python3 .mcp/validation/gate_cli.py <write|delete|move|workspace|status|report> ...   # delete 支持 --recursive/--dry-run
python3 .mcp/validation/check_norm.py <file>          # 单文件规范自查（不写盘）
```

`write_gated.py` / `delete_gated.py` / `move_gated.py` 是等价兼容入口。与 MCP 同一判定核
（`validation/judgement.py`）、同一错误码；状态读 `.mcp/state.json`。退出码 `0` 通过 / `1` 被拦 / `2` 用法。

## 提醒

- **为什么要有 CLI**：MCP 工具表按会话冻结（服务器跑新代码但工具调不到，须 `/mcp` 重连）；三条操作都有 CLI，冻结时都不再断。
- **状态语义**：CLI 只读 `.mcp/state.json`，MCP 服务端内存态独立 —— 刚跑过测试（末尾 `gate_reset` 清文件）时 CLI 可能报 `NO_RECIPE` 而 `gate_status` 正常。
- **审计有意不对称**：写入只在会话内存（最近 100 条）；删除 / 移动另落盘 `.mcp/{deletes,moves}.jsonl` —— 删除不留产物、移动丢的是「哪个变成了哪个」，日志是唯一痕迹。
- **策略可调**（`.mcp/policy.json`）：agent **不自行改级别**；疑似误拦交 meta-developer 复核，行为疑问用 `.mcp/tests/run_isolated.py` 验证。
