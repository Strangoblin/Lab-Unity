"""出口验收 — 交付是否合格，用**证据**回答，不用自报告。

v3 的三个操作（write / delete / move）全部管在**入口**：写之前验内容、删之前验可逆性。
入口管到极致也有一个盲区 —— **做完之后没有任何人检查**。agent 说「改完了」，
框架无从判断：编译过了吗？规范贴合吗？画面/耗时验证了吗？结论记下来了吗？

v4 把重量放在出口。每项义务都能落到一条可核查的事实上，而不是一句承诺：

| 义务 | 证据来源 | 为什么可信 |
|------|----------|-----------|
| knowledge | PostToolUse 观测到的 Read 记录 | 工具看到的就是实际发生的，没有申报字段可编造 |
| norm      | writes.jsonl 里记录的违规 | 写入时已判定，这里只汇总 |
| compile   | unityctl logs 的 error 行 | 读的是编辑器自己的日志 |
| verify    | validation/ 或 tmp/sessions 下晚于最后写入的产物 | 时间先后可查，早于改动的报告不算数 |
| memory    | 有实质改动时出现 dated 记录 + 索引更新 | 路径与形状可机械判定 |

每项义务的阻断强度取自 `policy.json`（`close.<id>`），缺省为各 profile 声明；
可 `waive` 单项并留痕 —— 但豁免会同时落 friction 账本，属于「被记录的放宽」。
"""

from __future__ import annotations

import json
import os
import re
import subprocess

import profiles
import session
import zones
from validation import policy
from validation.knowledge_paths import (KnowledgePathError, default_kb_roots,
                                        find_project_root, resolve_entry)
from validation.unity_assets import classify

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
KB_ROOTS = default_kb_roots(find_project_root(__file__))

# 编译证据：Unity 控制台里的错误行。判行不判整体 —— unityctl logs 的 --json 仍返回纯文本。
_ERROR_LINE = re.compile(
    r"(\berror CS\d{4}\b|\bShader error\b|\bCompilation failed\b|\bCompilerOutput\b"
    r"|^\[?[^]]*\]?\s*error:|^\s*error\b)", re.IGNORECASE)

_MEMORY_RECORD = re.compile(r"^(meta|unity)/\d{4}-\d{2}-\d{2}[^/]*\.md$")


def _now_ms() -> int:
    import time
    return int(time.time() * 1000)


# ═══ 各项义务的判定 ═══

def check_knowledge(profile_name: str, since_ms: int | None) -> dict:
    """必读知识是否被真实读取。按稳定 ID 或 basename 匹配观测到的 Read 记录。"""
    spec = profiles.PROFILES.get(profile_name) or {}
    required = spec.get("required", [])
    if not required:
        return {"id": "knowledge", "status": "OK", "detail": "该 profile 无必读项"}

    read_paths = {zones.normalize(r["path"]) for r in session.reads_since(since_ms)}
    missing = []
    for entry in required:
        try:
            resolved = resolve_entry(entry, PROJECT_ROOT, KB_ROOTS)
            target = resolved.resolved_path
            rel = os.path.relpath(target, PROJECT_ROOT).replace(os.sep, "/")
        except KnowledgePathError:
            missing.append({"id": entry, "why": "知识 ID 解析不到真实文件"})
            continue
        if rel not in read_paths and os.path.basename(rel) not in {
                os.path.basename(p) for p in read_paths}:
            missing.append({"id": entry, "why": "未被读取"})

    if missing:
        return {"id": "knowledge", "status": "MISS", "missing": missing,
                "detail": f"必读未读: {[m['id'] for m in missing]}"}
    return {"id": "knowledge", "status": "OK", "detail": f"{len(required)} 份必读均已读取"}


def check_norm(since_ms: int | None) -> dict:
    """本次写入是否出现过规范违规。违规在写入时已判定并记账，这里只汇总。"""
    items = [w for w in session.writes_since(since_ms) if w.get("violations")]
    if items:
        return {"id": "norm", "status": "MISS",
                "detail": f"{len(items)} 次写入带违规",
                "violations": [{"path": w["path"], "violations": w["violations"]}
                               for w in items[:10]]}
    return {"id": "norm", "status": "OK", "detail": "无规范违规记录"}


