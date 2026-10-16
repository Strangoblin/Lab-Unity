#!/usr/bin/env python3
"""统一门禁 CLI — write / delete / move 三条操作，一个入口，同一判定核（MCP v3）.

为什么存在: v2 的通道分散在 MCP 工具 + 两个 Bash CLI 里，而写入**没有** CLI ——
一旦 MCP 工具表按会话冻结（Claude Code 的已知行为），写入就成了唯一断掉的操作。
本 CLI 让三条操作都在同一个传输无关的判定核上（``validation/judgement.py``），
于是 Claude 与 Codex 都能用同一条路，工具表冻结也不再等于能力缺失。

判定与记账全部在 ``judgement`` 内完成 —— 本文件只解析参数、打印、给退出码。

用法:
  # 写入（内容来自本地文件，或 stdin）
  python3 .mcp/validation/gate_cli.py write --path <项目相对路径> --from <本地文件> [注解...]
  python3 .mcp/validation/gate_cli.py write --path <项目相对路径> -               # stdin
  echo "..." | python3 .mcp/validation/gate_cli.py write --path <p> -

  # 删除 / 移动
  python3 .mcp/validation/gate_cli.py delete --reason "..." [--recursive] [--dry-run] <path>...
  python3 .mcp/validation/gate_cli.py move   --reason "..." [--dry-run] <from> <to> ...

  # 工作目录
  python3 .mcp/validation/gate_cli.py workspace set Assets/Mine/Scripts
  python3 .mcp/validation/gate_cli.py workspace request Assets/Mine/Shaders/X --reason "需要改 X"
  python3 .mcp/validation/gate_cli.py workspace approve ws-1a2b3c4d          # 人工动作
  python3 .mcp/validation/gate_cli.py workspace reject  ws-1a2b3c4d --reason "..."
  python3 .mcp/validation/gate_cli.py workspace show

  # 自省
  python3 .mcp/validation/gate_cli.py status
  python3 .mcp/validation/gate_cli.py report [--json] [--since N]

写入注解（可选，非阻塞，进审计）:
  --mode M   --script-decision D   --file-type T   --category C   --effect E

退出码: 0 = 通过 / 1 = 被拦或失败 / 2 = 用法错误
"""

from __future__ import annotations

import json
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_MCP = os.path.dirname(_HERE)
for _p in (_HERE, _MCP):
    if _p not in sys.path:
        sys.path.insert(0, _p)

from validation import judgement                 # noqa: E402
from validation.project_paths import PROJECT_ROOT  # noqa: E402


def _flag(argv: list, name: str, default: str = "") -> str:
    if name in argv:
        i = argv.index(name)
        if i + 1 >= len(argv):
            raise SystemExit(f"ERROR: {name} 缺少值")
        return argv[i + 1]
    return default


def _strip_flags(argv: list, takes_value: tuple) -> list:
    out, i = [], 0
    while i < len(argv):
        a = argv[i]
        if a in takes_value:
            i += 2
            continue
        if a.startswith("--"):
            i += 1
            continue
        out.append(a)
        i += 1
    return out


def _emit(obj: dict, code_if_ok: int = 0) -> int:
    print(json.dumps(obj, ensure_ascii=False))
    return code_if_ok if obj.get("status") in ("OK", "NEEDS_APPROVAL") else 1


# ═══ write ═══

def cmd_write(argv: list) -> int:
    takes = ("--path", "--from", "--content", "--mode", "--script-decision",
             "--file-type", "--category", "--effect")
    path = _flag(argv, "--path")
    if not path:
        print("ERROR: write 需要 --path <项目相对路径>")
        return 2

    if "--content" in argv:
        content = _flag(argv, "--content")
    elif "--from" in argv:
        src = _flag(argv, "--from")
        try:
            with open(src, encoding="utf-8") as f:
                content = f.read()
        except OSError as e:
            print(f"ERROR: 读不到 --from 文件: {e}")
            return 2
    elif "-" in _strip_flags(argv, takes):
        content = sys.stdin.read()
    else:
        print("ERROR: write 需要内容来源（--from <文件> | --content <串> | - 读 stdin）")
        return 2

    result = judgement.write(
        path, content, channel="cli", tool="gate_cli.write",
        gate=judgement.state_gate_from_file(),
        workspace=judgement.workspace_from_file(),
        mode=_flag(argv, "--mode"), script_decision=_flag(argv, "--script-decision"),
        file_type=_flag(argv, "--file-type"), category=_flag(argv, "--category"),
        effect=_flag(argv, "--effect"), dry_run="--dry-run" in argv)
    return _emit(result)


# ═══ delete / move ═══

