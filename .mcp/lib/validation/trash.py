"""Trash — 让删除变成可逆操作，而不是一道需要审批的闸。

v3 把删除验成「不可恢复性」：tracked-dirty 硬拦（删了只剩历史版本）、GUID 被引用硬拦。
判定本身没错，错在**结论**：用「不许删」来防住不可逆，代价是把所有删除都变成需要
先提交、先解引用的仪式 —— 实测里这挡住了正常清理。

v4 反向做：不可逆这件事由工具自己消掉。删除 = 搬到 `.mcp/trash/<id>/` 并落一份
manifest（原路径、git 状态、GUID 引用、sha256、目录清单），随时 `restore` 回来。

于是：
  - tracked-dirty 不再需要拦 —— 内容在垃圾桶里，恢复是一条命令
  - guid-referenced 不再需要拦 —— 引用还在，恢复后立刻复原
  - 失败可以把内容取回来 —— 删除失败最坏是「没删掉」

**留底不是审计**：`deletes.jsonl` 记的是「谁在什么时候删了什么」，trash 记的是「内容在哪」。
两者互补，缺一不可 —— 审计回答意图，manifest 回答内容。
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import stat
import time

from validation.unity_assets import (classify, guid_referenced_elsewhere, orphan_meta_warnings,
                                     read_guid)

TRASH_DIR = os.path.join(".mcp", "trash")


class TrashError(ValueError):
    pass


def _trash_root(project_root: str) -> str:
    return os.path.join(project_root, TRASH_DIR)


def _new_id() -> str:
    return time.strftime("%Y%m%d-%H%M%S") + "-" + hashlib.sha256(
        str(time.time_ns()).encode()).hexdigest()[:6]


def _sha256(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _is_symlink(path: str) -> bool:
    return os.path.islink(path)


def _walk_entry(project_root: str, rel: str, full: str) -> dict:
    """记录一个待搬条目的形状。目录记子项清单，文件记大小与 hash。"""
    st = os.lstat(full)
    kind = "directory" if stat.S_ISDIR(st.st_mode) else "file"
    entry = {"path": rel, "kind": kind}
    if kind == "file":
        entry.update({"size": st.st_size, "sha256": _sha256(full)})
        guid = read_guid(full)
        if guid:
            entry["guid"] = guid
            refs = guid_referenced_elsewhere(project_root, rel, guid)
            if refs:
                entry["referenced_by"] = refs[:20]
    else:
        children = []
        for name in sorted(os.listdir(full)):
            children.append(_walk_entry(project_root, rel + "/" + name,
                                        os.path.join(full, name)))
        entry["children"] = children
    return entry


def plan(project_root: str, paths: list[str]) -> dict:
    """预检 → manifest。**不执行任何搬动**；拒绝的代价必须是零。

    拒绝的只有两类，都是「这次请求本身不成立」，与内容安全无关：
      - 软链：不跟随，也不搬（搬一个链接等于搬走别人的指向）
      - 分区根 / 仓库根：删掉作用域根等于删掉整个工作区
    """
    blocked, entries = [], []
    trash_full = os.path.abspath(_trash_root(project_root))
    protected = {os.path.abspath(project_root), trash_full}

    for rel in paths:
        full = os.path.abspath(os.path.join(project_root, rel))
        try:
            if os.path.commonpath([full, os.path.abspath(project_root)]) != os.path.abspath(project_root):
                blocked.append({"path": rel, "state": "path-not-allowed",
                                "detail": "路径在项目根之外"})
                continue
        except ValueError:
            blocked.append({"path": rel, "state": "path-not-allowed",
                            "detail": "路径在项目根之外"})
            continue
        if full in protected:
            blocked.append({"path": rel, "state": "protected-root",
                            "detail": "作用域根不可删除"})
            continue
        if not os.path.lexists(full):
            entries.append({"path": rel, "kind": "missing"})
            continue
        if _is_symlink(full):
            blocked.append({"path": rel, "state": "symlink",
                            "detail": "软链不跟随、不搬运 —— 改了指向之外的东西"})
            continue
        try:
            entries.append(_walk_entry(project_root, rel, full))
        except OSError as e:
            blocked.append({"path": rel, "state": "precheck-failed", "detail": str(e)})

    # 目录请求覆盖其子项请求，避免同一路径搬两次
    tops, seen = [], set()
    for e in entries:
        if any(e["path"] == t or e["path"].startswith(t + "/") for t in tops):
            continue
        tops.append(e["path"])
        seen.add(e["path"])

    status = classify(project_root, [e["path"] for e in entries if e["kind"] != "missing"])
    return {"blocked": blocked, "entries": entries, "selected": tops, "status": status}


def execute(project_root: str, planned: dict, reason: str, channel: str = "cli") -> dict:
    """把条目搬进垃圾桶并落 manifest。返回 {status, trash_id, deleted, warnings}。"""
    if planned["blocked"]:
        return {"status": "DENIED", "error": "TRASH_UNSAFE", "blocked": planned["blocked"],
                "hint": "整批未执行。软链与作用域根不搬；路径须在项目根内。"}

    movable = [e for e in planned["entries"] if e["kind"] != "missing" and e["path"] in planned["selected"]]
    if not movable:
        return {"status": "OK", "deleted": [], "count": 0,
                "hint": "没有可移动的条目（全部不存在）。"}

    trash_id = _new_id()
    bucket = os.path.join(_trash_root(project_root), trash_id)
    manifest = {
        "id": trash_id,
        "ts": int(time.time() * 1000),
        "reason": reason,
        "channel": channel,
        "root": TRASH_DIR + "/" + trash_id,
        "entries": [],
    }

    moved, failed = [], []
    for entry in movable:
        rel = entry["path"]
        src = os.path.join(project_root, rel)
        dst = os.path.join(bucket, "files", rel)
        try:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.move(src, dst)
            info = planned["status"].get(rel, {})
            manifest["entries"].append({
                **entry, "trash_path": os.path.relpath(dst, project_root).replace(os.sep, "/"),
                "git_state": info.get("state", "unknown"),
                "recoverable_via_git": info.get("recoverable", False),
            })
            moved.append({"path": rel, "kind": entry["kind"],
                          "trash_path": os.path.relpath(dst, project_root).replace(os.sep, "/"),
                          "git_state": info.get("state", "unknown")})
        except OSError as e:
            failed.append({"path": rel, "error": str(e)})

    if manifest["entries"]:
        try:
            with open(os.path.join(bucket, "manifest.json"), "w", encoding="utf-8") as f:
                json.dump(manifest, f, ensure_ascii=False, indent=2)
        except OSError:
            pass

    result = {"status": "PARTIAL" if failed else "OK", "trash_id": trash_id,
              "trash_root": manifest["root"], "deleted": moved, "count": len(moved),
              "reason": reason,
              "hint": f"内容在 {manifest['root']}/，恢复：python3 .mcp/cli.py restore {trash_id}"}
    if failed:
        result["failed"] = failed
        result["error"] = "TRASH_FAILED"

    orphan = orphan_meta_warnings(project_root, [m["path"] for m in moved])
    if orphan:
        result["warnings"] = orphan
    return result


def list_buckets(project_root: str) -> list[dict]:
    root = _trash_root(project_root)
    if not os.path.isdir(root):
        return []
    out = []
    for name in sorted(os.listdir(root), reverse=True):
        manifest_path = os.path.join(root, name, "manifest.json")
        try:
            with open(manifest_path, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError):
            continue
        out.append({"id": data.get("id", name), "ts": data.get("ts"),
                    "reason": data.get("reason", ""),
                    "count": len(data.get("entries", [])),
                    "paths": [e.get("path") for e in data.get("entries", [])][:10]})
    return out


def restore(project_root: str, trash_id: str, only: list[str] | None = None) -> dict:
    """把垃圾桶里的条目搬回原位。目标已存在时不覆盖 —— 覆盖等于丢掉别人的改动。"""
    bucket = os.path.join(_trash_root(project_root), trash_id)
    manifest_path = os.path.join(bucket, "manifest.json")
    try:
        with open(manifest_path, encoding="utf-8") as f:
            manifest = json.load(f)
    except (OSError, ValueError):
        return {"status": "DENIED", "error": "NO_SUCH_TRASH",
                "hint": f"找不到 {TRASH_DIR}/{trash_id}/manifest.json"}

    restored, blocked = [], []
    for entry in manifest.get("entries", []):
        rel = entry["path"]
        if only and rel not in only:
            continue
        src = os.path.join(project_root, entry["trash_path"])
        dst = os.path.join(project_root, rel)
        if not os.path.lexists(src):
            blocked.append({"path": rel, "state": "missing-in-trash"})
            continue
        if os.path.lexists(dst):
            blocked.append({"path": rel, "state": "target-exists",
                            "detail": "原位已有文件，不覆盖 —— 先确认那是谁的版本"})
            continue
        try:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.move(src, dst)
            restored.append(rel)
        except OSError as e:
            blocked.append({"path": rel, "state": "restore-failed", "detail": str(e)})

    out = {"status": "OK" if not blocked else "PARTIAL", "restored": restored,
           "count": len(restored)}
    if blocked:
        out["blocked"] = blocked
    return out


def empty(project_root: str, trash_id: str | None = None) -> dict:
    """彻底清空（不可恢复）。只建议在明确要腾空间时用。"""
    root = _trash_root(project_root)
    targets = [os.path.join(root, trash_id)] if trash_id else (
        [os.path.join(root, n) for n in os.listdir(root)] if os.path.isdir(root) else [])
    removed = []
    for target in targets:
        if not os.path.isdir(target):
            continue
        shutil.rmtree(target, ignore_errors=True)
        removed.append(os.path.basename(target))
    return {"status": "OK", "removed": removed, "count": len(removed)}
