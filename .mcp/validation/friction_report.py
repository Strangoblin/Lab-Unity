#!/usr/bin/env python3
"""摩擦报告 — 把信号账本聚合成「框架该往哪改」的提案（MCP v3）.

输入: `.mcp/friction.jsonl`（telemetry 落账）
输出: 事实（各信号计数、仪式开销比）+ 提案（调级 / 退役 / 待观察）

它不是自动调参器 —— 提案必须由 meta-developer 复核、人工确认后才改 ``policy.json``。
它解决的是**触发问题**: 以前框架升级等人「想起来」，现在有账本就有触发点与排序依据。

用法:
  python3 .mcp/validation/friction_report.py            # 人读格式
  python3 .mcp/validation/friction_report.py --json     # 机器格式
  python3 .mcp/validation/friction_report.py --since 7  # 只看最近 7 天
退出码（lint 风格）: 0 = 无 review 级提案（健康或样本不足）/ 1 = 有提案待办
"""

from __future__ import annotations

import json
import os
import sys
import time
from collections import Counter

_MCP = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _MCP not in sys.path:
    sys.path.insert(0, _MCP)

# 一律走 `validation.*` 包名 —— 若这里用顶层 `policy`，会与 judgement/gate_center 的
# `validation.policy` 形成**两个模块实例**（各自一份缓存），reload 只清一个。
from telemetry import read_all                          # noqa: E402
from validation.policy import snapshot as policy_snapshot   # noqa: E402

# 判定「样本够了」的最小真实操作数 —— 样本不足不出提案，避免用 3 次写入下结论
MIN_OPS = 20
# 真实操作数足够后，零命中的检查才提「退役/降级」
CEREMONY_RATIO_WARN = 0.30


def _cutoff(days: int | None) -> int:
    return 0 if not days else int(time.time() * 1000) - days * 86400_000


def aggregate(entries: list, since_days: int | None = None) -> dict:
    cutoff = _cutoff(since_days)
    rows = [e for e in entries if int(e.get("ts", 0)) >= cutoff]

    by_signal = Counter(e.get("signal", "") for e in rows)
    by_outcome = Counter(e.get("outcome", "") for e in rows)
    by_op = Counter(e.get("op", "") for e in rows)
    def _key(e):
        return e.get("check") or e.get("signal", "")
    by_check_deny = Counter(_key(e) for e in rows if e.get("outcome") == "denied")
    by_check_warn = Counter(_key(e) for e in rows if e.get("outcome") in ("warn", "muted"))

    gate_calls = sum(n for sig, n in by_signal.items() if sig.startswith("ceremony."))
    # 真实操作 = write/delete/move 的判定（ok + denied）；dry-run 与 ceremony 都不计
    real_ops = sum(1 for e in rows
                   if e.get("op") in ("write", "delete", "move")
                   and e.get("outcome") in ("ok", "denied"))
    ratio = (gate_calls / real_ops) if real_ops else 0.0

    policy = policy_snapshot()
    observed_checks = set(by_check_deny) | set(by_check_warn) | {
        e.get("check") for e in rows if e.get("check")}
    observed_checks.discard("")

    return {
        "window_days": since_days,
        "records": len(rows),
        "by_outcome": dict(by_outcome),
        "by_op": dict(by_op),
        "by_signal": dict(by_signal.most_common(30)),
        "by_check_deny": dict(by_check_deny.most_common()),
        "by_check_warn": dict(by_check_warn.most_common()),
        "ceremony_calls": gate_calls,
        "real_ops": real_ops,
        "ceremony_ratio": round(ratio, 3),
        "policy": policy,
        "never_fired": sorted(set(policy) - observed_checks),
        "sample_sufficient": real_ops >= MIN_OPS,
    }


