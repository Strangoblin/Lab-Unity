#!/usr/bin/env python3
"""移动门禁 CLI — move_gated 的 Bash 通道.

与 MCP 侧同一道门禁、同一套判定、同一套执行（都走 moving.plan + moving.execute，
不允许各判一套，否则两条通道必然分叉）:

  MCP 侧: mcp__unity-gate__move_gated
  Bash 侧: 本 CLI —— 供 MCP 工具表未刷新、或需要脚本化批量移动时使用

为什么需要这条通道：Claude Code 的 **MCP 工具表按会话冻结**——服务器进程跑的是新代码，
工具却调不到，需 `/mcp` 重连。删除通道当初就是为这个场景补的，移动同理。

门禁（三步全过才放行）:
  1. 路径作用域 — validate_path（源与目标都要在作用域内）
  2. 配方门禁   — 读 .mcp/state.json：配方已声明 + 该配方门禁全过（与 can_write 同语义）
  3. 后果验证   — moving.plan：净效果判定（零改动预检）

用法:
  python3 .mcp/validation/move_gated.py --reason "<原因>" <from> <to> [<from> <to> ...]
  python3 .mcp/validation/move_gated.py --reason "<原因>" --dry-run <from> <to> ...
退出码:
  0 = 移动成功（--dry-run 时为「可移动」）
  1 = 被拦（门禁 / 作用域 / 预检）
  2 = 用法错误
"""

import sys, os, json

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)                        # 同级: project_paths / moving
sys.path.insert(0, os.path.dirname(_HERE))       # 上级: gate_center

from project_paths import validate_path, PROJECT_ROOT
from moving import plan, execute, append_audit
from gate_center import RECIPES, STATE_FILE


def gate_check() -> dict:
    """按持久化的 state.json 判定配方门禁 —— 与 can_write 同语义（与 delete_gated.py 同源）。"""
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
        print("ERROR: --reason 必填 —— 移动必须留下可追溯的原因")
        return 2
    if len(paths) % 2 != 0:
        print(json.dumps({"status": "DENIED", "error": "ODD_PATH_COUNT",
                          "hint": f"路径必须成对给出（源 目标 源 目标 …），收到 {len(paths)} 个。"},
                         ensure_ascii=False))
        return 2

    pairs = [(paths[i], paths[i + 1]) for i in range(0, len(paths), 2)]

    # ── 1. 路径作用域：源与目标都过同一个 validate_path ──
    for p in paths:
        r = validate_path(p)
        if r["status"] != "OK":
            print(json.dumps({**r, "offending_path": p}, ensure_ascii=False))
            return 1

    # ── 2. 配方门禁（与 MCP 同一道闸，读同一份落盘状态）──
    gate = gate_check()
    if gate["status"] != "OK":
        print(json.dumps(gate, ensure_ascii=False))
        return 1

    # ── 3. 后果验证：净效果预检（零改动）──
    planned = plan(PROJECT_ROOT, pairs)
    if planned["blocked"]:
        print(json.dumps({"status": "DENIED", "error": "MOVE_UNSAFE",
                          "blocked": planned["blocked"],
                          "hint": "整批未执行。TARGET_EXISTS 要替换请走 write_gated；"
                                  "GUID_TAKEN 先解除占用；CHAINED_MOVE 拆成两批。"},
                         ensure_ascii=False))
        return 1

    if dry_run:
        print(json.dumps({"status": "OK", "dry_run": True,
                          "moves": [{"from": p["from"], "to": p["to"]} for p in planned["pairs"]],
                          "count": len(planned["pairs"]), "recipe": gate["recipe"]},
                         ensure_ascii=False))
        return 0

    # ── 4. 执行（与 MCP 共用同一个 execute）──
    result = execute(PROJECT_ROOT, planned["pairs"])
    if result["status"] != "OK":
        print(json.dumps({"status": "ERROR", "error": f"写阶段失败，已回滚: {result['error']}",
                          "rolled_back": result["rolled_back"], "hint": result["hint"]},
                         ensure_ascii=False))
        return 1

    # ── 5. 审计落盘 —— 移动丢的是「哪个变成了哪个」，日志是唯一痕迹 ──
    record = {"channel": "cli", "kind": "move", "moves": result["moved"],
              "count": len(result["moved"]), "removed": result["removed"],
              "reason": reason.strip(), "recipe": gate["recipe"], "argv": sys.argv[1:],
              "carried_dirty": []}
    append_audit(PROJECT_ROOT, record)

    out = {"status": "OK", "moved": result["moved"], "count": len(result["moved"]),
           "recipe": gate["recipe"], "reason": reason.strip()}
    if result["failed"]:
        out["failed"] = result["failed"]
        out["hint"] = "新位置已写好；旧副本删除失败，内容没有丢失。"
    print(json.dumps(out, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
