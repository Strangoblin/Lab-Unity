#!/usr/bin/env python3
"""删除门禁 CLI — delete_gated 的 Bash 通道.

同一道门禁的两个通道，判定同源（都走 deletion.evaluate）:
  MCP 侧: mcp__unity-gate__delete_gated
  Bash 侧: 本 CLI —— 供 MCP 工具表未刷新、或需要脚本化批量删除时使用

门禁（三步全过才放行）:
  1. 路径作用域 — validate_path（Assets/Mine、roslyn scripts、tmp）
  2. 配方门禁   — 读 .mcp/state.json：配方已声明 + 该配方门禁全过（与 can_write 同语义）
  3. 后果验证   — deletion.evaluate：不可恢复性 + .meta GUID 引用

整批 all-or-nothing。删除不留产物，故每次成功都追加审计到 .mcp/deletes.jsonl。

用法:
  python3 .mcp/validation/delete_gated.py --reason "<原因>" <path> [<path> ...]
  python3 .mcp/validation/delete_gated.py --reason "<原因>" --dry-run <path> ...
退出码:
  0 = 删除成功（--dry-run 时为「可删」）
  1 = 被拦（门禁 / 作用域 / 不可恢复性）
  2 = 用法错误
"""

import sys, os, json

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)                        # 同级: project_paths / deletion
sys.path.insert(0, os.path.dirname(_HERE))       # 上级: gate_center

from project_paths import validate_path, PROJECT_ROOT
from deletion import evaluate, append_audit, orphan_meta_warnings
from gate_center import RECIPES, STATE_FILE


def gate_check() -> dict:
    """按持久化的 state.json 判定配方门禁 —— 与 SessionState.can_write 同语义。

    门禁状态由 MCP 侧 gate_set_recipe + gate_pass 写入并落盘，
    故本 CLI 不提供「过门禁」的入口，只能消费已通过的状态。
    """
    if not os.path.isfile(STATE_FILE):
        return {"status": "DENIED", "error": "NO_RECIPE",
                "hint": f"无 {STATE_FILE} —— 先用 MCP gate_set_recipe + gate_pass 过链。"}
    try:
        with open(STATE_FILE, encoding="utf-8") as f:
            st = json.load(f)
    except (OSError, ValueError):
        return {"status": "DENIED", "error": "STATE_UNREADABLE",
                "hint": f"{STATE_FILE} 不可读或损坏 —— 重走 MCP 门禁链。"}

    recipe = st.get("recipe")
    if not recipe or recipe not in RECIPES:
        return {"status": "DENIED", "error": "NO_RECIPE",
                "hint": f"state.json 未声明有效配方（当前: {recipe!r}）—— 先 gate_set_recipe。"}
    passed = set(st.get("passed", []))
    missing = [g for g in RECIPES[recipe] if g not in passed]
    if missing:
        return {"status": "DENIED", "error": "GATE_NOT_PASSED", "recipe": recipe,
                "missing": missing, "passed": sorted(passed),
                "hint": f"配方 '{recipe}' 还需通过: {missing}"}
    return {"status": "OK", "recipe": recipe, "passed": sorted(passed)}


def _parse(argv: list) -> tuple:
    """→ (paths, reason, dry_run) ; 出错时 reason 置 None 并已打印用法。"""
    paths, reason, dry_run, i = [], "", False, 0
    while i < len(argv):
        a = argv[i]
        if a in ("--reason", "-r"):
            i += 1
            if i >= len(argv):
                print("ERROR: --reason 缺少值")
                return [], None, False
            reason = argv[i]
        elif a == "--dry-run":
            dry_run = True
        elif a in ("-h", "--help"):
            print(__doc__)
            return [], None, False
        elif a.startswith("-"):
            print(f"ERROR: 未知参数 {a}")
            return [], None, False
        else:
            paths.append(a)
        i += 1
    return paths, reason, dry_run


def main() -> int:
    paths, reason, dry_run = _parse(sys.argv[1:])

    if reason is None:
        print(__doc__)
        return 2
    if not paths:
        print("ERROR: 未给出路径")
        print(__doc__)
        return 2
    if not reason.strip():
        print("ERROR: --reason 必填 —— 删除必须留下可追溯的原因")
        return 2

    # ── 1. 路径作用域（与 MCP 同一个 validate_path）──
    checked: list = []
    for p in paths:
        r = validate_path(p)
        if r["status"] != "OK":
            print(json.dumps({**r, "offending_path": p}, ensure_ascii=False))
            return 1
        checked.append((p, r["full_path"]))

    # ── 2. 配方门禁（与 MCP 同一道闸，读同一份落盘状态）──
    gate = gate_check()
    if gate["status"] != "OK":
        print(json.dumps(gate, ensure_ascii=False))
        return 1

    # ── 3. 后果验证: 不可恢复性 + GUID 引用（与 MCP 同一个 evaluate）──
    ev = evaluate(PROJECT_ROOT, checked)
    if ev["blocked"]:
        print(json.dumps({"status": "DENIED", "error": "DELETION_UNSAFE",
                          "blocked": ev["blocked"],
                          "hint": "整批未执行。tracked-dirty 先提交或还原；"
                                  "guid-referenced 先解除引用。"}, ensure_ascii=False))
        return 1

    status = ev["status"]

    # ── 4. 执行（missing 跳过 —— 批量清单可重复执行）──
    if dry_run:
        deletable = [p for p, _ in checked if status[p]["state"] != "missing"]
        print(json.dumps({"status": "OK", "dry_run": True, "deletable": deletable,
                          "count": len(deletable), "recipe": gate["recipe"]},
                         ensure_ascii=False))
        return 0

    deleted, failed = [], []
    for p, full in checked:
        if status[p]["state"] == "missing":
            continue
        try:
            os.remove(full)
            deleted.append({"path": p, "state": status[p]["state"],
                            "recoverable": status[p]["recoverable"]})
        except OSError as e:
            failed.append({"path": p, "error": str(e)})

    deleted_paths = [d["path"] for d in deleted]
    append_audit(PROJECT_ROOT, {
        "channel": "cli", "paths": deleted_paths, "count": len(deleted),
        "reason": reason.strip(), "recipe": gate["recipe"],
        "unrecoverable": [d["path"] for d in deleted if not d["recoverable"]],
        "argv": sys.argv[1:],
    })

    out = {"status": "OK", "deleted": deleted, "count": len(deleted),
           "recipe": gate["recipe"], "reason": reason.strip()}
    if failed:
        out["failed"] = failed
    warnings = orphan_meta_warnings(PROJECT_ROOT, deleted_paths)
    if warnings:
        out["warnings"] = warnings
    print(json.dumps(out, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