def check_compile(since_ms: int | None) -> dict:
    """最后一次写入之后，Unity 编译是否干净。

    拿不到运行时（编辑器没开、CLI 缺席）时返回 UNAVAILABLE，不返回 OK ——
    「无法验证」与「验证通过」是两件事，混同会让出口验收失去意义。
    """
    writes = session.writes_since(since_ms)
    created = [w for w in writes if w.get("zone") == "asset"]
    if not created:
        return {"id": "compile", "status": "OK", "detail": "本次未改资产，无需编译证据"}

    last_write = max(w["ts"] for w in created)
    try:
        proc = subprocess.run(("unityctl", "logs", "-l", "error", "-n", "200"),
                              cwd=PROJECT_ROOT, capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.SubprocessError) as e:
        return {"id": "compile", "status": "UNAVAILABLE",
                "detail": f"无法读取 Unity 日志: {type(e).__name__}"}

    if proc.returncode != 0:
        return {"id": "compile", "status": "UNAVAILABLE",
                "detail": f"unityctl 退出码 {proc.returncode}（编辑器未连接？）"}

    lines = [ln.strip() for ln in proc.stdout.splitlines() if ln.strip()]
    errors = [ln for ln in lines if _ERROR_LINE.search(ln)]
    if errors:
        return {"id": "compile", "status": "MISS",
                "detail": f"写入后仍有 {len(errors)} 条错误",
                "errors": errors[:10], "last_write_ms": last_write}
    return {"id": "compile", "status": "OK", "detail": "无错误行"}


# 验证产物：验证根下的文件才算「验证证据」。
_VERIFY_EXTS = {".png", ".json", ".md", ".log", ".csv", ".txt"}


def _can_contain_verification(rel_root: str) -> bool:
    """目录本身是、位于、或可能包含验证根（`Validation~` / `tmp/sessions`）。

    项目根要继续下探；`Assets` 任意深度都可能出现 `Validation~`；`tmp` 只走 `sessions`。
    早先版本以「路径里是否出现 sessions/Validation~」判断，从项目根（`rel_root="."`）
    直接剪掉全部子目录，于是 `verify` 永远 MISS —— 判据必须看「能否到达」，不是「当前是否已在」。
    """
    if rel_root == ".":
        return True
    if "Validation~" in rel_root.split("/"):
        return True
    if rel_root == "Assets" or rel_root.startswith("Assets/"):
        return True
    return rel_root in ("tmp", "tmp/sessions") or rel_root.startswith("tmp/sessions/")


def _inside_verification(rel_root: str) -> bool:
    """目录本身是否位于验证根内 —— 只有这里的文件才是证据。"""
    return "Validation~" in rel_root.split("/") or rel_root in ("tmp/sessions",) \
        or rel_root.startswith("tmp/sessions/")


def find_verification(since_ms: int) -> list[dict]:
    """找出晚于最后一次写入的验证产物。按修改时间判早晚，与内容无关。"""
    found = []
    for root, dirs, files in os.walk(PROJECT_ROOT):
        dirs[:] = [d for d in dirs if d not in
                   {".git", ".venv", "__pycache__", "Library", "Temp", "obj", "Logs",
                    "node_modules", ".pytest_cache"}]
        rel_root = os.path.relpath(root, PROJECT_ROOT).replace(os.sep, "/")
        if not _can_contain_verification(rel_root):
            dirs[:] = []
            continue
        if not _inside_verification(rel_root):
            continue
        for name in files:
            if os.path.splitext(name)[1].lower() not in _VERIFY_EXTS:
                continue
            full = os.path.join(root, name)
            try:
                mtime = int(os.path.getmtime(full) * 1000)
            except OSError:
                continue
            if mtime >= since_ms:
                found.append({"path": os.path.relpath(full, PROJECT_ROOT).replace(os.sep, "/"),
                              "mtime_ms": mtime})
    return found


