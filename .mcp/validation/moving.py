"""移动后果验证 —— write_gated 与 delete_gated 的复合面。

移动 = 写新 + 删旧。两个原子操作合成一个意图，而**单独看每一个，判定都是对的**：

    删除旧位置：看它自己，tracked-dirty 意味着「删了只剩历史版本」→ 硬拦，正确
    写入新位置：看它自己，内容规范检查通过 → 放行，正确

合起来却成了误拦 —— 因为内容完整写到了新位置，没有丢失什么。

所以本模块验的不是单个文件的状态，而是这次操作的**净效果**：内容是否只是换了位置。
两个附加后果要一并交代，否则「移动」会悄悄改变 Unity 的资产身份：

  .meta 的 GUID   Unity 资产的身份是 GUID 不是路径。正文搬走而 .meta 不搬，
                  引用会断。故 .meta 成对搬运，且搬运前查 GUID 是否已被别人占用
                  （同 GUID 出现两次本身就是错误，与位置无关）。

  覆盖            dst 已存在时默认拒绝。移动不是「就地替换」，
                  要替换就显式走 write_gated —— 让意图留在调用形态里。

与 deletion.py 的分工:
    deletion.py — 单向：这个文件消失后，损失是否可逆
    moving.py   — 双向：内容是否只是换了位置
"""

from __future__ import annotations

import json
import os
import re
import subprocess
from datetime import datetime, timezone

from validation.deletion import read_guid

META_SUFFIX = ".meta"
GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)

# 与 deletion.py 同源的上限 —— 判定不了就不放行
TIMEOUT_GREP = 120

AUDIT_FILE = os.path.join(".mcp", "moves.jsonl")


def _exists(project_root: str, rel: str) -> bool:
    return os.path.lexists(os.path.join(project_root, rel))


def find_guid_owners(project_root: str, guid: str, exclude: list[str],
                     assets_dir: str = "Assets") -> list[str]:
    """返回 `Assets/` 下**声明**该 GUID 的其他 .meta（项目相对，已剔除 exclude）。

    与 deletion.guid_referenced_elsewhere 的差别：那个问「谁引用它」，
    这个问「谁也声称拥有它」。后者是移动才需要问的 —— 同 GUID 两份 = Unity 报错。
    """
    if not guid or not os.path.isdir(os.path.join(project_root, assets_dir)):
        return []
    try:
        r = subprocess.run(("grep", "-rlF", guid, assets_dir), cwd=project_root,
                           capture_output=True, text=True, timeout=TIMEOUT_GREP)
    except (subprocess.SubprocessError, OSError):
        return [f"<grep 失败: 无法排除 {guid} 是否已被占用>"]
    excluded = set(exclude)
    owners = []
    for line in r.stdout.splitlines():
        if not line or line in excluded or not line.endswith(META_SUFFIX):
            continue
        if read_guid(os.path.join(project_root, line)) == guid:
            owners.append(line)
    return owners


def plan(project_root: str, moves: list[tuple[str, str]]) -> dict:
    """预检全部移动对。moves = [(src_rel, dst_rel), ...]

    返回 {"blocked": [...], "pairs": [...]}；blocked 为空即整批可执行。
    预检不做任何改动 —— 拒绝的代价必须是零。
    """
    blocked: list[dict] = []
    pairs: list[dict] = []

    sources = [s for s, _ in moves]
    targets = [t for _, t in moves]

    for src, dst in moves:
        entry = {"from": src, "to": dst}

        # ── 批次级检查在前 ──
        # 顺序有讲究：链式歧义是「这一批的形状」问题，不是某个文件的问题。
        # 若排在存在性之后，「B 是 A 的目标又是 C 的源」会先报成 B 不存在 ——
        # 而 B 不存在恰恰是链条本身造成的，那个诊断会把人引向错误的方向。
        if src == dst:
            blocked.append({**entry, "error": "SAME_PATH",
                            "detail": "源与目标相同，无需移动。"})
            continue
        if sources.count(src) > 1:
            blocked.append({**entry, "error": "DUPLICATE_SOURCE",
                            "detail": "同一批里源路径出现多次。"})
            continue
        if targets.count(dst) > 1:
            blocked.append({**entry, "error": "DUPLICATE_TARGET",
                            "detail": "同一批里多个源指向同一目标。"})
            continue
        if dst in sources or src in targets:
            blocked.append({**entry, "error": "CHAINED_MOVE",
                            "detail": f"{src} 与 {dst} 之间有中间态：本批里它既是目标又是源。"
                                      f"拆成两批，顺序才无歧义。"})
            continue

        # ── 逐文件检查 ──
        if not _exists(project_root, src):
            blocked.append({**entry, "error": "MISSING_SOURCE",
                            "detail": "源不存在。"})
            continue
        if _exists(project_root, dst):
            blocked.append({**entry, "error": "TARGET_EXISTS",
                            "detail": f"{dst} 已存在。移动不覆盖目标；要替换请走 write_gated。"})
            continue

        # .meta 成对：正文与 .meta 一起走，GUID 才不会断
        meta_from, meta_to = src + META_SUFFIX, dst + META_SUFFIX
        entry["meta_from"], entry["meta_to"] = meta_from, meta_to

        if _exists(project_root, meta_from):
            if _exists(project_root, meta_to):
                blocked.append({**entry, "error": "META_TARGET_EXISTS",
                                "detail": f"{meta_to} 已存在。"})
                continue
            guid = read_guid(os.path.join(project_root, meta_from))
            if guid:
                owners = find_guid_owners(project_root, guid, exclude=[meta_from])
                if owners:
                    blocked.append({**entry, "error": "GUID_TAKEN", "guid": guid,
                                    "detail": f"GUID {guid} 已被 {owners[0]} 声明 —— "
                                              f"同 GUID 两份会让 Unity 报错。",
                                    "owners": owners[:5]})
                    continue
            entry["guid"] = guid

        pairs.append(entry)

    return {"blocked": blocked, "pairs": pairs}


