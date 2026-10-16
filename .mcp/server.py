"""Unity Gate MCP Server — 后果验证门禁系统 (v3).

本文件是**传输薄壳**：三个写/删/移工具把参数交给 ``validation/judgement.py`` 的判定核，
策略只在那里应用、信号只在那里落账。判定逻辑不写在这里 —— 否则 CLI 通道会各自分叉
（v2 的 write 就因此长期没有 CLI 通道）。

链统一为 [g_entry, g_knowledge]；模式（recipe）经 gate_set_recipe 声明；脚本决策/文件分类
降级为 write_gated 注解（记录不阻塞）。
delete_gated 是 write_gated 的对称面 —— 删除无内容可查，后果验证落在「不可恢复性」上。
move_gated 是两者的**复合面** —— 移动 = 写新 + 删旧，验的是净效果：内容是否只是换了位置
（连同 .meta 的 GUID）。单独看删除，tracked-dirty 硬拦是对的；在移动里就成了误拦。

传输对等 v3: 三条操作都有 CLI 等价入口 ``.mcp/validation/gate_cli.py {write,delete,move}``
（同一判定核、同一错误码）。策略（``.mcp/policy.json``）与摩擦账本（``.mcp/friction.jsonl``）
是 v3 的两个新面 —— 严格度可调、每次判定留痕，见
``.agents/knowledge/meta/enforcement-policy.md``。

工具:
  gate_set_recipe(name)              — 选择配方 (模式声明)
  gate_set_workspace(path)            — 声明本次任务的工作目录（写入边界）
  gate_request_workspace(path, reason) — 申请扩展工作目录（人工批准，agent 不得自批）
  gate_pass(gate_id, **context)      — 通过指定门禁
  gate_status()                       — 查看当前状态
  gate_list()                         — 列出所有门禁 + 配方
  gate_reset()                        — 重置
  script_list()                       — 列出脚本库
  write_gated(path, content, ...)     — 门禁 + 规范检查后写入
  delete_gated(paths, reason, recursive, dry_run) — 门禁 + 结构/不可恢复性检查后删除（含目录）
  move_gated(moves, reason)           — 门禁 + 净效果检查后移动（.meta 成对，保 GUID）
"""

import json, asyncio
from mcp.server import MCPServer

from gate_center import state, GATE_REGISTRY, RECIPES
from validation.script_library import list_scripts
from validation import judgement

server = MCPServer(name="unity-gate", version="0.7.0")


def _append_audit(lst: list, rec: dict, cap: int = 100) -> None:
    """会话内审计（最近 cap 条）。落盘的持久审计在 validation/judgement.py。"""
    lst.append(rec)
    if len(lst) > cap:
        del lst[:-cap]


# ═══ gate_set_recipe — 选择配方 ═══

@server.tool()
async def gate_set_recipe(name: str) -> str:
    """选择配方 (Mode)。新任务第一步。

    Args:
        name: "Production" | "Research" | "Experiment" | "Debug" | "Minimal" | "Quick"
    """
    return json.dumps(state.set_recipe(name), ensure_ascii=False)


# ═══ gate_set_workspace / gate_request_workspace — 任务边界 ═══

@server.tool()
async def gate_set_workspace(path: str) -> str:
    """声明本次任务的工作目录（写入边界）。任务开始第一步之一。

    作用域: `Assets/Mine/**` 下的写入 / 删除 / 移动必须落在该目录内；`tmp/` 等
    非工程资产区不受边界约束。**边界只能收窄** —— path 必须先落在基础作用域内。
    需要多工作区时用 gate_request_workspace 登记请求，由人工批准后扩展。

    Args:
        path: 项目相对目录，如 "Assets/Mine/Scripts"
    """
    return json.dumps(state.set_workspace(path), ensure_ascii=False)


