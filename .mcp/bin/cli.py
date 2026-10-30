#!/usr/bin/env python3
"""统一入口 — 业务式管理下 agent 需要显式调用的全部动作。

平台原生 Write / Edit 已经是写入主通道（由 PreToolUse hook 判定），所以这里**不再有
write**：多一个写入通道就多一套判定口径。留下的是原生工具做不到的事：

    open    声明任务、沙箱与 profile（一次调用，取代 recipe + workspace + 两道门禁）
    expand  用户指出新地址后扩充沙箱（对话里确认，这里只记录）
    close   出口验收：按 profile 的完成义务核对证据
    delete  删除 → 进垃圾桶（可 restore），不再有「不可恢复性」硬拦
    move    .meta 成对的移动 / 改名；普通搬动也可以直接用 mv，这条只用来跨目录批量改名
    restore 从垃圾桶取回
    trash   查看垃圾桶
    status  当前会话、分区、策略
    report  摩擦账本聚合提案

用法:
    python3 .mcp/cli.py open --task "修复 SSC 云边缘" --profile shader \\
        --workspace Assets/Mine/Shaders/PostProcess/SSC \\
        --workspace Assets/Mine/Special/HLSL
    python3 .mcp/cli.py close
    python3 .mcp/cli.py close --waive verify --reason "纯逻辑改动，无需画面验证"
    python3 .mcp/cli.py expand --workspace Assets/Mine/Special/HLSL --reason "用户确认: 共享库要一起改"
    python3 .mcp/cli.py delete Assets/Mine/Shaders/X/Old.shader --reason "被 V2 取代"
    python3 .mcp/cli.py move <源> <目标> --reason "..."
    python3 .mcp/cli.py trash
    python3 .mcp/cli.py restore 20261028-101500-a1b2c3

退出码: 0 = 通过 / 1 = 被拦或未达出口 / 2 = 用法错误
"""

from __future__ import annotations

import argparse
import json
import os
import sys

# 入口在 .mcp/bin/，模块在 .mcp/lib/ —— 把 lib 插进搜索路径，import 名保持不变。
_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.join(os.path.dirname(_HERE), "lib")
if _LIB not in sys.path:
    sys.path.insert(0, _LIB)

import close as close_mod                                # noqa: E402
import judgement                                         # noqa: E402
import profiles                                          # noqa: E402
import session                                           # noqa: E402
import zones                                             # noqa: E402
from telemetry import record as emit                     # noqa: E402
from validation import policy                            # noqa: E402
from validation import trash                             # noqa: E402
from validation.moving import append_audit as append_move_audit, execute as execute_moves, \
    plan as plan_moves                                   # noqa: E402
from validation.unity_assets import classify             # noqa: E402

PROJECT_ROOT = zones.PROJECT_ROOT


def _print(obj: dict, ok_codes=("OK",)) -> int:
    print(json.dumps(obj, ensure_ascii=False, indent=2))
    return 0 if obj.get("status") in ok_codes else 1


# ═══ 分区视图 —— 由 zones.json 生成，不在文案里手写 ═══

def _zone_view(workspace: list[str]) -> dict:
    """按分区表生成「现在能写哪里」。手写这份文案会随分区表漂移。"""
    table: dict[str, list[str]] = {}
    for zone, prefix in zones.zone_prefixes():
        table.setdefault(zone, []).append(prefix)
    view = {zone: ", ".join(prefixes) for zone, prefixes in table.items()}
    view["asset"] = "沙箱内 + 已入库: " + (", ".join(workspace) if workspace
                                          else "(未声明 —— 资产区新建与未跟踪写入会被拒)")
    view["git_protected"] = ("分区表之外或沙箱外、已入库的仓库文件也可写（git 兜底，改错 checkout 取回）；"
                             "新建与未跟踪仍拒。来自 zones.json 的 git_protected")
    return view


# ═══ open / expand ═══

def _collect_workspace(raw_dirs: list[str]) -> tuple[list[str], dict | None]:
    """校验并归一 --workspace。返回 (目录表, 错误)。"""
    workspace = []
    for raw in raw_dirs or []:
        rel = zones.normalize(raw).rstrip("/")
        if not rel:
            continue
        if not os.path.isdir(os.path.join(PROJECT_ROOT, rel)):
            return [], {"status": "DENIED", "error": "WORKSPACE_MISSING",
                        "hint": f"沙箱目录不存在: {rel}。先建目录，或核对路径。"}
        if zones.zone_of(rel) != "asset":
            return [], {"status": "DENIED", "error": "WORKSPACE_NOT_ASSET",
                        "hint": f"{rel} 属于 {zones.zone_of(rel)} 区。沙箱只用于 "
                                f"Assets/Mine；记忆库与体系库本来就随时可写，不必声明；"
                                f"已入库的仓库文件也由 git 兜底放行。"}
        workspace.append(rel)
    return workspace, None


