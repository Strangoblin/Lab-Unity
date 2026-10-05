# MCP 门禁工具速查（unity-gate server，后果验证 v2）

> 本项目的写删移门禁系统。**`Assets/Mine/` 写入必须走 `write_gated`、删除走 `delete_gated`、移动/改名走 `move_gated`**。
> 原生 Write/Edit 已被 settings deny，且该 deny 连带命中 Bash —— 实测：`rm`/`mv`/`cp` 被拦（含把文件**读出**到 `/tmp` 这种纯读），写入型 `sed` 被拦，只读的 `sed -n` 放行。
> 会话内工具名带前缀：`mcp__unity-gate__write_gated` 等（就是本页的工具）。
> **权威来源**：`.mcp/README.md`（架构 + 各层判定理由，本页不重复）、`.mcp/gate_center.py`、`.mcp/validation/*.py`、`.mcp/tests/test_recipes.py`。

## v2 设计（2026-08-25 收敛；2026-09-21 写删对称 + 移动复合）

门禁按**操作**建模，不是按文件：

| 操作 | 工具 | 后果验证问的问题 |
|------|------|-----------------|
| 写内容 | `write_gated` | 结构规范过不过？落点对不对？ |
| 消失 | `delete_gated` | 这个损失可逆吗？ |
| 换位置 | `move_gated` | 内容只是换了位置吗？ |

> 移动为什么单独成工具：移动 = 写新 + 删旧，而**单独看每一步判定都对**（看删除，`tracked-dirty` 硬拦正确；看写入，规范贴合放行正确），合起来却成了误拦——内容完整写到了新位置。凡是「整文件覆盖」或「整文件删除」之外的操作，先想它属于哪个工具，别拿两个拼一个。

```
gate_set_recipe → gate_pass(g_entry) → gate_pass(g_knowledge) → write/delete/move_gated
     │                │                       │                        │
  模式声明         身份确认          知识证据（声明须命中真实文件）   后果验证
```

- **链唯一**：所有配方 = `[g_entry, g_knowledge]`。g_mode/g_script/g_file/g_web_search/g_plan 已退役（调用返回 `GATE_NOT_IN_RECIPE`）
- **write_gated 原子写**：同目录隐藏临时文件（`.` 前缀，Unity 忽略）+ rename —— Editor 只看到完整新内容，无中间混合态

## 工具

| 工具 | 作用 | 何时调用 |
|------|------|---------|
| `gate_status()` | 配方 + 已过门禁 + 剩余 + 写入/删除/移动审计 | **任何任务第一步** |
| `gate_set_recipe(name)` | 声明模式：Production/Research/Experiment/Debug/Minimal/Quick | 新任务第一步 |
| `gate_pass(gate_id, ...)` | 通过 `g_entry` 或 `g_knowledge` | 各调一次 |
| `gate_reset()` / `gate_list()` | 清空状态 / 列出全部门禁与配方 | 切任务 / 不确定时 |
| `script_list()` | 列出 scripts/roslyn/ | 需要脚本决策时 |
| `write_gated(path, content, ...)` | **唯一写入通道**（门禁 + 结构规范 + 落点） | 写入时 |
| `delete_gated(paths, reason)` | **唯一删除通道**（门禁 + 不可恢复性） | 删除时 |
| `move_gated(moves, reason)` | **唯一移动通道**（门禁 + 净效果；`moves=[{"from","to"}]`） | 移动/改名时 |

## 标准流程（3 步）

```
1. gate_set_recipe("Production")   ← 新任务先声明模式（旧状态会清空）
2. gate_pass(g_entry, agent="unity-developer")
   gate_pass(g_knowledge, loaded_files="unity/standard/shader/shader-structure.md, unity/standard/script/script-structure.md", status="COMPLETE")
3. write_gated(path, content)      ← 全量文件内容；成功后可继续写（门禁状态保留）
```

## g_knowledge 铁律

- **先真实读取**对应知识文件，再申报 `status="COMPLETE"`——申报不是仪式，声明条目会被解析校验
- 高优先级必读：`unity/standard/shader/shader-structure.md`（shader）+ `unity/standard/script/script-structure.md`（C#），两者都声明
- 参考实现等代码文件用项目相对路径声明（如 `Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader`）
- 编造文件名 → `G15_UNRESOLVED_FILE` DENIED

## 后果验证规则一览

| 规则 id | 适用 | 级别 | 检测 |
|---------|------|------|------|
| `shader-decl` — 必须含 `Shader "..."` | .shader | **error** | 全量 |
| `cs-type-decl` — 必须含类型声明 | .cs | **error** | 全量 |
| `region-added` — 禁止 #region | .cs | **error** | 仅新增行 |
| `divider-added` — 纯分隔线 | .shader/.hlsl/.cs | warning | 仅新增行 |
| `placement-root` — 落点须在已知顶层根下 | .shader/.hlsl/.cs/.compute/.md | **error**（新文件）/ warning（已有） | 落点 |
| `placement-category` — `category` 注解与路径一致 | 同上且给了 `category` | warning | 落点 |