def cmd_delete(argv: list) -> int:
    reason = _flag(argv, "--reason") or _flag(argv, "-r")
    if argv and "--reason" not in argv and "-r" not in argv:
        print("ERROR: delete 需要 --reason <原因>")
        return 2
    paths = _strip_flags(argv, ("--reason", "-r"))
    if not paths:
        print("ERROR: delete 未给出路径")
        return 2
    result = judgement.delete(
        paths, reason, channel="cli", tool="gate_cli.delete",
        gate=judgement.state_gate_from_file(),
        workspace=judgement.workspace_from_file(), argv=argv,
        dry_run="--dry-run" in argv, recursive="--recursive" in argv)
    return _emit(result)


def cmd_move(argv: list) -> int:
    reason = _flag(argv, "--reason") or _flag(argv, "-r")
    if argv and "--reason" not in argv and "-r" not in argv:
        print("ERROR: move 需要 --reason <原因>")
        return 2
    rest = _strip_flags(argv, ("--reason", "-r"))
    if not rest:
        print("ERROR: move 未给出路径")
        return 2
    if len(rest) % 2:
        print(json.dumps({"status": "DENIED", "error": "ODD_PATH_COUNT",
                          "hint": f"路径必须成对给出（源 目标 源 目标 …），收到 {len(rest)} 个。"},
                         ensure_ascii=False))
        return 2
    moves = [{"from": rest[i], "to": rest[i + 1]} for i in range(0, len(rest), 2)]
    result = judgement.move(
        moves, reason, channel="cli", tool="gate_cli.move",
        gate=judgement.state_gate_from_file(),
        workspace=judgement.workspace_from_file(), argv=argv,
        dry_run="--dry-run" in argv)
    return _emit(result)


# ═══ status / report ═══

def cmd_workspace(argv: list) -> int:
    from gate_center import state
    if not argv or argv[0] in ("-h", "--help"):
        print("workspace: set <dir> | request <dir> --reason R | "
              "approve <id> [--by 名] | reject <id> [--reason R] | show")
        return 0 if argv else 2
    action, rest = argv[0], argv[1:]
    if action == "set":
        if not rest:
            print("ERROR: workspace set 需要 <目录>")
            return 2
        return _emit(state.set_workspace(rest[0]))
    if action == "request":
        target = _strip_flags(rest, ("--reason", "-r"))
        if not target:
            print("ERROR: workspace request 需要 <目录>")
            return 2
        return _emit(state.request_workspace(target[0], _flag(rest, "--reason")))
    if action == "approve":
        if not rest:
            print("ERROR: workspace approve 需要 <request_id>")
            return 2
        return _emit(state.approve_workspace(rest[0], _flag(rest, "--by", "human")))
    if action == "reject":
        if not rest:
            print("ERROR: workspace reject 需要 <request_id>")
            return 2
        return _emit(state.reject_workspace(rest[0], _flag(rest, "--reason")))
    if action == "show":
        return _emit({"status": "OK", "workspace": state.workspace_view()})
    print(f"ERROR: 未知 workspace 子命令 '{action}'")
    return 2


def cmd_status(argv: list) -> int:
    from gate_center import GATE_REGISTRY, RECIPES, STATE_FILE
    from validation import policy
    data = {"status_file": STATE_FILE, "recipes": RECIPES,
            "gates": {g: {"name": d["name"], "retired": d.get("retired", False)}
                      for g, d in GATE_REGISTRY.items()},
            "policy": policy.snapshot()}
    if os.path.isfile(STATE_FILE):
        try:
            with open(STATE_FILE, encoding="utf-8") as f:
                data["state"] = json.load(f)
        except (OSError, ValueError):
            data["state"] = None
    data["gate_view"] = judgement.state_gate_from_file()
    data["workspace"] = judgement.workspace_from_file()
    return _emit({"status": "OK", **data})


def cmd_report(argv: list) -> int:
    from validation import friction_report
    return friction_report.main(argv)


USAGE = __doc__


def main(argv: list | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if not argv or argv[0] in ("-h", "--help"):
        print(USAGE)
        return 0 if argv else 2
    cmd, rest = argv[0], argv[1:]
    if cmd == "write":
        return cmd_write(rest)
    if cmd == "delete":
        return cmd_delete(rest)
    if cmd == "move":
        return cmd_move(rest)
    if cmd == "workspace":
        return cmd_workspace(rest)
    if cmd == "status":
        return cmd_status(rest)
    if cmd == "report":
        return cmd_report(rest)
    print(f"ERROR: 未知子命令 '{cmd}'")
    print(USAGE)
    return 2


if __name__ == "__main__":
    sys.exit(main())
