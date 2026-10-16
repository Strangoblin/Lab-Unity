# Unity Gate MCP Server

> 后果验证门禁系统。Claude Code 通过 `.mcp.json` 自动加载。

作用域仅限 `unity-developer` 的 Unity 业务写入。`meta-developer` 与 `.agents/.mcp/.claude/.codex` 体系维护直接绕过本 MCP，改由 meta 架构测试验收。

可写目录：`Assets/Mine/` 全部子目录、`.agents/agents/unity-developer/scripts/roslyn/` 和 `tmp/`。路径按真实目录边界校验，拒绝相似前缀与软链越界；知识和内容门禁仍然生效。Claude/Codex 共用 `validation/project_paths.py` 的路径规则。

## 架构

```
Gate Center (gate_center.py)      ← 注册表 + 配方表 + 状态追踪
  └── RECIPES (配方)               ← 会话模式声明；链统一 [g_entry, g_knowledge]
        └── Gates (gates/*.py)     ← check(ctx) → pass | fail
Validation (validation/)           ← script_library.py + norms.py + deletion.py + moving.py
  └── project_paths.py             ← 路径作用域 + 受管文件落点
  └── norms.py                     ← 结构规范数据化（写入后果验证规则）
  └── deletion.py                  ← 不可恢复性判定（删除后果验证规则）
  └── moving.py                    ← 净效果判定（移动后果验证规则）
  └── check_api_refs.py            ← 模板 API 核验（不属门禁链，见末节）
```

三层解耦：**中心不在意门禁内容，配方不在意门禁实现，门禁只在意自身逻辑。**

**v3 自观测与策略数据化（2026-10-08）**：v2 的判定与严格度都是代码常量，于是「框架该松还是紧」
只能靠人判断，升级永远晚于 agent 能力。v3 拆成四层：

```
policy.json            ← 执行策略（数据）: check → block | warn | off
judgement.py           ← 判定核（唯一实现）: write / delete / move + policy 应用 + 记账
server.py / gate_cli.py← 传输薄壳: MCP 工具 与 统一 CLI 调同一判定核
friction.jsonl         ← 传感器: 每次判定留一行信号
friction_report.py     ← 聚合 + 提案: 调级 / 退役 / 修管线（人工确认后才改 policy.json）
```

动机、能力契约与控制律见 `.agents/knowledge/meta/enforcement-policy.md`。
要点：**缺省 policy = v2 行为（零回归）**；策略只放宽不收紧；安全类检查标 `retirable: false` 不退役。

**v2 收敛（2026-08-25）**：门禁从「流程仪式 + 自报告」收敛为「知识证据 + 内容后果验证」；g_knowledge 校验真实文件，write_gated 校验内容，Codex 经 check_norm.py 对等接入。

**写删对称（2026-09-21）**：`delete_gated` 与 `write_gated` 共用同一道门禁（`can_write`）与同一套路径作用域（`validate_path`）。删除没有「内容」可查，所以后果验证改为判定**不可恢复性**（见下节）。

**移动 = 复合面（2026-09-21）**：`move_gated` 是两者的复合。移动 = 写新 + 删旧，而**单独看每一步判定都是对的**——看删除，`tracked-dirty` 意味着「删了只剩历史版本」，硬拦正确；看写入，内容规范贴合，放行正确。合起来却成了误拦，因为内容完整写到了新位置。所以移动验的是**净效果**：内容是否只是换了位置（见下节）。

## 工具

