# Unity Gate MCP Server

> 后果验证门禁系统。Claude Code 通过 `.mcp.json` 自动加载。

作用域仅限 `unity-developer` 的 Unity 业务写入。`meta-developer` 与 `.agents/.mcp/.claude/.codex` 体系维护直接绕过本 MCP，改由 meta 架构测试验收。

可写目录：`Assets/Mine/` 全部子目录、`.agents/agents/unity-developer/scripts/roslyn/` 和 `tmp/`。路径按真实目录边界校验，拒绝相似前缀与软链越界；知识和内容门禁仍然生效。Claude/Codex 共用 `validation/project_paths.py` 的路径规则。

## 架构

```
Gate Center (gate_center.py)      ← 注册表 + 配方表 + 状态追踪
  └── RECIPES (配方)               ← 会话模式声明；链统一 [g_entry, g_knowledge]
        └── Gates (gates/*.py)     ← check(ctx) → pass | fail
Validation (validation/)           ← script_library.py + norms.py + deletion.py + check_norm.py
  └── norms.py                     ← 结构规范数据化（写入后果验证规则）
  └── deletion.py                  ← 不可恢复性判定（删除后果验证规则）
  └── check_api_refs.py            ← 模板 API 核验（不属门禁链，见末节）
```

三层解耦：**中心不在意门禁内容，配方不在意门禁实现，门禁只在意自身逻辑。**

**v2 收敛（2026-08-25）**：门禁从「流程仪式 + 自报告」收敛为「知识证据 + 内容后果验证」；g_knowledge 校验真实文件，write_gated 校验内容，Codex 经 check_norm.py 对等接入。

**写删对称（2026-09-21）**：`delete_gated` 与 `write_gated` 共用同一道门禁（`can_write`）与同一套路径作用域（`validate_path`）。删除没有「内容」可查，所以后果验证改为判定**不可恢复性**（见下节）。

## 工具

| 工具 | 作用 |
|------|------|
| `gate_set_recipe(name)` | 声明模式: Production / Research / Experiment / Debug / Minimal / Quick |
| `gate_pass(gate_id, **ctx)` | 通过 g_entry / g_knowledge |
| `gate_status()` | 当前状态 + 写入审计 + 删除审计 |
| `gate_list()` | 列出所有门禁 + 配方（含退役标记） |
| `gate_reset()` | 重置 |
| `script_list()` | 列出 scripts/roslyn/ |
| `write_gated(path, content, ...)` | 门禁 + 规范检查通过才放行写入（注解非阻塞记录） |
| `delete_gated(paths, reason)` | 门禁 + 不可恢复性检查通过才放行删除（批量，all-or-nothing） |

删除另有 Bash 通道 `validation/delete_gated.py`（同一道门禁），见下节。

## 配方

Production / Research / Experiment / Debug / Minimal / Quick 均使用唯一链 `g_entry → g_knowledge`；配方只声明模式并用于审计。

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

## 后果验证（validation/norms.py）

写入内容执行结构规范检查——error 级阻断（NORM_VIOLATION），warning 级提示放行：

| 规则 | 适用 | 级别 | 检测 |
|------|------|------|------|
| shader-decl — 必须含 `Shader "..."` | .shader | error | 全量 |
| cs-type-decl — 必须含类型声明 | .cs | error | 全量 |
| region-added — 禁止 #region | .cs | error | 仅新增行 |
| divider-added — 分隔线统一 ═ 风格 | .shader/.hlsl/.cs | warning | 仅新增行 |

新增行 diff：对已存在文件只检查本次引入的行，历史遗留不合规不阻断新写入。
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

整批 **all-or-nothing**：任一路径被拦则整批不执行，`blocked` 列出原因。

**两个通道，同一道门禁**——判定同源（都走 `deletion.evaluate`），不允许各判一套：

| 通道 | 入口 | 用途 |
|---|---|---|
| MCP | `mcp__unity-gate__delete_gated` | 会话内常规调用 |
| Bash | `python3 .mcp/validation/delete_gated.py --reason "..." <paths...>` | MCP 工具表未刷新时、或脚本化批量删除 |

CLI 的门禁状态读 `.mcp/state.json`（与 `can_write` 同语义）——**只消费已通过的链，不提供过门禁的入口**；`--reason` 必填，支持 `--dry-run`（判定可删但不执行）。退出码 `0` = 成功 / `1` = 被拦 / `2` = 用法错误。

**删除审计落盘** `.mcp/deletes.jsonl`（JSONL，不入库，`gate_status` 另显最近 20 条）：写入有产物本身即记录，**删除没有——日志是唯一痕迹**。两个通道都追加，含 `channel` 字段区分，并记 `argv` 便于溯源。

> 存在的理由：`Assets/Mine/` 的直接 `Write`/`Edit` 被 settings deny（逼写入走 `write_gated`），而 Claude Code 的路径规则会**连带命中 Bash**——`rm`/`mv` 即便在 allow 列表里也被拦。MCP 调用与 Python 子进程都不经该路径匹配，故这一对通道是 `Assets/Mine/` 下可用的删除路径。

## Codex 对等

```bash
python .mcp/validation/check_norm.py <file>   # exit 0 = 通过; exit 1 = 有 error 违规
```

## 模板 API 核验（validation/check_api_refs.py）

**不属门禁链**——模板写入不经 `write_gated`，本 CLI 是 meta-developer 维护 `templates/` 时的自查工具，
与 `check_norm.py`（业务产出）对称。查的是「模板里写下的 API 确实存在且没过时」，不是「模板能编译」：
模板是给人拷贝的参考实现，本就有占位符，`.shader`/`.hlsl` 也无从用编译器验。

```bash
python3 .mcp/validation/check_api_refs.py .agents/agents/unity-developer/templates   # 词法 + 过时 API
python3 .mcp/validation/check_api_refs.py --compile <自足骨架.cs>                     # 追加真编译
```

（以上为仓库根执行；本节其余示例沿用 `.mcp/` 内执行的旧写法。）
判据、边界与控制组要求见 `.agents/agents/meta-developer/references/template-conventions.md`。

## 使用

```bash
# 测试
uv run python tests/test_recipes.py

# 规范检查 CLI（Codex 对等通道）
python validation/check_norm.py Assets/Mine/Shaders/Render/Xxx/Xxx.shader

# 删除门禁 CLI（Bash 通道，与 MCP delete_gated 同一道门禁）
python3 .mcp/validation/delete_gated.py --reason "<原因>" <路径...>
python3 .mcp/validation/delete_gated.py --reason "<原因>" --dry-run <路径...>   # 只判定不执行

# 模板 API 核验（meta 层，仓库根执行）
python3 .mcp/validation/check_api_refs.py .agents/agents/unity-developer/templates
```
