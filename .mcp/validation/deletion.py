"""删除后果验证 — 写入门禁（norms.py）的对称面。

删除没有「内容」可查，所以后果验证落在**不可恢复性**上：
删文件真正不可逆的损失，是丢掉 git 恢复不了的东西。

| 工作区状态 | 含义 | 处置 |
|---|---|---|
| `tracked-clean` | 已入库且工作区干净 | 放行 —— `git checkout` 可恢复 |
| `tracked-dirty` | 已入库但有未提交改动 | **硬拦** —— 删了只剩历史版本 |
| `untracked` | 未入库（含 .gitignore 命中） | 放行但标注不可恢复 |
| `missing` | 不存在 | 跳过（批量清单可重复执行） |

另有 Unity 专属检查：删 `.meta` 前查其 GUID 是否仍被 `Assets/` 下其他文件引用。
"""

from __future__ import annotations
import os, re, subprocess

# Unity .meta 的 guid 行：`guid: <32 位小写十六进制>`
GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)

# 单次 git / grep 调用的上限；超时按「无法判定」处理，不放行
TIMEOUT_GIT = 60
TIMEOUT_GREP = 120


def _git(project_root: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(("git",) + args, cwd=project_root,
                          capture_output=True, text=True, timeout=TIMEOUT_GIT)


def _nul_list(stdout: str) -> set[str]:
    return {p for p in stdout.split("\0") if p}


def classify(project_root: str, rel_paths: list[str]) -> dict[str, dict]:
    """按 git 工作区状态给每个路径分类。

    三次 git 调用（不按文件数增长）：`ls-files` 定已入库集合，
    `diff --name-only`（工作区 / 索引）定改动集合。
    全部用 `-z --no-renames`：只回路径列表，无状态码、无重命名对，
    解析不会错位。
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
        tracked = _nul_list(_git(project_root, "ls-files", "-z", "--", *existing).stdout)
        unstaged = _nul_list(_git(project_root, "diff", "--no-renames", "--name-only",
                                  "-z", "--", *existing).stdout)
        staged = _nul_list(_git(project_root, "diff", "--cached", "--no-renames",
                                "--name-only", "-z", "--", *existing).stdout)
    except (subprocess.SubprocessError, OSError):
        # 判定不了就不能放行 —— 未知状态按最坏情况处理
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
    """返回 `Assets/` 下引用该 GUID 的其他文件（相对项目根，已剔除本 .meta 自身）。"""
    if not guid or not os.path.isdir(os.path.join(project_root, assets_dir)):
        return []
    try:
        r = subprocess.run(("grep", "-rlF", guid, assets_dir), cwd=project_root,
                           capture_output=True, text=True, timeout=TIMEOUT_GREP)
    except (subprocess.SubprocessError, OSError):
        return [f"<grep 失败: 无法排除 {guid} 是否仍被引用>"]
    return [line for line in r.stdout.splitlines() if line and line != meta_rel]


def orphan_meta_warnings(project_root: str, deleting: list[str]) -> list[dict]:
    """删资产却留下同名 .meta（或反之）时提示 —— Unity 会因此产生孤儿。

    只提示不阻断：成对删除是调用方的责任，工具不擅自扩大删除范围。
    """
    deleting_set = set(deleting)
    warnings = []
    for p in deleting:
        if p.endswith(".meta"):
            asset = p[:-len(".meta")]
            if os.path.lexists(os.path.join(project_root, asset)) and asset not in deleting_set:
                warnings.append({"path": p, "id": "orphan-asset",
                                 "detail": f"保留的 {asset} 将失去 .meta（GUID 丢失，引用断裂）"})
        else:
            meta = p + ".meta"
            if os.path.lexists(os.path.join(project_root, meta)) and meta not in deleting_set:
                warnings.append({"path": p, "id": "orphan-meta",
                                 "detail": f"遗留的 {meta} 将成为孤儿 .meta"})
    return warnings