@server.tool()
async def gate_request_workspace(path: str, reason: str) -> str:
    """申请扩展工作目录 —— **只登记待批请求，不授权**。

    批准是人工动作（`gate_cli.py workspace approve <request_id>`），agent 不得自行批准。
    在批准前，边界外的操作仍然 DENIED。

    Args:
        path: 想扩展到的项目相对目录
        reason: 为什么需要（进审计，供人裁决）
    """
    return json.dumps(state.request_workspace(path, reason), ensure_ascii=False)


# ═══ gate_pass — 通过指定门禁 ═══

@server.tool()
async def gate_pass(gate_id: str, agent: str = "", mode: str = "", reason: str = "",
                    decision: str = "", file_type: str = "", category: str = "",
                    effect: str = "", query: str = "", summary: str = "",
                    plan_summary: str = "", loaded_files: str = "",
                    status: str = "") -> str:
    """通过指定门禁。传入门禁需要的上下文参数。

    实质门禁（配方内）:
      g_entry: agent="unity-developer"
      g_knowledge: loaded_files="unity/standard/shader/shader-structure.md, unity/standard/script/script-structure.md", status="COMPLETE"
    退役门禁（调用返回 GATE_NOT_IN_RECIPE）:
      g_mode → 模式走 gate_set_recipe(name)
      g_script / g_file → 走 write_gated 注解参数

    Args:
        gate_id: 门禁 ID (如 "g_entry", "g_knowledge")
    """
    kwargs = {k: v for k, v in locals().items()
              if k not in ("gate_id",) and v}  # 跳过空值
    return json.dumps(state.pass_gate(gate_id, **kwargs), ensure_ascii=False)


# ═══ gate_status / gate_list / gate_reset ═══

@server.tool()
async def gate_status() -> str:
    """查看当前配方、门禁通过状态与写入审计."""
    return json.dumps({
        "recipe": state.recipe,
        "workspace": state.workspace_view(),
        "passed": sorted(state.passed),
        "remaining": state.remaining,
        "contexts": {k: v for k, v in state.contexts.items()},
        "writes": state.writes[-20:],     # 最近 20 条写入审计
        "deletes": state.deletes[-20:],   # 最近 20 条删除审计
        "moves": state.moves[-20:],       # 最近 20 条移动审计
    }, ensure_ascii=False)


@server.tool()
async def gate_list() -> str:
    """列出所有可用门禁和配方."""
    return json.dumps({
        "gates": {gid: {"name": d["name"], "requires": d["requires"],
                        "retired": d.get("retired", False)}
                  for gid, d in GATE_REGISTRY.items()},
        "recipes": RECIPES,
    }, ensure_ascii=False)


@server.tool()
async def gate_reset() -> str:
    """重置门禁状态（新任务开始）."""
    state.reset()
    return json.dumps({"status": "OK", "message": "已重置。请 gate_set_recipe(name) 开始新任务。"})


# ═══ script_list — 脚本库 ═══

@server.tool()
async def script_list() -> str:
    """列出 scripts/roslyn/ 中的所有可用脚本."""
    scripts = list_scripts()
    return json.dumps({"scripts": scripts, "count": len(scripts)}, ensure_ascii=False)


# ═══ write_gated — 带门禁写入（知识证据 + 内容后果验证）═══

@server.tool()
async def write_gated(path: str, content: str, mode: str = "", script_decision: str = "",
                      file_type: str = "", category: str = "", effect: str = "") -> str:
    """门禁校验后写入文件。配方门禁全部通过 + 内容通过规范检查后才放行。

    门禁: [g_entry, g_knowledge]（知识证据）— 所有模式（含 Quick/Minimal）一致。
    后果验证: 内容执行结构规范检查（norms.py），error 级违规阻断，warning 级放行携带提示。
    注解（非阻塞，记录审计）: mode 缺省取配方；script_decision 复用脚本库校验。

    Args:
        path: 目标文件路径（相对于项目根目录）
        content: 文件内容
        mode: 模式注解（缺省 = 当前配方）
        script_decision: 脚本决策注解，如 "USE scene-query.cs" | "NONE"
        file_type / category / effect: 文件分类注解
    """

    return json.dumps(judgement.write(
        path, content, channel="mcp", tool="write_gated", gate=state,
        workspace=state.workspace_view(),
        mode=mode, script_decision=script_decision, file_type=file_type,
        category=category, effect=effect,
        on_record=lambda rec: _append_audit(state.writes, rec)), ensure_ascii=False)