def cmd_open(args: argparse.Namespace) -> int:
    if not args.task or not args.task.strip():
        return _print({"status": "DENIED", "error": "MISSING_TASK",
                       "hint": "open 需要 --task \"<这次要做什么>\"。"})
    if args.profile not in profiles.PROFILES:
        return _print({"status": "DENIED", "error": "INVALID_PROFILE",
                       "hint": f"--profile 必须是 {profiles.names()} 之一。"})

    workspace, error = _collect_workspace(args.workspace)
    if error:
        return _print(error)

    if not workspace:
        # 资产类 profile 必须给沙箱；不给就退化成「整个 Assets/Mine 可写」，
        # 那正是 v3 想收窄的东西。（已入库文件另有 git 兜底，不靠这次声明。）
        if args.profile not in ("meta", "research"):
            return _print({"status": "DENIED", "error": "MISSING_WORKSPACE",
                           "hint": f"profile '{args.profile}' 需要一个 --workspace。"
                                   f"一次可给多个（共享库请一并给出）。"})

    session.open_task(args.task.strip(), workspace, args.profile)
    playbook = profiles.playbook(args.profile)
    emit(op="session", tool="cli.open", outcome="ok", signal="session.open",
         recipe=args.profile, detail=f"{args.task.strip()} | {workspace}")

    current = session.load()
    return _print({
        "status": "OK",
        "task": current["task"],
        "profile": args.profile,
        "workspace": workspace,
        "zones_writable_now": _zone_view(workspace),
        "playbook": playbook,
        "hint": "写入直接用平台 Write/Edit（hook 判定）。已入库文件由 git 兜底不受沙箱限制；"
                "沙箱管的是新建与未跟踪。收尾用 close 核对证据。",
    })


def cmd_expand(args: argparse.Namespace) -> int:
    """扩充本次任务的沙箱。**用户先在对话里指了地址**，这条命令只负责记录。

    v3 的分权设计是「agent 登记 → 人跑 CLI 批准」，实际一次没用过：确认本来就在对话里发生，
    多一道 agent 够不到的通道只是把确认变成遗忘。这里反过来 —— 确认留在对话，命令留痕。
    """
    if not args.reason or not args.reason.strip():
        return _print({"status": "DENIED", "error": "MISSING_REASON",
                       "hint": "--reason 要写用户确认的原话 —— 事后要能回答「这块地是谁给的」。"})
    if not args.workspace:
        return _print({"status": "DENIED", "error": "EMPTY_WORKSPACE",
                       "hint": "expand 需要 --workspace <目录>（可重复）。"})

    data = session.load()
    if not data.get("task"):
        return _print({"status": "DENIED", "error": "NO_OPEN_TASK",
                       "hint": "还没有打开任务。先 cli.py open ...，expand 只服务于当前任务。"})

    workspace, error = _collect_workspace(args.workspace)
    if error:
        return _print(error)

    result = session.expand_task(workspace, args.reason.strip())
    emit(op="session", tool="cli.expand", outcome="ok", signal="session.expand",
         recipe=data.get("profile") or "",
         detail=f"{result['added']} | {args.reason.strip()}")
    return _print({
        "status": "OK",
        "added": result["added"],
        "workspace": result["workspace"],
        "reason": args.reason.strip(),
        "zones_writable_now": _zone_view(result["workspace"]),
        "hint": ("已扩充。证据起点不变（仍是 open 的时间）。"
                 if result["added"] else "这些目录本来就在沙箱内，无需扩充。"),
    })


# ═══ close ═══

def cmd_close(args: argparse.Namespace) -> int:
    if args.waive:
        if not args.reason or not args.reason.strip():
            return _print({"status": "DENIED", "error": "MISSING_REASON",
                           "hint": "--waive 需要 --reason，豁免会记账，必须说明理由。"})
        session.add_waiver(args.waive, args.reason.strip())
        emit(op="close", tool="cli.close", outcome="warn", signal=f"waive.close.{args.waive}",
             detail=args.reason.strip())
        print(f"已豁免 {args.waive}（留痕于 session.json 与 friction.jsonl）。")

    result = close_mod.run(args.profile)
    if result["status"] == "OK":
        session.close_task()
    emit(op="close", tool="cli.close",
         outcome="ok" if result["status"] == "OK" else "denied",
         signal="close.ok" if result["status"] == "OK" else "close.blocked",
         recipe=result.get("profile") or "",
         detail=",".join(result["blocking"]))
    return _print(result)


