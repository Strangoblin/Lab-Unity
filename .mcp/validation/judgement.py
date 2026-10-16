"""判定核 — write / delete / move 的唯一决策实现（MCP v3）.

v2 把判定写在 ``server.py`` 的工具函数里，于是「Bash 通道」只能在旁边重新拼一遍流程：
``delete_gated.py`` / ``move_gated.py`` 各自解析参数、各自走 ``state.json``、各自执行。
后果是**写入至今没有 CLI 通道**（Codex 也就没有等价通道），而两条通道迟早会在细节上分叉。

v3 把判定抽到这里，规定:
  - **判定只有一份实现**（本模块），MCP 工具与 CLI 都是薄壳
  - **策略只在判定核内应用**（``policy.level_of``）——通道自己不许判级别
  - **信号只在判定核内落账**（``telemetry``）——通道自己不许记账，否则口径不一
  - 通道通过 ``gate`` 回调传入「会话门禁状态」（MCP 用进程内 state，CLI 读 state.json）

行为等价性: 缺省 policy（= 固有级别）下，本模块的响应形状与 v2 的
``write_gated`` / ``delete_gated`` / ``move_gated`` 逐字段一致。
"""

from __future__ import annotations

import os

from validation.atomic_io import atomic_write
from validation.deletion import evaluate, append_audit as append_delete_audit, \
    orphan_meta_warnings, classify, plan as plan_deletion, execute as execute_deletion
from validation.moving import plan as plan_moves, execute as execute_moves, \
    append_audit as append_move_audit
from validation.norms import check_content, _existing_lines
from validation.policy import level_of, workspace_policy
from validation.project_paths import validate_path, check_placement, check_workspace, \
    PROJECT_ROOT, ALLOWED_PREFIXES
from validation.script_library import validate_decision
from telemetry import record as emit


# ═══ policy 应用 — 把违规按有效级别分桶 ═══

def _split_by_level(violations: list, check_id_of) -> tuple:
    """→ (errors, warnings, muted). 级别取自 policy，缺省用违规自报的 level。"""
    errors, warnings, muted = [], [], []
    for v in violations:
        cid = check_id_of(v)
        intrinsic = "block" if v.get("level") == "error" else "warn"
        level = level_of(cid, intrinsic)
        item = {**v, "check": cid}
        if level == "block":
            errors.append({**item, "level": "error"})
        elif level == "warn":
            warnings.append({**item, "level": "warning"})
        else:
            muted.append({**item, "level": "off"})
    return errors, warnings, muted


def _norm_check_id(v: dict) -> str:
    return f"norm.{v.get('id', 'unknown')}"


def _placement_check_id(v: dict, is_new: bool) -> str:
    if v.get("id") == "placement-root":
        return "placement.root.new" if is_new else "placement.root.existing"
    return "placement.category"


def _gate(state_gate) -> dict:
    """门禁视图：已算好的 dict / 回调 / 带 can_write 的对象，三种都收。"""
    if isinstance(state_gate, dict):
        return state_gate
    if callable(state_gate):
        return state_gate()
    return state_gate.can_write()


def _deny(*, op: str, tool: str, channel: str, signal: str, payload: dict,
          check: str = "", detail: str = "", path: str = "") -> dict:
    emit(op=op, tool=tool, channel=channel, outcome="denied", signal=signal,
         check=check, level="block", detail=detail or payload.get("hint", ""), path=path)
    return payload


# ═══ 工作目录（任务级边界，用户下达）═══

def workspace_from_file(state_file: str | None = None) -> dict:
    """CLI 通道用: 读 `.mcp/state.json` 的 workspace 段。"""
    import json
    from gate_center import STATE_FILE
    path = state_file or STATE_FILE
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f).get("workspace") or {}
    except (OSError, ValueError):
        return {}