| 工具 | 作用 |
|------|------|
| `gate_set_recipe(name)` | 声明模式: Production / Research / Experiment / Debug / Minimal / Quick |
| `gate_set_workspace(path)` | 声明**本次任务的工作目录**（`Assets/Mine/**` 写/删/移的边界） |
| `gate_request_workspace(path, reason)` | 申请扩展工作目录 —— **只登记，不授权**（批准是人工动作） |
| `gate_pass(gate_id, **ctx)` | 通过 g_entry / g_knowledge |
| `gate_status()` | 当前状态 + 写入 / 删除 / 移动审计 |
| `gate_list()` | 列出所有门禁 + 配方（含退役标记） |
| `gate_reset()` | 重置 |
| `script_list()` | 列出 scripts/roslyn/ |
| `write_gated(path, content, ...)` | 门禁 + 结构规范 + 文件落点检查通过才放行写入（注解非阻塞记录） |
| `delete_gated(paths, reason, recursive=?, dry_run=?)` | 门禁 + 结构/不可恢复性检查通过才放行删除；空目录默认可删，非空须 `recursive=True`（批量，all-or-nothing） |
| `move_gated(moves, reason)` | 门禁 + 净效果检查通过才放行移动（批量，`.meta` 成对，保 GUID） |

**三条操作都有 CLI 通道**（`validation/gate_cli.py` + 三个兼容入口 `write_gated.py` / `delete_gated.py` /
`move_gated.py`），判定与记账全在 `judgement.py` —— 见下节。

## 配方

Production / Research / Experiment / Debug / Minimal / Quick 均使用唯一链 `g_entry → g_knowledge`，配方只声明模式。
**该链是否阻断由 `policy.json` 的 `ceremony.recipe_gate` 决定**：当前为 `warn`（未过链可写 + warning + 记账，观察期），
故配方现在只影响**审计与注解**（`recipe` 字段），不再挡写入。收紧回 `block` 即恢复硬闸。

## 门禁

| ID | 名称 | 前置 | 参数 |
|----|------|------|------|
| g_entry | 框架入口 | — | agent |
| g_knowledge | 知识加载证据校验 | g_entry | loaded_files, status（声明必须解析到真实文件） |
| g_mode | 模式确认（退役） | g_entry | → 模式走 gate_set_recipe |
| g_script | 脚本决策（退役） | g_knowledge | → 走 write_gated `script_decision` 注解 |
| g_file | 文件放置（退役） | g_knowledge | → 走 write_gated `file_type/category/effect` 注解 |
| g_web_search / g_plan | 联网搜索 / 方案设计（退役） | — | 不在任何配方 |

退役门禁调用返回 `GATE_NOT_IN_RECIPE`（带退役原因提示），出现在任何配方即测试回归。
**退役条目不带 `module`** —— 它们永不进配方，`_load_gate` 到不了，对应模块文件已于 2026-10-09 删除；
保留注册只为让 `gate_list` 能回答「这个名字退役了、该走哪条路」。要复活：写回 `gates/<name>.py` + 加回 `module` 与配方条目。

## 写入后果验证（validation/norms.py + project_paths.py）

写入执行两类检查，合成一次判定（`NORM_VIOLATION`，看 `violations[].id` 区分来源）。
**表里的「级别」是检查自身的固有级别；实际生效级别取自 `policy.json`（只能放宽，不能收紧）。** error 级阻断，warning 级提示放行：

| 规则 | 适用 | 级别 | 检测 |
|------|------|------|------|
| shader-decl — 必须含 `Shader "..."` | .shader | error | 全量 |
| cs-type-decl — 必须含类型声明 | .cs | error | 全量 |
| region-added — 禁止 #region | .cs | error | 仅新增行 |
| divider-added — 分隔线统一 ═ 风格 | .shader/.hlsl/.cs | warning | 仅新增行 |
| placement-root — 受管文件必须在已知顶层根下 | .shader/.hlsl/.cs/.compute/.md | error（新文件）/ warning（已有） | 落点 |
| placement-category — category 注解与路径一致 | 同上，且给了 `category` | warning | 落点 |

**新增行 diff**：对已存在文件只检查本次引入的行，历史遗留不合规不阻断新写入。落点校验同源：
新文件是全新决定 → error；已有文件只是就地编辑，硬拦会堵死修正入口 → warning。

**受管扩展名只收开发者自撰的代码与文档**（`.shader`/`.hlsl`/`.cs`/`.compute`/`.md`）。
`.mat`/`.prefab`/`.png`/`.asset`/`.shadergraph`/`.meta` 等由 Unity 导入或工具产出，不在列——
把导入类型也列进来，白名单会随 Unity 每加一种资产类型而失效，最终变成误拦。

