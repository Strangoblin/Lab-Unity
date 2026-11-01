#!/usr/bin/env python3
"""体系自检盘点：记忆寿命、索引覆盖、活文档入链、证据目录与垃圾桶存量。

只读。不删除、不改写任何文件 —— 判断留给 agent，脚本只把同一份事实算成
可复现的表格。手写 shell 每次数出来的「大概」不能作为删东西的依据。

退出码：0 正常；1 `--check` 阈值被触发；2 用法错误。
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from datetime import date

# 本文件在 .agents/skills/structure-selfcheck/scripts/，仓库根要退四级。
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "..", "..", "..", ".."))
AGENTS = os.path.join(ROOT, ".agents")
MEMORY = os.path.join(AGENTS, "memory")
LIVE_DIRS = ("knowledge", "skills", "agents")
DOMAINS = ("meta", "unity")
INDEX_FILES = {"meta": os.path.join(MEMORY, "meta", "memory.md"),
               "unity": os.path.join(MEMORY, "unity", "MEMORY.md")}
TRASH = os.path.join(ROOT, ".mcp", "trash")
RECORD_RE = re.compile(r"^(\d{4}-\d{2}-\d{2})-(.+)\.md$")
LINK_RE = re.compile(r"\]\(([^)\s]+\.md)\)")

DEFAULT_MAX_AGE = 60
DEFAULT_STALE_LIMIT = 20
DEFAULT_BUCKET_LIMIT = 15


def _read(path: str) -> str:
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            return fh.read()
    except OSError:
        return ""


def _dir_summary(path: str):
    if not os.path.isdir(path):
        return None
    files = size = 0
    for dirpath, _dirnames, filenames in os.walk(path):
        for name in filenames:
            try:
                size += os.path.getsize(os.path.join(dirpath, name))
            except OSError:
                continue
            files += 1
    return {"files": files, "bytes": size}


def live_corpus() -> str:
    """活文档正文。memory 自身不算活引用 —— 记录之间互相引用，不代表结论还在用。"""
    chunks = []
    for sub in LIVE_DIRS:
        for dirpath, dirnames, filenames in os.walk(os.path.join(AGENTS, sub)):
            dirnames[:] = [d for d in dirnames if d not in ("__pycache__", ".git")]
            for name in filenames:
                if not name.endswith((".md", ".py", ".json", ".sh")):
                    continue
                full = os.path.join(dirpath, name)
                try:
                    if os.path.getsize(full) > 2 * 1024 * 1024:
                        continue
                except OSError:
                    continue
                chunks.append(_read(full))
    return "\n".join(chunks)


def memory_records(corpus: str) -> list:
    out = []
    today = date.today()
    for domain in DOMAINS:
        base = os.path.join(MEMORY, domain)
        index_text = _read(INDEX_FILES[domain])
        try:
            names = sorted(os.listdir(base))
        except OSError:
            continue
        for name in names:
            match = RECORD_RE.match(name)
            if not match:
                continue
            try:
                recorded = date.fromisoformat(match.group(1))
            except ValueError:
                continue
            stem = name[:-3]
            full = os.path.join(base, name)
            out.append({
                "domain": domain,
                "name": name,
                "stem": stem,
                "date": recorded.isoformat(),
                "age_days": (today - recorded).days,
                "lines": _read(full).count("\n") + 1,
                "bytes": os.path.getsize(full),
                "live_links": corpus.count(name),
                # 1 = 只有索引表里那一行；>1 = 索引正文另有引用；0 = 未入索引
                "index_mentions": index_text.count(name),
                "evidence": _dir_summary(os.path.join(base, "evidence", stem)),
            })
    return out


def _walk_entries(entry):
    """遍历 manifest 条目。对损坏的 manifest 保持沉默 —— 盘点脚本不该因为
    一份历史桶的 `children` 被写成键名列表而罢工，只把它标记出来。"""
    if not isinstance(entry, dict):
        return
    if entry.get("kind") == "file":
        yield entry
    children = entry.get("children")
    if isinstance(children, list):
        for child in children:
            if isinstance(child, dict):
                yield from _walk_entries(child)


def _manifest_intact(entry) -> bool:
    if not isinstance(entry, dict):
        return False
    children = entry.get("children")
    if isinstance(children, list):
        return all(isinstance(child, dict) and _manifest_intact(child) for child in children)
    return children is None


def _classify(top: list) -> str:
    if not top:
        return "其它"
    if any(path.startswith("Assets/") for path in top):
        return "业务资产"
    if all(path.startswith("tmp/") for path in top):
        return "生成物"
    if any(path.startswith(".mcp/") for path in top):
        return "退役子系统"
    return "其它"


def trash_buckets() -> list:
    out = []
    if not os.path.isdir(TRASH):
        return out
    today = date.today()
    for name in sorted(os.listdir(TRASH)):
        full = os.path.join(TRASH, name)
        if not os.path.isdir(full):
            continue
        bucket = {"id": name, "class": "无 manifest", "top": [], "files": 0,
                  "bytes": 0, "age_days": None, "reason": "", "manifest_ok": True}
        try:
            manifest = json.loads(_read(os.path.join(full, "manifest.json")) or "{}")
        except ValueError:
            manifest = {}
        entries = manifest.get("entries") or []
        if not entries:
            out.append(bucket)
            continue
        bucket["top"] = [entry.get("path", "") for entry in entries]
        bucket["reason"] = manifest.get("reason", "")
        bucket["manifest_ok"] = all(_manifest_intact(entry) for entry in entries)
        files = [item for entry in entries for item in _walk_entries(entry)]
        bucket["files"] = len(files)
        bucket["bytes"] = sum(item.get("size", 0) for item in files)
        stamp = manifest.get("ts")
        if isinstance(stamp, (int, float)):
            bucket["age_days"] = (today - date.fromtimestamp(stamp / 1000.0)).days
        bucket["class"] = _classify(bucket["top"])
        out.append(bucket)
    return out


def index_orphans(records: list) -> dict:
    """索引与磁盘对不上：文件没进索引、索引指向已不存在的文件。"""
    linked = {}
    for domain, path in INDEX_FILES.items():
        base = os.path.dirname(path)
        for target in LINK_RE.findall(_read(path)):
            if target.startswith(("http://", "https://")):
                continue
            resolved = os.path.normpath(os.path.join(base, target))
            linked.setdefault(domain, set()).add(resolved)
    present = {(record["domain"], os.path.join(MEMORY, record["domain"], record["name"]))
               for record in records}
    unindexed = sorted(record["domain"] + "/" + record["name"]
                       for record in records if record["index_mentions"] == 0)
    missing = []
    for domain, paths in linked.items():
        for resolved in paths:
            if not os.path.exists(resolved):
                missing.append(domain + "/" + os.path.relpath(resolved, os.path.join(MEMORY, domain)))
    return {"unindexed": unindexed, "missing_files": sorted(missing),
            "record_count": len(present)}


def build(max_age: int) -> dict:
    corpus = live_corpus()
    records = memory_records(corpus)
    buckets = trash_buckets()
    stale = [record for record in records
             if record["age_days"] >= max_age and record["live_links"] == 0]
    by_domain = {}
    for domain in DOMAINS:
        rows = [record for record in records if record["domain"] == domain]
        by_domain[domain] = {
            "records": len(rows),
            "old": sum(1 for r in rows if r["age_days"] >= max_age),
            "unindexed": sum(1 for r in rows if r["index_mentions"] == 0),
            "table_only": sum(1 for r in rows if r["index_mentions"] == 1),
            "unlinked": sum(1 for r in rows if r["live_links"] == 0),
            "with_evidence": sum(1 for r in rows if r["evidence"]),
        }
    by_class = {}
    for bucket in buckets:
        by_class[bucket["class"]] = by_class.get(bucket["class"], 0) + 1
    return {
        "generated": date.today().isoformat(),
        "max_age_days": max_age,
        "memory": {"domains": by_domain, "records": records, "stale": stale},
        "trash": {"buckets": buckets, "by_class": by_class},
        "orphans": index_orphans(records),
    }


def _size(num: int) -> str:
    for unit in ("B", "K", "M", "G"):
        if num < 1024 or unit == "G":
            return "%d%s" % (num, unit)
        num //= 1024
    return str(num)


def render(report: dict) -> str:
    lines = ["# 体系自检盘点  %s" % report["generated"], "", "## 记忆"]
    lines.append("%-7s %6s %6s %8s %6s %6s %8s"
                 % ("域", "记录", "≥%dd" % report["max_age_days"],
                    "未入索引", "仅表行", "零入链", "证据目录"))
    for domain, row in report["memory"]["domains"].items():
        lines.append("%-7s %6d %6d %8d %6d %6d %8d"
                     % (domain, row["records"], row["old"], row["unindexed"],
                        row["table_only"], row["unlinked"], row["with_evidence"]))
    stale = report["memory"]["stale"]
    lines.append("")
    lines.append("退役候选（≥%d 天且零入链）：%d" % (report["max_age_days"], len(stale)))
    for record in stale:
        extra = " 证据%2d项" % record["evidence"]["files"] if record["evidence"] else ""
        lines.append("  %-5s %-46s %4dd %4d行 索引%d%s"
                     % (record["domain"], record["name"], record["age_days"],
                        record["lines"], record["index_mentions"], extra))
    lines += ["", "## 垃圾桶"]
    lines.append("%-26s %-11s %5s %6s %7s  %s"
                 % ("id", "类别", "年龄", "文件", "体积", "顶层路径"))
    for bucket in report["trash"]["buckets"]:
        age = "-" if bucket["age_days"] is None else "%dd" % bucket["age_days"]
        top = ", ".join(bucket["top"][:2])
        if len(bucket["top"]) > 2:
            top += " …"
        if not bucket["manifest_ok"]:
            top += "  [manifest 损坏]"
        lines.append("%-26s %-11s %5s %6d %7s  %s"
                     % (bucket["id"], bucket["class"], age, bucket["files"],
                        _size(bucket["bytes"]), top))
    summary = "、".join("%s %d 桶" % (key, value)
                       for key, value in sorted(report["trash"]["by_class"].items()))
    lines.append("按类汇总：" + (summary or "（无）"))
    orphans = report["orphans"]
    lines += ["", "## 孤儿"]
    lines.append("未入索引：%d" % len(orphans["unindexed"]))
    for item in orphans["unindexed"]:
        lines.append("  " + item)
    lines.append("索引指向缺失文件：%d" % len(orphans["missing_files"]))
    for item in orphans["missing_files"]:
        lines.append("  " + item)
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="体系自检盘点（只读）")
    parser.add_argument("--json", action="store_true", help="机器可读输出")
    parser.add_argument("--check", action="store_true", help="超过阈值时退 1")
    parser.add_argument("--max-age", type=int, default=DEFAULT_MAX_AGE,
                        help="记忆年龄阈值（天），默认 %d" % DEFAULT_MAX_AGE)
    parser.add_argument("--stale-limit", type=int, default=DEFAULT_STALE_LIMIT,
                        help="触发提醒的退役候选数，默认 %d" % DEFAULT_STALE_LIMIT)
    parser.add_argument("--bucket-limit", type=int, default=DEFAULT_BUCKET_LIMIT,
                        help="触发提醒的垃圾桶数，默认 %d" % DEFAULT_BUCKET_LIMIT)
    args = parser.parse_args(argv)
    if args.max_age <= 0:
        parser.error("--max-age 必须为正数")

    report = build(args.max_age)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(render(report))

    if args.check:
        reasons = []
        if len(report["memory"]["stale"]) >= args.stale_limit:
            reasons.append("退役候选 %d ≥ %d" % (len(report["memory"]["stale"]), args.stale_limit))
        buckets = len(report["trash"]["buckets"])
        if buckets >= args.bucket_limit:
            reasons.append("垃圾桶 %d 桶 ≥ %d" % (buckets, args.bucket_limit))
        if reasons:
            print("\n[阈值触发] " + "；".join(reasons), file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