def _workspace_deny(targets: list, workspace: dict, *, op: str, tool: str, channel: str) -> dict | None:
    """工作目录越界 → DENIED 响应；未越界返回 None。级别取自 policy（scope.workspace）。"""
    pol = workspace_policy()
    roots = (workspace or {}).get("roots", []) or []
    violations: list = []
    for t in targets:
        result = check_workspace(t, roots, pol["required"], pol["applies_to"])
        violations += result["errors"] + result["warnings"]
    if not violations:
        return None

    level = level_of("scope.workspace", "block")
    if level == "off":
        return None
    if level == "warn":
        emit(op=op, tool=tool, channel=channel, outcome="warn",
             signal="warn.scope.workspace", check="scope.workspace", level="warn",
             detail=violations[0].get("detail", ""), path=violations[0].get("path", ""))
        return None

    first = violations[0]
    emit(op=op, tool=tool, channel=channel, outcome="denied",
         signal=f"deny.workspace.{first['id']}", check="scope.workspace", level="block",
         detail=first.get("detail", ""), path=first.get("path", ""))
    code = "WORKSPACE_NOT_SET" if any(v["id"] == "workspace-required" for v in violations)         else "WORKSPACE_VIOLATION"
    return {"status": "DENIED", "error": code, "violations": violations,
            "workspace": {"primary": (workspace or {}).get("primary"), "roots": roots},
            "hint": "越界须人工批准 —— 用 gate_request_workspace(<目录>, <原因>) 登记请求，"
                    "再由用户运行 `gate_cli.py workspace approve <request_id>`。"}


# ═══ write ═══

def write(path: str, content: str, *, channel: str, tool: str, gate,
          mode: str = "", script_decision: str = "", file_type: str = "",
          category: str = "", effect: str = "", recipe: str = "",
          on_record=None, dry_run: bool = False, workspace: dict | None = None) -> dict:
    """判定 + 执行写入。响应形状同 v2 ``write_gated``。"""
    path_check = validate_path(path)
    if path_check["status"] != "OK":
        return _deny(op="write", tool=tool, channel=channel, signal="deny.scope.path",
                     payload=path_check, check="scope.path", path=path)

    ws_deny = _workspace_deny([path], workspace or {}, op="write", tool=tool, channel=channel)
    if ws_deny:
        return ws_deny

    gate_check = _gate(gate)
    if gate_check["status"] != "OK":
        sig = "deny.ceremony." + str(gate_check.get("error", "gate")).lower()
        return _deny(op="write", tool=tool, channel=channel, signal=sig,
                     payload=gate_check, check="ceremony.recipe_gate")

    full_path = path_check.get("full_path", path)
    existing = _existing_lines(full_path)
    is_new = not os.path.isfile(full_path)

    gate_warnings = list(gate_check.get("warnings", []))

    norm = check_content(path, content, existing=existing)
    place = check_placement(path, is_new=is_new, category=category)

    norm_errors, norm_warnings, norm_muted = _split_by_level(norm["errors"] + norm["warnings"],
                                                             _norm_check_id)
    place_errors, place_warnings, place_muted = _split_by_level(
        place["errors"] + place["warnings"], lambda v: _placement_check_id(v, is_new))

    errors = norm_errors + place_errors
    warnings = gate_warnings + norm_warnings + place_warnings

    if errors:
        first = errors[0]
        emit(op="write", tool=tool, channel=channel, outcome="denied",
             signal=f"deny.{first['check']}", check=first["check"], level="block",
             path=path, recipe=gate_check.get("recipe", recipe),
             detail=first.get("detail", ""))
        return {
            "status": "DENIED", "error": "NORM_VIOLATION",
            "violations": errors + warnings,
            "hint": "违反写入规范（结构规范或文件落点，来源见 violations[].id / .source）。"
                    "修正后重试；确属规范不适用再提交 review。",
        }

    annotations: dict = {"mode": mode or gate_check.get("recipe", recipe)}
    if script_decision:
        annotations["script_decision"] = validate_decision(script_decision)
    if file_type:
        annotations["file_type"] = file_type
    if category:
        annotations["category"] = category
    if effect:
        annotations["effect"] = effect

    if dry_run:
        emit(op="write", tool=tool, channel=channel, outcome="dry_run",
             signal="dry_run.write", path=path, recipe=gate_check.get("recipe", recipe))
        out = {"status": "OK", "dry_run": True, "path": path,
               "bytes": len(content.encode("utf-8")), "is_new": is_new,
               "recipe": gate_check.get("recipe", recipe)}
        if warnings:
            out["warnings"] = warnings
        return out

    try:
        atomic_write(full_path, content)
    except Exception as e:
        emit(op="write", tool=tool, channel=channel, outcome="error", signal="error.write",
             path=path, detail=str(e))
        return {"status": "ERROR", "error": str(e), "hint": "文件写入失败。"}

    rec = {"path": path, "bytes": len(content.encode("utf-8")),
           "recipe": gate_check.get("recipe", recipe), "annotations": annotations}
    if on_record:
        on_record(rec)

    if warnings:
        wid = warnings[0].get("check", "") or warnings[0].get("id", "gate_warning")
        emit(op="write", tool=tool, channel=channel, outcome="warn", signal=f"warn.{wid}",
             check=wid, level="warn", path=path,
             recipe=gate_check.get("recipe", recipe), detail=warnings[0].get("detail", ""))
    else:
        emit(op="write", tool=tool, channel=channel, outcome="ok", signal="ok.write",
             path=path, recipe=gate_check.get("recipe", recipe))

    response = {
        "status": "OK", "written": path, "bytes": len(content.encode("utf-8")),
        "recipe": gate_check.get("recipe", recipe),
        "passed": sorted(gate_check.get("passed", [])),
        "annotations": annotations,
    }
    if warnings:
        response["warnings"] = warnings
    if norm_muted or place_muted:
        response["muted"] = norm_muted + place_muted   # off 级：判定过、未阻断，留痕
    return response