**顶层根只到一层**（`Effects`/`Scripts`/`Shaders`/`Special`）。规则照 `Assets/Mine` 的**现实布局**
写：`Scripts/` 下既有 `Editor/` `Shaders/` `Noises/` 等嵌套子目录，也有直接躺在根下的
`TestAuto.cs`；按深度或「必须进效果子目录」的规则，第一次运行就会误拦既有文件。
本层只拦「扔在 `Assets/Mine` 根下或扔进 `Data/` 这类非代码区」——扫全部 202 个受管文件，零误报。

规范来源：`agents/unity-developer/references/urp-shader-lib/shader-structure.md §7`、`csharp-dev/script-structure.md §6`。

## 删除后果验证（validation/deletion.py）

写入门禁的对称面。删除无内容可查，验的是**不可恢复性**——删文件真正不可逆的损失，是丢掉 git 恢复不了的东西：

| 工作区状态 | 含义 | 处置 |
|---|---|---|
| `tracked-clean` | 已入库且工作区干净 | 放行 —— `git checkout` 可恢复 |
| `tracked-dirty` | 已入库但有未提交改动 | **硬拦** —— 删了只剩历史版本 |
| `untracked` | 未入库（含 .gitignore 命中） | 放行，结果标注 `recoverable: false` |
| `missing` | 不存在 | 跳过（批量清单可重复执行） |

分类用三次 git 调用（不随文件数增长）：`ls-files` 定已入库集合，`diff --name-only`（工作区 / 索引）定改动集合，全部 `-z --no-renames` 保证解析不错位。

另有 Unity 专属检查：删 `.meta` 前用 `grep -rlF` 查其 GUID 是否仍被 `Assets/` 下其他文件引用，命中即硬拦（`guid-referenced`）。删资产却留下同名 `.meta`（或反之）只报 `orphan-meta` / `orphan-asset` 警告，不阻断——成对删除是调用方的责任，工具不擅自扩大删除范围。

### 目录（2026-10-10 补齐）

`delete_gated` 同时处理文件与目录，语义按「这次删除请求本身成不成立」和「内容删了回不回得来」分层：

| 情形 | 处置 |
|---|---|
| 空目录 | 默认可删（`rmdir`，不承载内容） |
| 非空目录 | 须 `recursive=True`（CLI `--recursive`）；否则 `directory-not-empty` 拦截 |
| 目录子树 | 展开后**逐个子项**仍走 dirty / GUID 判定 —— `recursive` 只放开目录形状，不绕过不可恢复性 |
| 软链（自身或祖先） | `symlink` 拒绝，不跟随；不会删到链接之外 |
| 非普通文件（FIFO 等） | `unsupported-type` 拒绝 |
| 作用域根（`Assets/Mine` / `tmp` / `roslyn`） | `protected-root` 拒绝 |

执行前**整批复核**（inode / 内容 hash / 目录子项名），预检后出现的 `symlink` 替换、新增子项、同尺寸同 mtime 的内容替换都会被识破并**零改动**返回。删除用 `unlink`/`rmdir` 逐条进行，深层先删、同层文件先于目录，`rmdir` 天然拒绝预检后新增的子项。

展开出的路径会计入同一次批量判定（`blocked` 一并列出），所以「父目录 + 子项」的重复请求不会重复删除，`recursive` 也不会把作用域边界撑大：**请求本身照旧先过工作目录检查**。

整批 **all-or-nothing**：任一路径被拦则整批不执行，`blocked` 列出原因。

**两个通道，同一道门禁**——判定同源（都走 `deletion.evaluate` + `deletion.plan`），不允许各判一套：

| 通道 | 入口 | 用途 |
|---|---|---|
| MCP | `mcp__unity-gate__delete_gated` | 会话内常规调用 |
| Bash | `python3 .mcp/validation/delete_gated.py --reason "..." [--recursive] [--dry-run] <paths...>` | MCP 工具表未刷新时、或脚本化批量删除（含目录） |

