"""Unity Gate MCP Server — 后果验证门禁系统 (v2).

链统一为 [g_entry, g_knowledge] — 门禁只保留「知识证据」实质校验:
  g_knowledge 校验声明条目命中真实知识文件（防编造）
  write_gated 对内容执行结构规范检查（后果验证，新增行 diff）

模式（recipe）经 gate_set_recipe 声明；脚本决策/文件分类降级为 write_gated 注解（记录不阻塞）。
delete_gated 是 write_gated 的对称面 —— 删除无内容可查，后果验证落在「不可恢复性」上。
move_gated 是两者的**复合面** —— 移动 = 写新 + 删旧，验的是净效果：内容是否只是换了位置
（连同 .meta 的 GUID）。单独看删除，tracked-dirty 硬拦是对的；在移动里就成了误拦。
Bash 通道: python .mcp/validation/delete_gated.py --reason "..." <paths...>（同一道门禁）
Codex 对等通道: python .mcp/validation/check_norm.py <file>

工具:
  gate_set_recipe(name)              — 选择配方 (模式声明)
  gate_pass(gate_id, **context)      — 通过指定门禁
  gate_status()                       — 查看当前状态
  gate_list()                         — 列出所有门禁 + 配方
  gate_reset()                        — 重置
  script_list()                       — 列出脚本库
  write_gated(path, content, ...)     — 门禁 + 规范检查后写入
  delete_gated(paths, reason)         — 门禁 + 不可恢复性检查后删除
  move_gated(moves, reason)           — 门禁 + 净效果检查后移动（.meta 成对，保 GUID）
"""

import json, os, asyncio
from mcp.server import MCPServer

from gate_center import state, GATE_REGISTRY, RECIPES
from validation.script_library import list_scripts, validate_decision
from validation.project_paths import validate_path, check_placement, PROJECT_ROOT
from validation.norms import check_content, _existing_lines
from validation.deletion import evaluate, append_audit, orphan_meta_warnings, classify
from validation.moving import (plan as plan_moves, execute as execute_moves,
                               append_audit as append_move_audit)
from validation.atomic_io import atomic_write

server = MCPServer(name="unity-gate", version="0.6.0")


# ═══ gate_set_recipe — 选择配方 ═══

@server.tool()
async def gate_set_recipe(name: str) -> str:
    """选择配方 (Mode)。新任务第一步。

    Args:
        name: "Production" | "Research" | "Experiment" | "Debug" | "Minimal" | "Quick"
    """
    return json.dumps(state.set_recipe(name), ensure_ascii=False)


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
    path_check = validate_path(path)
    if path_check["status"] != "OK":
        return json.dumps(path_check, ensure_ascii=False)

    gate_check = state.can_write()
    if gate_check["status"] != "OK":
        return json.dumps(gate_check, ensure_ascii=False)

    full_path = path_check.get("full_path", path)

    # ── 后果验证: 结构规范（新增行 diff）+ 文件落点 ──
    # 两者合成一次判定：都是「这次写入的后果」，调用方看 violations[].id 区分来源
    existing = _existing_lines(full_path)
    is_new = not os.path.isfile(full_path)
    norm = check_content(path, content, existing=existing)
    place = check_placement(path, is_new=is_new, category=category)
    violations = norm["errors"] + place["errors"]
    if violations:
        return json.dumps({
            "status": "DENIED", "error": "NORM_VIOLATION",
            "violations": violations,
            "hint": "违反写入规范（结构规范或文件落点，来源见 violations[].id / .source）。"
                    "修正后重试；确属规范不适用再提交 review。",
        }, ensure_ascii=False)

    # ── 注解（非阻塞，记录审计）──
    annotations: dict = {"mode": mode or state.recipe}
    if script_decision:
        annotations["script_decision"] = validate_decision(script_decision)
    if file_type:
        annotations["file_type"] = file_type
    if category:
        annotations["category"] = category
    if effect:
        annotations["effect"] = effect

    # ── 原子写（见 validation/atomic_io.py）──
    try:
        atomic_write(full_path, content)
    except Exception as e:
        return json.dumps({"status": "ERROR", "error": str(e),
                           "hint": "文件写入失败。"})

    record = {"path": path, "bytes": len(content.encode("utf-8")),
              "recipe": state.recipe, "annotations": annotations}
    state.writes.append(record)
    if len(state.writes) > 100:
        state.writes = state.writes[-100:]
    response = {
        "status": "OK", "written": path,
        "bytes": len(content.encode("utf-8")),
        "recipe": state.recipe, "passed": sorted(state.passed),
        "annotations": annotations,
    }
    warnings = norm["warnings"] + place["warnings"]
    if warnings:
        response["warnings"] = warnings   # 提示不阻断
    return json.dumps(response, ensure_ascii=False)


