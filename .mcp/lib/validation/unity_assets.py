"""Unity 资产事实 — git 状态、GUID 关系。删除与移动共用。

从 v3 的 `deletion.py` 抽出来：这些函数不是「删除的判定」，而是**关于资产的客观事实**
（这个文件在 git 里是什么状态、这个 GUID 还有谁在用）。v3 把事实直接当成了拒绝理由，
于是 tracked-dirty 就等于「不许删」。v4 里它们只是 manifest 字段和提示 ——
事实照记，结论交给 trash 与调用方。
"""

from __future__ import annotations

import os
import re
import subprocess

# Unity .meta 的 guid 行：`guid: <32 位小写十六进制>`
GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)

# 单次 git / grep 调用的上限；超时按「无法判定」处理。
TIMEOUT_GIT = 60
TIMEOUT_GREP = 120


def _git(project_root: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(("git",) + args, cwd=project_root,
                          capture_output=True, text=True, timeout=TIMEOUT_GIT)


def _nul_list(stdout: str) -> set[str]:
    return {p for p in stdout.split("\0") if p}


def classify(project_root: str, rel_paths: list[str]) -> dict[str, dict]:
    """按 git 工作区状态给每个路径分类。

    三次 git 调用（不随文件数增长）：`ls-files` 定已入库集合，
    `diff --name-only`（工作区 / 索引）定改动集合。
    全部用 `-z --no-renames`：只回路径列表，无状态码、无重命名对，解析不会错位。
    """
    result: dict[str, dict] = {}
    if not rel_paths:
        return result

    existing, missing = [], []
    for p in rel_paths:
        (existing if os.path.lexists(os.path.join(project_root, p)) else missing).append(p)
    for p in missing:
        result[p] = {"state": "missing", "recoverable": True}

    if not existing:
        return result

    try:
        queries = [
            _git(project_root, "ls-files", "-z", "--", *existing),
            _git(project_root, "diff", "--no-renames", "--name-only", "-z", "--", *existing),
            _git(project_root, "diff", "--cached", "--no-renames", "--name-only", "-z", "--",
                 *existing),
        ]
        if any(q.returncode != 0 for q in queries):
            raise OSError("git 状态查询失败")
        tracked, unstaged, staged = (_nul_list(q.stdout) for q in queries)
    except (subprocess.SubprocessError, OSError):
        # 判不了就说判不了 —— 不再用「按最坏情况拒绝」，因为 trash 已经兜住内容。
        for p in existing:
            result[p] = {"state": "unknown", "recoverable": False,
                         "detail": "git 状态无法判定"}
        return result

    for p in existing:
        if p in unstaged or p in staged:
            where = "工作区" if p in unstaged else "索引"
            result[p] = {"state": "tracked-dirty", "recoverable": False,
                         "detail": f"已入库但{where}有未提交改动"}
        elif p in tracked:
            result[p] = {"state": "tracked-clean", "recoverable": True}
        else:
            result[p] = {"state": "untracked", "recoverable": False}
    return result


def read_guid(full_path: str) -> str:
    """读 .meta 的 GUID；非 .meta 或无 guid 行返回空串。"""
    if not full_path.endswith(".meta"):
        return ""
    try:
        with open(full_path, encoding="utf-8", errors="replace") as f:
            m = GUID_RE.search(f.read(4096))
        return m.group(1) if m else ""
    except OSError:
        return ""


def guid_referenced_elsewhere(project_root: str, meta_rel: str, guid: str,
                              assets_dir: str = "Assets") -> list[str]:
    """返回 `Assets/` 下引用该 GUID 的其他文件（相对项目根，已剔除本 .meta 自身）。

    注意：这里问的是「谁**引用**它」。Unity 的引用是按 GUID 记录的，所以删了资产、
    引用还在，场景里就变成 Missing。v4 不阻断，只把结果写进 manifest。
    """
    if not guid or not os.path.isdir(os.path.join(project_root, assets_dir)):
        return []
    try:
        r = subprocess.run(("grep", "-rlF", guid, assets_dir), cwd=project_root,
                           capture_output=True, text=True, timeout=TIMEOUT_GREP)
        if r.returncode not in (0, 1):
            raise OSError("grep 查询失败")
    except (subprocess.SubprocessError, OSError):
        return [f"<grep 失败: 无法排除 {guid} 是否仍被引用>"]
    # 逐行读取，不用 -Z：macOS/BSD grep 的 -Z 不产出 NUL 分隔，会把所有命中并成一条
    return [p for p in r.stdout.splitlines() if p and p != meta_rel]


def find_guid_owners(project_root: str, guid: str, exclude: list[str],
                     assets_dir: str = "Assets") -> list[str]:
    """返回 `Assets/` 下**声明**该 GUID 的其他 .meta。

    与 `guid_referenced_elsewhere` 的差别：那个问「谁引用它」，这个问「谁也声称拥有它」。
    同 GUID 两份 = Unity 报错，这是移动才需要问的问题。
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
        if not line or line in excluded or not line.endswith(".meta"):
            continue
        if read_guid(os.path.join(project_root, line)) == guid:
            owners.append(line)
    return owners


def orphan_meta_warnings(project_root: str, touched: list[str]) -> list[dict]:
    """删/移资产却留下同名 .meta（或反之）时提示 —— Unity 会因此产生孤儿。

    只提示不阻断：成对处理是调用方的责任，工具不擅自扩大操作范围。
    """
    touched_set = set(touched)
    warnings = []
    for p in touched:
        if p.endswith(".meta"):
            asset = p[:-len(".meta")]
            if os.path.lexists(os.path.join(project_root, asset)) and asset not in touched_set:
                warnings.append({"path": p, "id": "orphan-asset",
                                 "detail": f"保留的 {asset} 将失去 .meta（GUID 丢失，引用断裂）"})
        else:
            meta = p + ".meta"
            if os.path.lexists(os.path.join(project_root, meta)) and meta not in touched_set:
                warnings.append({"path": p, "id": "orphan-meta",
                                 "detail": f"遗留的 {meta} 将成为孤儿 .meta"})
    return warnings