CLI 的门禁状态读 `.mcp/state.json`（与 `can_write` 同语义）——**只消费已通过的链，不提供过门禁的入口**；`--reason` 必填，`--recursive` 放开非空目录，`--dry-run` 只判定不执行。退出码 `0` = 成功 / `1` = 被拦或失败 / `2` = 用法错误。

**删除审计落盘** `.mcp/deletes.jsonl`（JSONL，不入库，`gate_status` 另显最近 20 条）：写入有产物本身即记录，**删除没有——日志是唯一痕迹**。两个通道都追加，含 `channel` 字段区分，并记 `argv` 便于溯源。目录删除额外记 `requested_paths`（原始请求）/ `expanded_paths`（展开后全量）/ `recursive` —— 删掉一棵树的痕迹只此一份。

> 存在的理由：`Assets/Mine/` 的直接 `Write`/`Edit` 被 settings deny（逼写入走 `write_gated`），而 Claude Code 的路径规则会**连带命中 Bash**——`rm`/`mv` 即便在 allow 列表里也被拦。MCP 调用与 Python 子进程都不经该路径匹配，故这一对通道是 `Assets/Mine/` 下可用的删除路径。

## 移动后果验证（validation/moving.py）

`write_gated` 与 `delete_gated` 的**复合面**。移动 = 写新 + 删旧，两个原子操作合成一个意图，
而单独看每一个判定都对，合起来却误拦。所以这里验的是**净效果**：内容是否只是换了位置。

`plan()` 预检（零改动），任一被拦则整批不执行：

| 判定 | 含义 |
|---|---|
| `SAME_PATH` / `MISSING_SOURCE` / `TARGET_EXISTS` | 源目标相同 / 源不存在 / 目标已存在（移动**不覆盖**，要替换走 `write_gated`） |
| `DUPLICATE_SOURCE` / `DUPLICATE_TARGET` | 同一源搬两次 / 多个源抢同一目标（**逐对报**，重复的每一对都是一次独立意图） |
| `CHAINED_MOVE` | 一个的 `dst` 是另一个的 `src`（对称判定，链条成员各报一条）——拆成两批才无歧义 |
| `META_TARGET_EXISTS` | 目标的同名 `.meta` 已存在 |
| `GUID_TAKEN` | 该 GUID 已被别的 `.meta` **声明**（同 GUID 两份 = Unity 报错，与位置无关） |

**批次级检查排在逐文件检查之前**：链式歧义是「这一批的形状」问题。若排在存在性之后，
「B 是 A 的目标又是 C 的源」会先报成 B 不存在——而 B 不存在恰恰是链条造成的，
那个诊断会把人引向错误的方向。

**`.meta` 成对搬运**：Unity 资产的身份是 `.meta` 里的 GUID，不是路径。正文搬走而 `.meta` 不搬，
引用会断。故正文与 `.meta` 一起走，搬前查 GUID 是否已被占用。

**不复跑内容规范检查**：移动不引入新内容，正文逐字节原样搬运。对一次纯搬位置重跑 norms，
会把「历史遗留不合规但本来就存在」的文件拦下——那是限制而非规范。

执行顺序 **write-all → delete-all**，拒绝与失败都有方向：

| 阶段 | 失败后果 |
|---|---|
| 预检不过 | 零改动（拒绝的代价必须是零） |
| 写阶段失败 | 回滚已写的新文件，源一个没删（等于没发生） |
| 删阶段失败 | 保留旧副本，内容没丢（安全方向），`failed` 里列出 |

**审计落盘** `.mcp/moves.jsonl`：新位置有产物、旧位置没产物，**丢失的正是「哪个变成了哪个」**
——这一条在文件系统里查不回来，故与删除同样落盘。记录含 `carried_dirty`（搬走了哪些带未提交
改动的源）——放行它们正是本工具存在的理由，但这件事必须在审计里留痕。