# ═══ delete_gated — 带门禁删除（不可恢复性验证）═══

@server.tool()
async def delete_gated(paths: list[str], reason: str) -> str:
    """门禁校验后删除文件 —— write_gated 的对称面。

    门禁: 与 write_gated 同一道闸（配方门禁全过）+ 同一套路径作用域。
    后果验证（validation/deletion.py）—— 删除无内容可查，验的是**不可恢复性**:
      tracked-clean → 放行（git 可恢复）
      tracked-dirty → 硬拦：删了只剩历史版本
      untracked     → 放行，结果标注不可恢复
      .meta 的 GUID 仍被 Assets/ 引用 → 硬拦
    整批 all-or-nothing: 任一路径被拦则整批不执行。

    Args:
        paths: 待删文件路径列表（项目相对，作用域同 write_gated）
        reason: 删除原因（必填，进审计）
    """
    if not reason or not reason.strip():
        return json.dumps({"status": "DENIED", "error": "MISSING_REASON",
                           "hint": "reason 必填 —— 删除必须留下可追溯的原因。"},
                          ensure_ascii=False)
    if not paths:
        return json.dumps({"status": "DENIED", "error": "EMPTY_PATHS",
                           "hint": "paths 为空。"}, ensure_ascii=False)

    # ── 作用域校验（与 write_gated 同一函数）──
    checked: list[tuple] = []
    for p in paths:
        path_check = validate_path(p)
        if path_check["status"] != "OK":
            return json.dumps({**path_check, "offending_path": p}, ensure_ascii=False)
        checked.append((p, path_check["full_path"]))

    # ── 门禁（与 write_gated 同一道闸）──
    gate_check = state.can_write()
    if gate_check["status"] != "OK":
        return json.dumps(gate_check, ensure_ascii=False)

    # ── 后果验证: 不可恢复性 + .meta GUID 引用（与 CLI 同源: deletion.evaluate）──
    ev = evaluate(PROJECT_ROOT, checked)
    status, blocked = ev["status"], ev["blocked"]
    if blocked:
        return json.dumps({
            "status": "DENIED", "error": "DELETION_UNSAFE", "blocked": blocked,
            "hint": "整批未执行。tracked-dirty 先提交或还原；guid-referenced 先解除引用。",
        }, ensure_ascii=False)

    # ── 执行（missing 跳过 —— 批量清单可重复执行）──
    deleted, failed = [], []
    for p, full in checked:
        info = status[p]
        if info["state"] == "missing":
            continue
        try:
            os.remove(full)
            deleted.append({"path": p, "state": info["state"],
                            "recoverable": info["recoverable"]})
        except OSError as e:
            failed.append({"path": p, "error": str(e)})

    deleted_paths = [d["path"] for d in deleted]
    record = {
        "channel": "mcp", "paths": deleted_paths, "count": len(deleted),
        "reason": reason.strip(), "recipe": state.recipe,
        "unrecoverable": [d["path"] for d in deleted if not d["recoverable"]],
    }
    state.deletes.append(record)
    if len(state.deletes) > 100:
        state.deletes = state.deletes[-100:]
    append_audit(PROJECT_ROOT, record)   # 落盘 —— 删除不留产物，日志是唯一痕迹

    response = {
        "status": "OK", "deleted": deleted, "count": len(deleted),
        "recipe": state.recipe, "passed": sorted(state.passed),
        "reason": reason.strip(),
    }
    if failed:
        response["failed"] = failed
    warnings = orphan_meta_warnings(PROJECT_ROOT, deleted_paths)
    if warnings:
        response["warnings"] = warnings   # 提示不阻断
    return json.dumps(response, ensure_ascii=False)


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
    if not reason or not reason.strip():
        return json.dumps({"status": "DENIED", "error": "MISSING_REASON",
                           "hint": "reason 必填 —— 移动必须留下可追溯的原因。"},
                          ensure_ascii=False)
    if not moves:
        return json.dumps({"status": "DENIED", "error": "EMPTY_MOVES",
                           "hint": "moves 为空。"}, ensure_ascii=False)

    # ── 入参整形：moves 是 [{"from","to"}]，缺键/空值当场指出，不猜 ──
    pairs: list[tuple] = []
    for i, m in enumerate(moves):
        if not isinstance(m, dict):
            return json.dumps({"status": "DENIED", "error": "BAD_MOVE_SHAPE",
                               "hint": f"moves[{i}] 应为对象 {{\"from\": ..., \"to\": ...}}。"},
                              ensure_ascii=False)
        src, dst = str(m.get("from", "")).strip(), str(m.get("to", "")).strip()
        if not src or not dst:
            return json.dumps({"status": "DENIED", "error": "BAD_MOVE_SHAPE",
                               "hint": f"moves[{i}] 的 from/to 都不能为空。"},
                              ensure_ascii=False)
        # ── 作用域校验：源与目标都要在作用域内（同一个 validate_path）──
        for p in (src, dst):
            path_check = validate_path(p)
            if path_check["status"] != "OK":
                return json.dumps({**path_check, "offending_path": p}, ensure_ascii=False)
        pairs.append((src, dst))

    # ── 门禁（与 write_gated / delete_gated 同一道闸）──
    gate_check = state.can_write()
    if gate_check["status"] != "OK":
        return json.dumps(gate_check, ensure_ascii=False)

    # ── 后果验证：预检（零改动）──
    planned = plan_moves(PROJECT_ROOT, pairs)
    if planned["blocked"]:
        return json.dumps({
            "status": "DENIED", "error": "MOVE_UNSAFE", "blocked": planned["blocked"],
            "hint": "整批未执行。TARGET_EXISTS 要替换请走 write_gated；"
                    "GUID_TAKEN 先解除占用；CHAINED_MOVE 拆成两批。",
        }, ensure_ascii=False)
    plan_pairs = planned["pairs"]

    # ── 执行（write-all → delete-all）——与 CLI 通道共用同一个 execute ──
    result = execute_moves(PROJECT_ROOT, plan_pairs)
    if result["status"] != "OK":
        return json.dumps({"status": "ERROR", "error": f"写阶段失败，已回滚: {result['error']}",
                           "rolled_back": result["rolled_back"],
                           "hint": result["hint"]}, ensure_ascii=False)

    moved, removed, failed = result["moved"], result["removed"], result["failed"]

    # 记录搬走的源里有哪些带着未提交改动 —— 移动放行它们正是本工具存在的理由，
    # 但「带走了未提交内容」这件事必须在审计里留痕（一次 classify，不随文件数增长）
    src_states = classify(PROJECT_ROOT, [m["from"] for m in moved])

    record = {
        "channel": "mcp", "kind": "move", "moves": moved, "count": len(moved),
        "removed": removed, "reason": reason.strip(), "recipe": state.recipe,
        "carried_dirty": [p for p, info in src_states.items()
                          if info["state"] == "tracked-dirty"],
    }
    state.moves.append(record)
    if len(state.moves) > 100:
        state.moves = state.moves[-100:]
    append_move_audit(PROJECT_ROOT, record)   # 落盘 —— 丢失的是「哪个变成了哪个」

    response = {
        "status": "OK", "moved": moved, "count": len(moved),
        "recipe": state.recipe, "passed": sorted(state.passed),
        "reason": reason.strip(),
    }
    if failed:
        response["failed"] = failed
        response["hint"] = "新位置已写好；旧副本删除失败，内容没有丢失。"
    return json.dumps(response, ensure_ascii=False)


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