def proposals(agg: dict) -> list:
    out: list = []
    if not agg["sample_sufficient"]:
        out.append({
            "kind": "insufficient-sample",
            "severity": "info",
            "detail": f"真实操作 {agg['real_ops']} < {MIN_OPS}，暂不出调级提案（避免小样本下结论）",
        })
        return out

    # 1) 仪式开销 —— agent 已能自主读知识时，让它继续为「声明」付费就是纯税。
    #    只在仍为 block 时提：已降级的项再提一次就是噪音（升级由事故驱动，不等样本）。
    gate_level = (agg["policy"].get("ceremony.recipe_gate") or {}).get("level")
    if agg["ceremony_ratio"] > CEREMONY_RATIO_WARN and gate_level == "block":
        out.append({
            "kind": "demote", "check": "ceremony.recipe_gate", "severity": "review",
            "detail": f"仪式调用 {agg['ceremony_calls']} / 真实操作 {agg['real_ops']} "
                      f"= {agg['ceremony_ratio']}（阈值 {CEREMONY_RATIO_WARN}）。"
                      f"复核 ceremony.recipe_gate 能否降为 warn。",
        })

    # 2) 从未命中的检查 —— 安全类零命中是好事，其余才是候选
    for cid in agg["never_fired"]:
        info = agg["policy"].get(cid, {})
        if not info.get("retirable", True):
            if info.get("level") == "block":
                out.append({
                    "kind": "safety-healthy", "check": cid, "severity": "info",
                    "detail": f"{cid}（block）零命中 —— 不可退役类，"
                              f"零命中 = 没有不安全/越界的操作，属健康信号。",
                })
            else:
                out.append({
                    "kind": "observe", "check": cid, "severity": "info",
                    "detail": f"{cid}（{info.get('level')}）零命中 —— 已放宽且不可退役，继续观察。",
                })
        elif info.get("level") == "block":
            out.append({
                "kind": "retire-or-demote", "check": cid, "severity": "review",
                "detail": f"{cid}（block）在 {agg['real_ops']} 次真实操作中零命中 —— "
                          f"复核是否退役，或降为 warn 观察。",
            })
        else:
            out.append({
                "kind": "observe", "check": cid, "severity": "info",
                "detail": f"{cid}（{info.get('level')}）零命中 —— 已是放宽态，继续观察。",
            })

    # 3) 高频拒绝 —— 未必是错，但值得看是不是误拦（尤其是风格类，非正确性类）
    for cid, n in agg["by_check_deny"].items():
        if not cid:
            continue
        if n >= max(5, agg["real_ops"] * 0.2) and cid.startswith(("norm.", "placement.")):
            out.append({
                "kind": "inspect-denies", "check": cid, "severity": "review",
                "detail": f"{cid} 拒绝 {n} 次（占真实操作 {n / max(agg['real_ops'], 1):.0%}）。"
                          f"抽样看是否误拦；若多为误拦 → 降级。",
            })

    # 4) NO_RECIPE / GATE_NOT_PASSED 反复出现 —— 修恢复，不是放宽门禁
    churn = sum(n for sig, n in agg["by_signal"].items()
                if sig in ("deny.ceremony.no_recipe", "deny.ceremony.gate_not_passed"))
    if churn >= 5:
        out.append({
            "kind": "fix-plumbing", "check": "ceremony.recipe_gate", "severity": "review",
            "detail": f"NO_RECIPE/GATE_NOT_PASSED 出现 {churn} 次 —— 属状态恢复问题"
                      f"（见 state.json 持久化），应修管线而非放宽门禁。",
        })
    return out


def render(agg: dict, props: list) -> str:
    L = []
    L.append("═" * 62)
    L.append("  摩擦报告（MCP v3）" + (f" — 最近 {agg['window_days']} 天" if agg['window_days'] else ""))
    L.append("═" * 62)
    L.append(f"记录 {agg['records']} 条  |  真实操作 {agg['real_ops']}  "
             f"|  仪式调用 {agg['ceremony_calls']}  |  仪式比 {agg['ceremony_ratio']}")
    L.append(f"结果分布: {agg['by_outcome']}")
    L.append("")
    L.append("── 拒绝 Top ──")
    if agg["by_check_deny"]:
        for cid, n in list(agg["by_check_deny"].items())[:12]:
            L.append(f"  {n:5d}  {cid or '(no check)'}")
    else:
        L.append("  （无）")
    L.append("")
    L.append("── 警告 / 已放宽 Top ──")
    if agg["by_check_warn"]:
        for cid, n in list(agg["by_check_warn"].items())[:12]:
            L.append(f"  {n:5d}  {cid or '(no check)'}")
    else:
        L.append("  （无）")
    L.append("")
    L.append("── 策略中从未命中的检查 ──")
    L.append("  " + (", ".join(agg["never_fired"]) if agg["never_fired"] else "（无）"))
    L.append("")
    L.append("── 提案 ──")
    if props:
        for i, p in enumerate(props, 1):
            L.append(f"  [{i}] ({p['kind']}) {p['detail']}")
    else:
        L.append("  （无）")
    L.append("")
    L.append("改 policy.json 前须经 meta-developer 复核 —— 见 "
             ".agents/knowledge/meta/enforcement-policy.md")
    return "\n".join(L)


def main(argv: list | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    as_json = "--json" in argv
    since = None
    if "--since" in argv:
        i = argv.index("--since")
        if i + 1 < len(argv):
            try:
                since = int(argv[i + 1])
            except ValueError:
                print("ERROR: --since 需要整数天数")
                return 2
    if "-h" in argv or "--help" in argv:
        print(__doc__)
        return 0

    agg = aggregate(read_all(), since_days=since)
    props = proposals(agg)
    if as_json:
        print(json.dumps({"facts": agg, "proposals": props}, ensure_ascii=False, indent=2))
    else:
        print(render(agg, props))
    actionable = [p for p in props if p["severity"] == "review"]
    return 1 if actionable else 0


if __name__ == "__main__":
    sys.exit(main())