# ═══ delete ═══

# 结构类拒绝（目录形状 / 软链 / 保护根）与不可恢复性拒绝分开记账：
# 前者说明「这个删除请求本身不成立」，后者说明「内容删了回不来」。
_STRUCTURE_STATES = frozenset(("directory-not-empty", "symlink", "unsupported-type",
                               "precheck-failed", "path-not-allowed", "protected-root"))


def delete(paths: list, reason: str, *, channel: str, tool: str, gate,
           on_record=None, argv: list | None = None, dry_run: bool = False,
           workspace: dict | None = None, recursive: bool = False) -> dict:
    """判定 + 执行删除（文件与目录）。响应形状同 v2 ``delete_gated``。

    目录：空目录默认可删；非空目录须 ``recursive=True``，展开后**逐个子项**仍按
    不可恢复性判定（dirty / GUID 引用），任一项被拦则整批零改动。
    """
    if not reason or not reason.strip():
        return _deny(op="delete", tool=tool, channel=channel, signal="deny.missing_reason",
                     payload={"status": "DENIED", "error": "MISSING_REASON",
                              "hint": "reason 必填 —— 删除必须留下可追溯的原因。"})
    if not paths:
        return _deny(op="delete", tool=tool, channel=channel, signal="deny.empty_paths",
                     payload={"status": "DENIED", "error": "EMPTY_PATHS", "hint": "paths 为空。"})

    checked: list = []
    for p in paths:
        path_check = validate_path(p)
        if path_check["status"] != "OK":
            return _deny(op="delete", tool=tool, channel=channel, signal="deny.scope.path",
                         payload={**path_check, "offending_path": p}, check="scope.path", path=p)
        checked.append((p, path_check["full_path"]))

    ws_deny = _workspace_deny([p for p, _ in checked], workspace or {},
                              op="delete", tool=tool, channel=channel)
    if ws_deny:
        return ws_deny

    gate_check = _gate(gate)
    if gate_check["status"] != "OK":
        sig = "deny.ceremony." + str(gate_check.get("error", "gate")).lower()
        return _deny(op="delete", tool=tool, channel=channel, signal=sig,
                     payload=gate_check, check="ceremony.recipe_gate")

    # 结构预检（目录展开 / 软链与保护根拒绝）→ 不可恢复性预检（dirty / GUID 引用）。
    # 展开后的每个子项都进分类，故 recursive 不会把子项的 dirty 检查绕过去。
    planned = plan_deletion(PROJECT_ROOT, checked, recursive=recursive,
                            protected_roots=ALLOWED_PREFIXES)
    ev = evaluate(PROJECT_ROOT, planned["checked"])
    planned["status"] = ev["status"]
    blocked = planned["blocked"] + ev["blocked"]
    if blocked:
        first = blocked[0]
        state = str(first.get("state", "unsafe"))
        structural = any(item.get("state") in _STRUCTURE_STATES for item in blocked)
        emit(op="delete", tool=tool, channel=channel, outcome="denied",
             signal=f"deny.deletion.{state}",
             check=("deletion.guid-referenced" if state == "guid-referenced"
                    else "deletion.structure" if structural else "deletion.recoverability"),
             level="block", detail=str(first))
        return {
            "status": "DENIED", "error": "DELETION_UNSAFE", "blocked": blocked,
            "hint": "整批未执行。非空目录须 recursive=True；软链与作用域根不删；"
                    "tracked-dirty 先提交或还原；guid-referenced 先解除引用。",
        }

    expanded = [e["path"] for e in planned["entries"]]
    if dry_run:
        deletable = [e["path"] for e in planned["entries"] if e["kind"] != "missing"]
        emit(op="delete", tool=tool, channel=channel, outcome="dry_run",
             signal="dry_run.delete", recipe=gate_check.get("recipe", ""),
             detail=f"{len(deletable)} deletable")
        return {"status": "OK", "dry_run": True, "deletable": deletable,
                "count": len(deletable), "recipe": gate_check.get("recipe", "")}

    outcome = execute_deletion(PROJECT_ROOT, planned)
    deleted, failed = outcome["deleted"], outcome["failed"]
    deleted_paths = [d["path"] for d in deleted]
    rec = {"channel": channel, "paths": deleted_paths, "count": len(deleted),
           "requested_paths": planned["requested_paths"],
           "expanded_paths": expanded, "recursive": bool(recursive),
           "reason": reason.strip(), "recipe": gate_check.get("recipe", ""),
           "unrecoverable": [d["path"] for d in deleted if not d["recoverable"]]}
    if any(d.get("kind") == "directory" for d in deleted):
        rec["directories"] = [d["path"] for d in deleted if d.get("kind") == "directory"]
    if argv is not None:
        rec["argv"] = argv
    append_delete_audit(PROJECT_ROOT, rec)          # 落盘：删除不留产物
    if on_record:
        on_record(rec)

    if failed:
        emit(op="delete", tool=tool, channel=channel, outcome="denied",
             signal="deny.deletion.failed", check="deletion.recoverability",
             level="block", detail=str(failed[0]))
    else:
        emit(op="delete", tool=tool, channel=channel, outcome="ok", signal="ok.delete",
             recipe=gate_check.get("recipe", ""), detail=f"{len(deleted)} entries")

    response = {"status": "PARTIAL" if failed else "OK", "deleted": deleted,
                "count": len(deleted),
                "recipe": gate_check.get("recipe", ""),
                "passed": sorted(gate_check.get("passed", [])),
                "reason": reason.strip()}
    if failed:
        response["failed"] = failed
        response["error"] = "DELETE_FAILED"
        response["hint"] = "预检后路径发生变化或 I/O 失败；已删除的条目见 deleted，其余保留。"
    orphan = orphan_meta_warnings(PROJECT_ROOT, deleted_paths)
    if orphan:
        response["warnings"] = orphan
    return response