def check_verify(since_ms: int | None) -> dict:
    writes = [w for w in session.writes_since(since_ms) if w.get("zone") == "asset"]
    if not writes:
        return {"id": "verify", "status": "OK", "detail": "本次未改资产，无需验证产物"}
    last_write = max(w["ts"] for w in writes)
    found = find_verification(last_write)
    if not found:
        return {"id": "verify", "status": "MISS",
                "detail": "没有晚于最后一次写入的验证产物（截图 / 基准报告 / 复跑日志）",
                "last_write_ms": last_write}
    return {"id": "verify", "status": "OK",
            "detail": f"{len(found)} 个验证产物晚于最后一次写入",
            "evidence": [f["path"] for f in found[:10]]}


def check_memory(since_ms: int | None) -> dict:
    """有实质改动时，是否留下了结论记录并更新索引。

    「实质改动」= asset / system 区的写入；「记录」= memory 区的写入。
    两者必须分开取 —— 只按前者过滤会把 memory 区的记录一并排除，
    于是这项检查永远 MISS，等于没有。
    """
    all_writes = session.writes_since(since_ms)
    substantive = [w for w in all_writes if w.get("zone") in ("asset", "system")]
    if not substantive:
        return {"id": "memory", "status": "OK", "detail": "本次无实质改动，无需记录"}

    records = [w for w in all_writes
               if zones.normalize(w["path"]).startswith(".agents/memory/")
               and re.match(_MEMORY_RECORD,
                            zones.normalize(w["path"])[len(".agents/memory/"):])]
    index_updated = any(
        zones.normalize(w["path"]) in (".agents/memory/unity/MEMORY.md",
                                       ".agents/memory/meta/memory.md")
        for w in all_writes)
    if not records:
        return {"id": "memory", "status": "MISS",
                "detail": "本次有实质改动，但没有新的 dated 记录"}
    if not index_updated:
        return {"id": "memory", "status": "PARTIAL",
                "detail": "有 dated 记录，但索引未更新",
                "records": [w["path"] for w in records]}
    return {"id": "memory", "status": "OK", "detail": "记录与索引均已更新"}


# ═══ 汇总 ═══

_CHECKS = {
    "knowledge": lambda profile, since: check_knowledge(profile, since),
    "norm": lambda profile, since: check_norm(since),
    "compile": lambda profile, since: check_compile(since),
    "verify": lambda profile, since: check_verify(since),
    "memory": lambda profile, since: check_memory(since),
}


def run(profile_name: str | None, extra_checks: list[str] | None = None) -> dict:
    """按当前会话执行出口校验。返回逐项结果与总体结论。"""
    data = session.load()
    profile_name = profile_name or data.get("profile")
    spec = profiles.PROFILES.get(profile_name or "") or {}
    obligations = list(spec.get("obligations", [])) + list(extra_checks or [])
    since = data.get("opened_at")

    results, blocking = [], []
    for name in dict.fromkeys(obligations):
        check = _CHECKS.get(name)
        if not check:
            continue
        result = check(profile_name, since)
        level = policy.level_of(f"close.{name}", "block")
        waiver = session.waived(name)
        result["level"] = level
        if waiver:
            result["waived"] = waiver.get("reason", "")
            result["status"] = "WAIVED"
        elif result["status"] in ("MISS", "PARTIAL") and level == "block":
            blocking.append(name)
        results.append(result)

    return {
        "status": "OK" if not blocking else "BLOCKED",
        "task": data.get("task"),
        "profile": profile_name,
        "workspace": data.get("workspace", []),
        "checks": results,
        "blocking": blocking,
        "hint": ("出口证据齐备。" if not blocking else
                 "以下义务未满足: " + ", ".join(blocking)
                 + "。补证据后重跑；确属不适用用 `close --waive <id> --reason ...`，会留痕。"),
    }
