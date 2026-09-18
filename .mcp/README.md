# Unity Gate MCP Server

> 后果验证门禁系统。Claude Code 通过 `.mcp.json` 自动加载。

作用域仅限 `unity-developer` 的 Unity 业务写入。`meta-developer` 与 `.agents/.mcp/.claude/.codex` 体系维护直接绕过本 MCP，改由 meta 架构测试验收。

可写目录：`Assets/Mine/` 全部子目录、`.agents/agents/unity-developer/scripts/roslyn/` 和 `tmp/`。路径按真实目录边界校验，拒绝相似前缀与软链越界；知识和内容门禁仍然生效。Claude/Codex 共用 `validation/project_paths.py` 的路径规则。

## 架构

```
Gate Center (gate_center.py)      ← 注册表 + 配方表 + 状态追踪
  └── RECIPES (配方)               ← 会话模式声明；链统一 [g_entry, g_knowledge]
        └── Gates (gates/*.py)     ← check(ctx) → pass | fail
Validation (validation/)           ← script_library.py + norms.py + check_norm.py
  └── norms.py                     ← 结构规范数据化（后果验证规则）
```

三层解耦：**中心不在意门禁内容，配方不在意门禁实现，门禁只在意自身逻辑。**

**v2 收敛（2026-08-25）**：门禁从「流程仪式 + 自报告」收敛为「知识证据 + 内容后果验证」；g_knowledge 校验真实文件，write_gated 校验内容，Codex 经 check_norm.py 对等接入。

## 工具

| 工具 | 作用 |
|------|------|
| `gate_set_recipe(name)` | 声明模式: Production / Research / Experiment / Debug / Minimal / Quick |
| `gate_pass(gate_id, **ctx)` | 通过 g_entry / g_knowledge |
| `gate_status()` | 当前状态 + 写入审计 |
| `gate_list()` | 列出所有门禁 + 配方（含退役标记） |
| `gate_reset()` | 重置 |
| `script_list()` | 列出 scripts/roslyn/ |
| `write_gated(path, content, ...)` | 门禁 + 规范检查通过才放行写入（注解非阻塞记录） |

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

## Codex 对等

```bash
python .mcp/validation/check_norm.py <file>   # exit 0 = 通过; exit 1 = 有 error 违规
```

## 使用

```bash
# 测试
uv run python tests/test_recipes.py

# 规范检查 CLI
python validation/check_norm.py Assets/Mine/Shaders/Render/Xxx/Xxx.shader
```