# ═══ move ═══

def move(moves: list, reason: str, *, channel: str, tool: str, gate,
         on_record=None, argv: list | None = None, dry_run: bool = False,
         workspace: dict | None = None) -> dict:
    """判定 + 执行移动。响应形状同 v2 ``move_gated``。"""
    if not reason or not reason.strip():
        return _deny(op="move", tool=tool, channel=channel, signal="deny.missing_reason",
                     payload={"status": "DENIED", "error": "MISSING_REASON",
                              "hint": "reason 必填 —— 移动必须留下可追溯的原因。"})
    if not moves:
        return _deny(op="move", tool=tool, channel=channel, signal="deny.empty_moves",
                     payload={"status": "DENIED", "error": "EMPTY_MOVES", "hint": "moves 为空。"})

    pairs: list = []
    for i, m in enumerate(moves):
        if not isinstance(m, dict):
            return _deny(op="move", tool=tool, channel=channel, signal="deny.bad_move_shape",
                         payload={"status": "DENIED", "error": "BAD_MOVE_SHAPE",
                                  "hint": f"moves[{i}] 应为对象 {{\"from\": ..., \"to\": ...}}。"})
        src, dst = str(m.get("from", "")).strip(), str(m.get("to", "")).strip()
        if not src or not dst:
            return _deny(op="move", tool=tool, channel=channel, signal="deny.bad_move_shape",
                         payload={"status": "DENIED", "error": "BAD_MOVE_SHAPE",
                                  "hint": f"moves[{i}] 的 from/to 都不能为空。"})
        for p in (src, dst):
            path_check = validate_path(p)
            if path_check["status"] != "OK":
                return _deny(op="move", tool=tool, channel=channel, signal="deny.scope.path",
                             payload={**path_check, "offending_path": p},
                             check="scope.path", path=p)
        pairs.append((src, dst))

    # 移动两端都要在边界内 —— 搬进来或搬出去都改了边界外的内容
    endpoints = [p for pair in pairs for p in pair]
    ws_deny = _workspace_deny(endpoints, workspace or {}, op="move", tool=tool, channel=channel)
    if ws_deny:
        return ws_deny

    gate_check = _gate(gate)
    if gate_check["status"] != "OK":
        sig = "deny.ceremony." + str(gate_check.get("error", "gate")).lower()
        return _deny(op="move", tool=tool, channel=channel, signal=sig,
                     payload=gate_check, check="ceremony.recipe_gate")

    planned = plan_moves(PROJECT_ROOT, pairs)
    if planned["blocked"]:
        first = planned["blocked"][0]
        emit(op="move", tool=tool, channel=channel, outcome="denied",
             signal=f"deny.move.{str(first.get('error', 'unsafe')).lower()}",
             check="move.presafety", level="block", detail=str(first))
        return {
            "status": "DENIED", "error": "MOVE_UNSAFE", "blocked": planned["blocked"],
            "hint": "整批未执行。TARGET_EXISTS 要替换请走 write_gated；"
                    "GUID_TAKEN 先解除占用；CHAINED_MOVE 拆成两批。",
        }

    if dry_run:
        emit(op="move", tool=tool, channel=channel, outcome="dry_run",
             signal="dry_run.move", recipe=gate_check.get("recipe", ""),
             detail=f"{len(planned['pairs'])} pair(s)")
        return {"status": "OK", "dry_run": True,
                "moves": [{"from": p["from"], "to": p["to"]} for p in planned["pairs"]],
                "count": len(planned["pairs"]), "recipe": gate_check.get("recipe", "")}

    result = execute_moves(PROJECT_ROOT, planned["pairs"])
    if result["status"] != "OK":
        emit(op="move", tool=tool, channel=channel, outcome="error", signal="error.move",
             detail=str(result.get("error", "")))
        return {"status": "ERROR", "error": f"写阶段失败，已回滚: {result['error']}",
                "rolled_back": result["rolled_back"], "hint": result["hint"]}

    moved, removed, failed = result["moved"], result["removed"], result["failed"]
    src_states = classify(PROJECT_ROOT, [m["from"] for m in moved])
    rec = {"channel": channel, "kind": "move", "moves": moved, "count": len(moved),
           "removed": removed, "reason": reason.strip(),
           "recipe": gate_check.get("recipe", ""),
           "carried_dirty": [p for p, info in src_states.items()
                             if info["state"] == "tracked-dirty"]}
    if argv is not None:
        rec["argv"] = argv
    append_move_audit(PROJECT_ROOT, rec)
    if on_record:
        on_record(rec)

    emit(op="move", tool=tool, channel=channel, outcome="ok", signal="ok.move",
         recipe=gate_check.get("recipe", ""), detail=f"{len(moved)} pair(s)")

    response = {"status": "OK", "moved": moved, "count": len(moved),
                "recipe": gate_check.get("recipe", ""),
                "passed": sorted(gate_check.get("passed", [])),
                "reason": reason.strip()}
    if failed:
        response["failed"] = failed
        response["hint"] = "新位置已写好；旧副本删除失败，内容没有丢失。"
    return response