# ═══ delete_gated — 带门禁删除（不可恢复性验证）═══

@server.tool()
async def delete_gated(paths: list[str], reason: str, recursive: bool = False,
                       dry_run: bool = False) -> str:
    """门禁校验后删除文件或目录 —— write_gated 的对称面。

    门禁: 与 write_gated 同一道闸（配方门禁全过）+ 同一套路径作用域。
    后果验证（validation/deletion.py）—— 删除无内容可查，验的是**不可恢复性**:
      tracked-clean → 放行（git 可恢复）
      tracked-dirty → 硬拦：删了只剩历史版本
      untracked     → 放行，结果标注不可恢复
      .meta 的 GUID 仍被 Assets/ 引用 → 硬拦
    空目录默认可删；非空目录须 recursive=True。展开子项后整批预检，
    任一路径被拦则整批不执行；不跟随软链，不删除基础作用域根。

    Args:
        paths: 待删文件或目录列表（项目相对，作用域同 write_gated）
        reason: 删除原因（必填，进审计）
        recursive: 显式允许删除非空目录及经过验证的子项
        dry_run: 仅预检并返回展开清单，不执行文件系统删除
    """

    return json.dumps(judgement.delete(
        paths, reason, channel="mcp", tool="delete_gated", gate=state,
        workspace=state.workspace_view(),
        recursive=recursive, dry_run=dry_run,
        on_record=lambda rec: _append_audit(state.deletes, rec)), ensure_ascii=False)


# ═══ move_gated — 带门禁移动（净效果验证）═══

@server.tool()
async def move_gated(moves: list[dict], reason: str) -> str:
    """门禁校验后移动文件 —— write_gated 与 delete_gated 的复合面。

    移动 = 写新 + 删旧。单独看每一步判定都对（旧位置脏 → 删了只剩历史；新位置内容
    规范 → 放行），合起来却是误拦：内容完整写到了新位置。所以后果验证看的是**净效果**
    —— 内容是否只是换了位置，见 validation/moving.py。

    执行顺序 write-all → delete-all，拒绝的代价与失败的代价都有方向：
      预检不过     → 零改动（拒绝的代价必须是零）
      写阶段失败   → 回滚已写的新文件，源一个没删（等于没发生）
      删阶段失败   → 保留旧副本，内容没丢（安全方向），failed 里列出

    .meta 成对搬运以保住 Unity 的 GUID；搬运前查该 GUID 是否已被别的 .meta 声明
    （同 GUID 两份 = Unity 报错，与位置无关）。

    **不复跑内容规范检查**：移动不引入新内容，正文逐字节原样搬运；对一次纯搬位置
    重跑 norms，会把「历史遗留不合规但本来就存在」的文件拦下 —— 那是限制而非规范。

    Args:
        moves: [{"from": "<项目相对源路径>", "to": "<项目相对目标路径>"}, ...]
        reason: 移动原因（必填，进审计）
    """

    return json.dumps(judgement.move(
        moves, reason, channel="mcp", tool="move_gated", gate=state,
        workspace=state.workspace_view(),
        on_record=lambda rec: _append_audit(state.moves, rec)), ensure_ascii=False)


# ═══ main ═══
async def main():
    try:
        # mcp 2.x: MCPServer 自带 stdio 入口（含初始化选项 + capabilities 自动生成）
        await server.run_stdio_async()
    except Exception as e:
        # stdio server 需要 MCP client 连接 — 直接运行会报错是正常的
        import sys
        print(f"MCP server stopped: {e}", file=sys.stderr)
        print("This server is started automatically by Claude Code via .mcp.json.", file=sys.stderr)
        print("To test: uv run python tests/test_recipes.py", file=sys.stderr)


if __name__ == "__main__":
    asyncio.run(main())