def execute(project_root: str, plan_pairs: list[dict]) -> dict:
    """执行已通过预检的移动对。**MCP 与 CLI 两条通道共用本函数**（判定与执行都必须同源）。

    顺序 write-all → delete-all，拒绝与失败各有方向：

        预检不过     → 调用方负责，本函数只收已通过的对（拒绝的代价必须是零）
        写阶段失败   → 回滚已写的新文件，源一个没删（等于没发生）
        删阶段失败   → 保留旧副本，内容没丢（安全方向），failed 里列出

    返回 {"status": "OK"|"ROLLED_BACK", "moved": [...], "removed": [...], "failed": [...]}
    """
    from validation.atomic_io import atomic_write

    written: list[str] = []          # 已写出的绝对路径，回滚用
    payload: list[dict] = []
    try:
        for entry in plan_pairs:
            src, dst = entry["from"], entry["to"]
            src_full = os.path.join(project_root, src)
            dst_full = os.path.join(project_root, dst)
            with open(src_full, encoding="utf-8") as f:
                body = f.read()
            item = {"from": src, "to": dst, "body": body,
                    "meta_from": entry.get("meta_from"), "guid": entry.get("guid", "")}
            atomic_write(dst_full, body)
            written.append(dst_full)

            if item["meta_from"] and os.path.lexists(os.path.join(project_root, item["meta_from"])):
                with open(os.path.join(project_root, item["meta_from"]), encoding="utf-8") as f:
                    item["meta_body"] = f.read()
                meta_to_full = os.path.join(project_root, entry["meta_to"])
                atomic_write(meta_to_full, item["meta_body"])
                written.append(meta_to_full)
            payload.append(item)
    except Exception as e:
        for full in reversed(written):        # 回滚：删掉刚写的，源保持不动
            try:
                os.remove(full)
            except OSError:
                pass
        return {"status": "ROLLED_BACK", "error": str(e), "rolled_back": len(written),
                "moved": [], "removed": [], "failed": [],
                "hint": "源文件全部未动，等于没发生。"}

    removed, failed = [], []
    for entry in payload:
        for rel in (entry["from"], entry.get("meta_from")):
            if not rel:
                continue
            full = os.path.join(project_root, rel)
            if not os.path.lexists(full):
                continue
            try:
                os.remove(full)
                removed.append(rel)
            except OSError as e:
                failed.append({"path": rel, "error": str(e)})

    return {"status": "OK",
            "moved": [{"from": e["from"], "to": e["to"], "guid": e["guid"]} for e in payload],
            "removed": removed, "failed": failed}


def append_audit(project_root: str, record: dict) -> None:
    """追加一条移动记录（JSONL）。失败静默 —— 与 deletion 同策略，审计不阻塞操作。

    移动必须落盘的理由：新位置有产物、旧位置没产物，**丢失的正是「哪个变成了哪个」**
    —— 这一条在文件系统里查不回来。
    """
    try:
        path = os.path.join(project_root, AUDIT_FILE)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps({"ts": datetime.now(timezone.utc).isoformat(), **record},
                               ensure_ascii=False) + "\n")
    except OSError:
        pass