**两个通道，同一道门禁**——与删除同理：判定同源（都走 `moving.plan` + `moving.execute`），
不允许各判一套。

| 通道 | 入口 | 用途 |
|---|---|---|
| MCP | `mcp__unity-gate__move_gated` | 会话内常规调用 |
| Bash | `python3 .mcp/validation/move_gated.py --reason "..." <from> <to> ...` | MCP 工具表未刷新时、或脚本化批量移动 |

**为什么需要 Bash 通道**：Claude Code 的 **MCP 工具表按会话冻结**——服务器进程跑的是新代码，
工具却调不到，需 `/mcp` 重连。删除通道当初就是为此补的，移动同理。CLI 的路径必须成对给出
（`源 目标 源 目标 …`），支持 `--dry-run`；退出码 `0`=成功 / `1`=被拦 / `2`=用法错误。

## 传输无关的判定核（v3）

**判定只有一份实现**：`validation/judgement.py`。MCP 工具与 CLI 都是薄壳，策略只在核内应用，
信号只在核内落账 —— 通道自己不许判级别、不许记账（否则口径分叉）。

| 操作 | MCP 工具 | CLI（Bash 通道） |
|---|---|---|
| write | `write_gated` | `write_gated.py --path <p> --from <file>` / `gate_cli.py write` |
| delete | `delete_gated` | `delete_gated.py --reason R [--recursive] <paths...>` |
| move | `move_gated` | `move_gated.py --reason R <from> <to> ...` |

**为什么三条都要 CLI**：Claude Code 的 MCP 工具表按会话冻结——服务器跑新代码，工具调不到，
须 `/mcp` 重连。v2 为此给删除、移动各补了 CLI，而**写入没有**，于是写入成了唯一会因冻结而断掉的
操作，Codex 也就没有等价通道。v3 补齐后，三条操作在两个传输上等价（`PATH_NOT_ALLOWED` 等错误码逐码一致）。

**会话门禁状态**：CLI 只**消费** `.mcp/state.json`（与 `can_write` 同语义），不提供过门禁的入口 ——
先用 MCP `gate_set_recipe` + `gate_pass` 过链。policy 把 `ceremony.recipe_gate` 降为 warn 时，
CLI 的 `state_gate_from_file` 同步放宽（同一策略，不各判一套）。

**Codex 对等**：Codex 无 MCP 工具，走上面同一条 CLI 通道（同一判定核、同一错误码）；
只读单文件自查用 `check_norm.py`（不写盘）。

## 工作目录边界（任务级）

基础作用域（`validate_path`）回答「框架允许写哪里」；**工作目录**回答「这次任务只许动哪里」——
是**收窄**，不是放宽：`gate_set_workspace(path)` 的 path 必须先落在基础作用域内。

| 情形 | 结果 |
|---|---|
| 目标在 `workspace.applies_to`（默认 `Assets/Mine/`）之外，如 `tmp/` | 不受边界约束（暂存区） |
| 未声明工作目录 + `workspace.required=true` | `WORKSPACE_NOT_SET` |
| 目标不在已授权 roots 内 | `WORKSPACE_VIOLATION`（`violations[].id` = `workspace-root`） |
| 工作目录本身越出基础作用域 | `WORKSPACE_NOT_ALLOWED` |
| 移动 | **两端**都要在边界内（搬进/搬出都改了边界外内容） |

**多工作区 = agent 提出、人工批准**，机制上分权:

| 角色 | 能做什么 | 做不了什么 |
|---|---|---|
| agent | `gate_request_workspace(path, reason)` 登记请求 | 不能批准 —— 该工具**不授权**，且 `gate_cli.py workspace approve/reject` 被平台 hook（C3）对 agent 封闭 |
| 用户 | `gate_cli.py workspace approve <id>` / `reject <id>` | —— |

批准走 CLI（磁盘），MCP server 进程经 `workspace_view()` 合并磁盘 roots，**批准后立即对运行中的会话生效**（不合并就会「批了仍被拒」）。请求 / 批准 / 拒绝都落 `friction.jsonl` 审计。