# ═══ delete / move / restore / trash ═══

def cmd_delete(args: argparse.Namespace) -> int:
    if not args.reason or not args.reason.strip():
        return _print({"status": "DENIED", "error": "MISSING_REASON",
                       "hint": "删除必须留下原因（进审计与 manifest）。"})
    if not args.paths:
        return _print({"status": "DENIED", "error": "EMPTY_PATHS", "hint": "未给出路径。"})

    data = session.load()
    decision = judgement.decide_delete(args.paths, data.get("workspace", []))
    if decision["status"] != "OK":
        emit(op="delete", tool="cli.delete", outcome="denied",
             signal=f"deny.{decision.get('error', 'delete')}", detail=decision.get("hint", ""))
        return _print(decision)

    planned = trash.plan(PROJECT_ROOT, args.paths)
    if args.dry_run:
        return _print({"status": "OK", "dry_run": True,
                       "would_trash": [e["path"] for e in planned["entries"]
                                       if e["kind"] != "missing"],
                       "blocked": planned["blocked"]})

    result = trash.execute(PROJECT_ROOT, planned, args.reason.strip(),
                           channel="cli" if not args.channel else args.channel)
    emit(op="delete", tool="cli.delete",
         outcome="ok" if result["status"] == "OK" else "denied",
         signal="ok.delete" if result["status"] == "OK" else "deny.delete",
         detail=f"{result.get('count', 0)} entries -> {result.get('trash_id', '')}")
    return _print(result)


def cmd_move(args: argparse.Namespace) -> int:
    if not args.reason or not args.reason.strip():
        return _print({"status": "DENIED", "error": "MISSING_REASON",
                       "hint": "移动必须留下原因。"})
    if not args.pairs:
        return _print({"status": "DENIED", "error": "EMPTY_MOVES", "hint": "未给出路径。"})
    if len(args.pairs) % 2:
        return _print({"status": "DENIED", "error": "ODD_PATH_COUNT",
                       "hint": "路径必须成对（源 目标 源 目标 …）。"})

    pairs = [(args.pairs[i], args.pairs[i + 1]) for i in range(0, len(args.pairs), 2)]
    data = session.load()
    workspace = data.get("workspace", [])

    # 两端都要在边界内 —— 搬进来或搬出去都改了边界外的内容
    endpoints = [p for pair in pairs for p in pair]
    decision = judgement.decide_delete(endpoints, workspace)
    if decision["status"] != "OK":
        return _print(decision)

    planned = plan_moves(PROJECT_ROOT, pairs)
    if planned["blocked"]:
        emit(op="move", tool="cli.move", outcome="denied", signal="deny.move",
             detail=str(planned["blocked"][0]))
        return _print({"status": "DENIED", "error": "MOVE_UNSAFE",
                       "blocked": planned["blocked"],
                       "hint": "整批未执行。TARGET_EXISTS 要替换请直接写目标文件；"
                               "GUID_TAKEN 先解除占用；CHAINED_MOVE 拆成两批。"})
    if args.dry_run:
        return _print({"status": "OK", "dry_run": True,
                       "moves": [{"from": p["from"], "to": p["to"]} for p in planned["pairs"]]})

    result = execute_moves(PROJECT_ROOT, planned["pairs"])
    if result["status"] != "OK":
        emit(op="move", tool="cli.move", outcome="error", signal="error.move",
             detail=str(result.get("error", "")))
        return _print({"status": "ERROR", "error": f"写阶段失败，已回滚: {result['error']}",
                       "rolled_back": result["rolled_back"], "hint": result["hint"]})

    src_states = classify(PROJECT_ROOT, [m["from"] for m in result["moved"]])
    record = {"channel": "cli", "kind": "move", "moves": result["moved"],
              "count": len(result["moved"]), "removed": result["removed"],
              "reason": args.reason.strip(),
              "carried_dirty": [p for p, info in src_states.items()
                                if info["state"] == "tracked-dirty"]}
    append_move_audit(PROJECT_ROOT, record)
    emit(op="move", tool="cli.move", outcome="ok", signal="ok.move",
         detail=f"{len(result['moved'])} pair(s)")
    out = {"status": "OK", "moved": result["moved"], "count": len(result["moved"]),
           "reason": args.reason.strip()}
    if result["failed"]:
        out["failed"] = result["failed"]
        out["hint"] = "新位置已写好；旧副本删除失败，内容没有丢失。"
    return _print(out)


def cmd_trash(args: argparse.Namespace) -> int:
    buckets = trash.list_buckets(PROJECT_ROOT)
    return _print({"status": "OK", "buckets": buckets, "count": len(buckets),
                   "root": trash.TRASH_DIR,
                   "hint": "恢复: cli.py restore <id>；彻底清空: cli.py trash --empty [id]"
                           if buckets else "垃圾桶为空。"})


