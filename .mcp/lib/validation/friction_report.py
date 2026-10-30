#!/usr/bin/env python3
"""摩擦报告 — 把信号账本聚合成「框架该往哪改」的提案.

输入: `.mcp/state/friction.jsonl`（telemetry 落账）
输出: 事实（各信号计数、拒绝分布）+ 提案（调级 / 退役 / 待观察）

它不是自动调参器 —— 提案必须由 meta-developer 复核、人工确认后才改 ``policy.json``。
它解决的是**触发问题**: 以前框架升级等人「想起来」，现在有账本就有触发点与排序依据。

v4 的重心变了：v3 看「仪式开销比」（申报付了多少税），v4 看两件更有用的事 ——
  · 哪条检查在**真实写入**上反复拒绝（可能是误拦，也可能是质量真的差）
  · 哪项**出口义务**总是缺失（说明义务定得不对，或者环境拿不到证据）

用法:
  python3 .mcp/cli.py report                  # 人读格式
  python3 .mcp/cli.py report --json           # 机器格式
  python3 .mcp/cli.py report --since 7        # 只看最近 7 天
退出码（lint 风格）: 0 = 无 review 级提案（健康或样本不足）/ 1 = 有提案待办
"""

from __future__ import annotations

import json
import os
import sys
import time
from collections import Counter

_MCP = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
if _MCP not in sys.path:
    sys.path.insert(0, _MCP)

# 一律走 `validation.*` 包名 —— 若这里用顶层 `policy`，会与 judgement 的
# `validation.policy` 形成**两个模块实例**（各自一份缓存），reload 只清一个。
from telemetry import read_all                          # noqa: E402
from validation.policy import snapshot as policy_snapshot   # noqa: E402

# 判定「样本够了」的最小真实操作数 —— 样本不足不出提案，避免用 3 次写入下结论
MIN_OPS = 20
# 某条检查的拒绝占比超过它，且次数够多，才值得抽样看是否误拦
DENY_SHARE_WARN = 0.20
DENY_COUNT_MIN = 5
# 某出口义务的缺失占比超过它，说明义务定得不对（或环境拿不到证据）
CLOSE_MISS_SHARE_WARN = 0.50
CLOSE_MISS_COUNT_MIN = 3


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

    # 真实写入 = Write/Edit 的判定（ok + denied）；读与会话不产生写入摩擦
    real_ops = sum(1 for e in rows
                   if e.get("op") in ("write", "delete", "move")
                   and e.get("outcome") in ("ok", "denied"))

    # 出口义务：每次 close 的 blocking 列表被展平成逐项信号
    close_rows = [e for e in rows if e.get("op") == "close"]
    close_miss = Counter()
    for e in close_rows:
        for name in str(e.get("detail", "")).split(","):
            name = name.strip()
            if name:
                close_miss[name] += 1

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
        "real_ops": real_ops,
        "closes": len(close_rows),
        "close_miss": dict(close_miss.most_common()),
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
            "detail": f"真实操作 {agg['real_ops']} < {MIN_OPS}，暂不出调级提案"
                      f"（避免小样本下结论）",
        })
        return out

    # 1) 从未命中的检查 —— 安全类零命中是好事，其余才是候选。
    #    出口检查（close.*）只有在收尾时才会被评估，没跑过收尾就谈不上「从未命中」，
    #    把它们当退役候选是错的：那会推荐删掉从没被用过的功能。
    never_fired = [cid for cid in agg["never_fired"] if not cid.startswith("close.")]
    for cid in never_fired:
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

    # 2) 高频拒绝 —— 未必是错，但值得看是不是误拦（尤其风格类，非正确性类）
    for cid, n in agg["by_check_deny"].items():
        if not cid:
            continue
        share = n / max(agg["real_ops"], 1)
        if n >= DENY_COUNT_MIN and share >= DENY_SHARE_WARN and cid.startswith(
                ("norm.", "placement.", "sandbox.", "scope.")):
            out.append({
                "kind": "inspect-denies", "check": cid, "severity": "review",
                "detail": f"{cid} 拒绝 {n} 次（占真实操作 {share:.0%}）。"
                          f"抽样看是否误拦；若多为误拦 → 降级或改沙箱声明。",
            })

    # 3) 出口义务总是缺失 —— 义务定得不对，或者环境根本拿不到证据。
    #    这类不该靠「再努力一点」，该改义务或改证据来源。
    for name, n in agg["close_miss"].items():
        if n < CLOSE_MISS_COUNT_MIN or not agg["closes"]:
            continue
        if n / agg["closes"] < CLOSE_MISS_SHARE_WARN:
            continue
        out.append({
            "kind": "reconsider-obligation", "check": f"close.{name}", "severity": "review",
            "detail": f"close.{name} 在 {agg['closes']} 次收尾里缺失 {n} 次"
                      f"（{n / agg['closes']:.0%}）。"
                      f"要么义务不适用于这类任务（改 profile），"
                      f"要么证据来源拿不到（改判定，例如 compile 需要 unityctl）。",
        })

    # 4) 会话没声明就开始写 —— 属恢复体验问题，不是放宽沙箱的理由
    churn = sum(n for sig, n in agg["by_signal"].items()
                if sig in ("deny.sandbox.not_set", "deny.SANDBOX_NOT_SET"))
    if churn >= 5:
        out.append({
            "kind": "fix-plumbing", "check": "sandbox.asset", "severity": "review",
            "detail": f"未声明沙箱就开始写入 {churn} 次 —— 属状态恢复/流程问题，"
                      f"应让 open 更顺手（或提供 resume），而不是取消沙箱。",
        })
    return out


def render(agg: dict, props: list) -> str:
    L = []
    L.append("═" * 62)
    L.append("  摩擦报告（MCP v4）" + (f" — 最近 {agg['window_days']} 天" if agg['window_days'] else ""))
    L.append("═" * 62)
    L.append(f"记录 {agg['records']} 条  |  真实操作 {agg['real_ops']}  "
             f"|  收尾 {agg['closes']} 次")
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
    L.append("── 出口义务缺失 Top ──")
    if agg["close_miss"]:
        for name, n in list(agg["close_miss"].items())[:12]:
            L.append(f"  {n:5d}  close.{name}")
    else:
        L.append("  （无）")
    L.append("")
    L.append("── 策略中从未命中的检查 ──")
    # close.* 只在收尾时评估；没跑过收尾就谈不上「从未命中」，列出来会误导成退役候选
    pending = [cid for cid in agg["never_fired"] if cid.startswith("close.")]
    fired_never = [cid for cid in agg["never_fired"] if not cid.startswith("close.")]
    L.append("  " + (", ".join(fired_never) if fired_never else "（无）"))
    if pending:
        L.append("  （未评估，因为没有收尾记录: " + ", ".join(pending) + "）")
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