- **新增行 diff**：已有文件只查本次引入的行——历史遗留不合规不阻断新写入。**落点同源**：新文件是全新决定 → error；已有文件只是就地编辑，硬拦会堵死修正入口 → warning
- **受管扩展名只收自撰代码与文档**；`Assets/Mine` 的顶层根只到一层，规则照**现实布局**写（扫全部 202 个受管文件零误报）。判定理由见 `.mcp/README.md` §写入后果验证
- error → `NORM_VIOLATION` DENIED，`violations[]` 带 `id`/`source`；warning → 放行，响应携带 `warnings`

**删除**（`deletion.py`）：`tracked-clean` 放行 / `tracked-dirty` **阻断** / `untracked` 放行并标注不可恢复 / `.meta` GUID 仍被引用 → 阻断。整批 all-or-nothing，`reason` 必填，审计落盘 `.mcp/deletes.jsonl`。

**移动**（`moving.py`）：预检零改动（不覆盖目标、GUID 不得已被声明、批内不得链式）；**`tracked-dirty` 的源放行**——这正是它存在的理由；`.meta` 成对搬运保 GUID；不复跑内容规范。执行 write-all → delete-all，审计落盘 `.mcp/moves.jsonl`。完整判定表见 `.mcp/README.md` §移动后果验证。

## 注解（非阻塞，记录审计）

`mode`（缺省取配方）、`script_decision`（复用脚本库校验）、`file_type`/`category`/`effect`。
后三项中 `category` 现在会与路径比对（不符只 warning）；`file_type`/`effect` 仍为自由文本。

## DENIED 响应解读

| error | 含义 | 处理 |
|-------|------|------|
| `NO_RECIPE` | 没声明模式 | 先 `gate_set_recipe` |
| `GATE_NOT_IN_RECIPE` | 调了退役门禁 | 模式走 recipe，脚本/分类走 write_gated 注解 |
| `GATE_NOT_PASSED` | 写入被拒 | `missing` 列出缺的门禁，补过再写 |
| `G15_UNRESOLVED_FILE` | 声明条目不存在 | 对照 references/ 或项目实际路径修正 |
| `NORM_VIOLATION` | 违反结构规范或文件落点 | 按 `violations[].id`/`.source` 修正后重试 |
| `MISSING_REASON` | delete/move 没给原因 | 补 `reason`（必填，进审计） |
| `DELETION_UNSAFE` | 删除不可恢复或有残留引用 | `tracked-dirty` 先提交或还原；`guid-referenced` 先解除引用 |
| `MOVE_UNSAFE` | 移动预检不通过 | `TARGET_EXISTS` 要替换走 write_gated；`GUID_TAKEN` 先解除占用；`CHAINED_MOVE` 拆成两批 |
| `BAD_MOVE_SHAPE` | `moves` 元素缺 `from`/`to` | 传 `[{"from": ..., "to": ...}]`（元素非对象更早被 schema 层拒） |
| `INVALID_RECIPE` / `INVALID_GATE` | 名字拼错 | 对照 gate_list |

## Codex 对等通道

```bash
python .mcp/validation/check_norm.py <file>   # exit 0 = 通过; exit 1 = 有 error 违规（全量检查）
```

Codex 无 MCP 通道（write/delete/move 均只有 Claude 侧持有），产出交 Claude 合入。

## 提醒

- 门禁状态持久化于 `.mcp/state.json`（进程内 + 落盘双份）——server 进程空闲重启后自动恢复。遇 `NO_RECIPE` 先 `gate_status()` 确认再重走链
- **Bash 通道（删除 / 移动）**：`delete_gated.py` 与 `move_gated.py`，与 MCP 工具同一道门禁、同一套判定与执行。**为什么需要**：Claude Code 的 MCP 工具表按会话冻结——服务器跑新代码但工具调不到，需 `/mcp` 重连。**注意状态语义**：CLI 只读 `.mcp/state.json`，而 MCP 服务端的内存态独立——若刚跑过测试（末尾 `gate_reset` 清了文件），CLI 会报 `NO_RECIPE` 而 `gate_status` 看着正常，重过一遍链即可
- **审计有意不对称**：`write_gated` 只在会话内存（最近 100 条）；`delete_gated` / `move_gated` **另落盘**（`.mcp/deletes.jsonl` / `moves.jsonl`）——删除不留产物、移动丢的是「哪个变成了哪个」，日志是唯一痕迹
- 错误/行为疑问先跑 `.mcp/tests/test_recipes.py`（All tests passed 为准）