def cmd_restore(args: argparse.Namespace) -> int:
    if not args.trash_id:
        return _print({"status": "DENIED", "error": "MISSING_ID",
                       "hint": "restore 需要垃圾桶 id（见 cli.py trash）。"})
    result = trash.restore(PROJECT_ROOT, args.trash_id, only=args.only or None)
    emit(op="restore", tool="cli.restore",
         outcome="ok" if result["status"] == "OK" else "warn",
         signal="ok.restore" if result["status"] == "OK" else "warn.restore",
         detail=f"{result.get('count', 0)} 项")
    return _print(result)


# ═══ status ═══

def cmd_status(_args: argparse.Namespace) -> int:
    data = session.load()
    spec = profiles.PROFILES.get(data.get("profile") or "") or {}
    return _print({
        "status": "OK",
        "session": {"task": data.get("task"), "profile": data.get("profile"),
                    "workspace": data.get("workspace", []),
                    "opened_at": data.get("opened_at"),
                    "waivers": data.get("waivers", []),
                    "expansions": data.get("expansions", [])},
        "obligations": spec.get("obligations", []),
        "zones": _zone_view(data.get("workspace", [])),
        "policy": {k: {"level": v["level"], "why": v["why"]}
                   for k, v in policy.snapshot().items()},
        "profiles": profiles.names(),
    })


# ═══ 入口 ═══

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="cli.py", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    p_open = sub.add_parser("open", help="声明任务、沙箱与 profile")
    p_open.add_argument("--task", required=True, help="这次要做什么（一句话）")
    p_open.add_argument("--profile", required=True, choices=profiles.names())
    p_open.add_argument("--workspace", action="append", default=[],
                        help="沙箱目录（可重复；共享库请一并给出）")
    p_open.set_defaults(func=cmd_open)

    p_expand = sub.add_parser("expand", help="扩充本次任务的沙箱（用户对话确认后执行）")
    p_expand.add_argument("--workspace", action="append", default=[],
                          help="要加入沙箱的目录（可重复）")
    p_expand.add_argument("--reason", required=True,
                          help="用户确认的原话 / 为什么需要这块地")
    p_expand.set_defaults(func=cmd_expand)

    p_close = sub.add_parser("close", help="出口验收")
    p_close.add_argument("--profile", default=None, help="覆盖会话 profile")
    p_close.add_argument("--waive", default="", help="豁免某项义务（须配 --reason）")
    p_close.add_argument("--reason", default="", help="豁免理由")
    p_close.set_defaults(func=cmd_close)

    p_delete = sub.add_parser("delete", help="删除 → 垃圾桶（可恢复）")
    p_delete.add_argument("paths", nargs="+")
    p_delete.add_argument("--reason", required=True)
    p_delete.add_argument("--dry-run", action="store_true")
    p_delete.add_argument("--channel", default="", help=argparse.SUPPRESS)
    p_delete.set_defaults(func=cmd_delete)

    p_move = sub.add_parser("move", help="移动 / 改名（.meta 成对）")
    p_move.add_argument("pairs", nargs="+", metavar="from to")
    p_move.add_argument("--reason", required=True)
    p_move.add_argument("--dry-run", action="store_true")
    p_move.set_defaults(func=cmd_move)

    p_trash = sub.add_parser("trash", help="查看垃圾桶")
    p_trash.add_argument("--empty", nargs="?", const="", default=None,
                         help="彻底清空（不可恢复）；给 id 只清那一桶")
    p_trash.set_defaults(func=lambda a: cmd_trash(a) if a.empty is None
                         else _print(trash.empty(PROJECT_ROOT, a.empty or None)))

    p_restore = sub.add_parser("restore", help="从垃圾桶恢复")
    p_restore.add_argument("trash_id")
    p_restore.add_argument("--only", action="append", default=[],
                           help="只恢复指定路径（可重复）")
    p_restore.set_defaults(func=cmd_restore)

    p_status = sub.add_parser("status", help="当前会话、分区与策略")
    p_status.set_defaults(func=cmd_status)

    p_report = sub.add_parser("report", help="摩擦账本聚合提案")
    p_report.add_argument("--json", action="store_true")
    p_report.add_argument("--since", type=int, default=7, help="最近 N 天")
    p_report.set_defaults(func=lambda a: _run_report(a))

    return parser


def _run_report(args: argparse.Namespace) -> int:
    from validation import friction_report
    argv = []
    if args.json:
        argv.append("--json")
    argv += ["--since", str(args.since)]
    return friction_report.main(argv)


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