## 摩擦账本与策略（v3）

- **传感器** `telemetry.py` → `.mcp/friction.jsonl`：每次判定一行（`ok` / `denied` / `warn` / `dry_run` / `error`），
  含 `channel` / `op` / `signal` / `check` / `level`。删除/移动的**审计**（`deletes.jsonl` / `moves.jsonl`）仍按 v2 落盘，二者互补：
  审计记「做了什么」，账本记「判定结果如何」。
- **提案** `friction_report.py`：聚合出仪式开销比、拒绝 Top、从未命中的检查，产出 `demote` / `retire-or-demote` /
  `safety-healthy` / `fix-plumbing` 提案。**它不自动改策略** —— 改 `policy.json` 须 meta-developer 复核 + 人工确认。
- **策略** `policy.json`：每条检查的 `level`（`block`/`warn`/`off`）与 `retirable`；另有 `workspace` 段
  （`required` / `applies_to`）控制工作目录边界的开关与作用区。缺省条目 = 检查自身固有级别（= v2 行为）。
  策略**只放宽不收紧**（收紧是检查实现的事，不是数据的事）。

**当前档位（2026-10-09 首批调级，人工批准）**：`ceremony.recipe_gate` 与 `ceremony.knowledge_high_priority`
由 `block` 降为 `warn` —— 未过配方链也能写（携带 `ceremony.recipe_gate` warning、`recipe` 记 `null`），
属 agent 自主性档位，**观察一个完整任务周期**，质量下滑即回 `block`（不必等样本）。
硬闸未变：作用域 / 工作目录 / 结构规范 / 落点（新文件）/ 删除与移动安全 / **编造文件名**。
未随降级放宽的项及理由见 `policy.json` 的 `_history`；档位表见
`.agents/knowledge/unity/mcp-gate-usage.md` §当前档位。

```bash
python3 .mcp/validation/gate_cli.py workspace set Assets/Mine/Scripts    # 声明工作目录
python3 .mcp/validation/gate_cli.py workspace approve ws-1a2b3c4d         # 人工批准（agent 被 hook 拦）
python3 .mcp/validation/gate_cli.py status           # 配方 + 工作目录 + 策略级别 + 落盘状态
python3 .mcp/validation/gate_cli.py report           # 摩擦报告（lint 风格: exit 1 = 有 review 级提案待办）
python3 .mcp/validation/gate_cli.py report --json --since 7
```

## 使用

```bash
# 测试（从仓库根运行，默认使用 .mcp/.venv/bin/python）
python3 .mcp/tests/run_isolated.py --task-dir tmp/sessions/<date-task-id>

# 门禁 CLI（统一入口 —— write/delete/move 与 MCP 工具同一判定核）
python3 .mcp/validation/gate_cli.py write  --path <项目相对路径> --from <本地文件>
python3 .mcp/validation/gate_cli.py delete --reason "<原因>" [--recursive] <文件或目录...>
python3 .mcp/validation/gate_cli.py move   --reason "<原因>" <from> <to> ...
python3 .mcp/validation/gate_cli.py workspace set|request|approve|reject|show ...
python3 .mcp/validation/gate_cli.py status | report
# 以上任一加 --dry-run = 只判定不执行（delete 的 dry_run 返回展开后的 deletable 清单）；
# 兼容入口 write_gated.py / delete_gated.py / move_gated.py 等价

# 单文件规范自查（Codex 对等通道，不写盘）
python3 .mcp/validation/check_norm.py Assets/Mine/Shaders/Render/Xxx/Xxx.shader
```

MCP 契约套件会 reset 状态，默认通过 `tests/run_isolated.py` 在独立项目/Git/TMPDIR 执行；不要直接对生产项目跑套件。runner 前后核对生产 state 与审计哈希，fixture 随运行清理；缺依赖用 `--python` 指定已有环境，失败可显式 `--retain-on-failure`。日志/报告留在任务 output/evidence，规则见 [会话产物规范](../.agents/knowledge/meta/interfaces/task-artifacts.md)。