# ═══ 通道适配 — CLI 读落盘的会话门禁状态 ═══

def state_gate_from_file(state_file: str | None = None) -> dict:
    """读 ``.mcp/state.json`` 的门禁视图 —— 与 ``SessionState.can_write`` 同语义。

    CLI / Codex 通道用: 门禁状态由 MCP 侧 ``gate_set_recipe`` + ``gate_pass`` 写入并落盘，
    故本函数只**消费**已通过的状态，不提供「过门禁」的入口。
    """
    import json
    from gate_center import RECIPES, STATE_FILE
    path = state_file or STATE_FILE
    relaxed = level_of("ceremony.recipe_gate") != "block"
    if not os.path.isfile(path):
        if relaxed:
            return {"status": "OK", "recipe": None, "passed": [],
                    "warnings": [{"id": "ceremony.recipe_gate", "level": "warning",
                                  "detail": "无 state.json（policy 已放宽）"}]}
        return {"status": "DENIED", "error": "NO_RECIPE",
                "hint": f"无 {path} —— 先用 MCP gate_set_recipe + gate_pass 过链。"}
    try:
        with open(path, encoding="utf-8") as f:
            st = json.load(f)
    except (OSError, ValueError):
        return {"status": "DENIED", "error": "STATE_UNREADABLE",
                "hint": f"{path} 不可读或损坏 —— 重走 MCP 门禁链。"}

    recipe = st.get("recipe")
    if not recipe or recipe not in RECIPES:
        if relaxed:
            return {"status": "OK", "recipe": None, "passed": [],
                    "warnings": [{"id": "ceremony.recipe_gate", "level": "warning",
                                  "detail": f"未声明有效配方（当前: {recipe!r}，policy 已放宽）"}]}
        return {"status": "DENIED", "error": "NO_RECIPE",
                "hint": f"state.json 未声明有效配方（当前: {recipe!r}）—— 先 gate_set_recipe。"}
    passed = set(st.get("passed", []))
    missing = [g for g in RECIPES[recipe] if g not in passed]
    if missing:
        if relaxed:
            return {"status": "OK", "recipe": recipe, "passed": sorted(passed),
                    "warnings": [{"id": "ceremony.recipe_gate", "level": "warning",
                                  "detail": f"配方 '{recipe}' 未过: {missing}（policy 已放宽）"}]}
        return {"status": "DENIED", "error": "GATE_NOT_PASSED", "recipe": recipe,
                "missing": missing, "passed": sorted(passed),
                "hint": f"配方 '{recipe}' 还需通过: {missing}"}
    return {"status": "OK", "recipe": recipe, "passed": sorted(passed)}
