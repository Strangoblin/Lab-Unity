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
import hashlib, json, os, re, stat, subprocess
from datetime import datetime, timezone

# Unity .meta 的 guid 行：`guid: <32 位小写十六进制>`
GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)

# 单次 git / grep 调用的上限；超时按「无法判定」处理，不放行
TIMEOUT_GIT = 60
TIMEOUT_GREP = 120

# 结构类拒绝的错误码 → 对外 state 名（调用方与文档按 state 分流，不用猜字符串）
STATE_BY_ERROR = {
    "SYMLINK_NOT_ALLOWED": "symlink",
    "UNSUPPORTED_TYPE": "unsupported-type",
    "PATH_NOT_ALLOWED": "path-not-allowed",
    "PROTECTED_ROOT": "protected-root",
    "PRECHECK_FAILED": "precheck-failed",
}


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
        queries = [
            _git(project_root, "ls-files", "-z", "--", *existing),
            _git(project_root, "diff", "--no-renames", "--name-only", "-z", "--", *existing),
            _git(project_root, "diff", "--cached", "--no-renames", "--name-only", "-z", "--", *existing),
        ]
        if any(q.returncode != 0 for q in queries):
            raise OSError("git 状态查询失败")
        tracked, unstaged, staged = (_nul_list(q.stdout) for q in queries)
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
        if r.returncode not in (0, 1):
            raise OSError("grep 查询失败")
    except (subprocess.SubprocessError, OSError):
        return [f"<grep 失败: 无法排除 {guid} 是否仍被引用>"]
    # 逐行读取，不用 -Z：macOS/BSD grep 的 -Z 不产出 NUL 分隔，会把所有命中并成一条
    return [p for p in r.stdout.splitlines() if p and p != meta_rel]


# ═══ 目录预检 / 执行 —— 不跟随软链，不使用递归 rmtree ═══

# Windows 没有 O_NOFOLLOW / O_DIRECTORY，也不支持 dir_fd；缺失时退化到路径级
# 复验：预检已用 lstat 拒绝软链，open 后再比对 inode 兑住 lstat→open 之间的替换。
_O_NOFOLLOW = getattr(os, "O_NOFOLLOW", 0)
_O_DIRECTORY = getattr(os, "O_DIRECTORY", 0)
_DIR_FD = os.open in os.supports_dir_fd


def _identity(st: os.stat_result) -> tuple:
    return st.st_dev, st.st_ino, stat.S_IFMT(st.st_mode)


def _fingerprint(full: str) -> dict:
    """目录保存直接子项；普通文件保存内容 hash 与 inode，检测预检后的变化。"""
    st = os.lstat(full)
    if stat.S_ISLNK(st.st_mode):
        raise ValueError("SYMLINK_NOT_ALLOWED")
    identity = list(_identity(st))
    if stat.S_ISDIR(st.st_mode):
        return {"kind": "directory", "identity": identity,
                "children": sorted(os.listdir(full))}
    if not stat.S_ISREG(st.st_mode):
        raise ValueError("UNSUPPORTED_TYPE")
    # 无 O_NOFOLLOW 的平台靠下面的 fstat 身份比对兑住 lstat→open 的替换竞态
    fd = os.open(full, os.O_RDONLY | _O_NOFOLLOW)
    try:
        if _identity(os.fstat(fd)) != _identity(st):
            raise OSError("文件在预检期间发生变化")
        digest = hashlib.sha256()
        while True:
            data = os.read(fd, 1024 * 1024)
            if not data:
                break
            digest.update(data)
        after = os.fstat(fd)
        if (st.st_size, st.st_mtime_ns, st.st_ctime_ns) != (
                after.st_size, after.st_mtime_ns, after.st_ctime_ns):
            raise OSError("文件在预检期间发生变化")
        return {"kind": "file", "identity": identity, "size": st.st_size,
                "mtime_ns": st.st_mtime_ns, "ctime_ns": st.st_ctime_ns,
                "sha256": digest.hexdigest()}
    finally:
        os.close(fd)


def _lineage(project_root: str, rel: str, anchors: dict) -> None:
    """记住项目根及每个祖先目录的 inode，阻止祖先被替换或改成软链。"""
    current = project_root
    parts = [""] + rel.split("/")[:-1]
    key = ""
    for part in parts:
        if part:
            current = os.path.join(current, part)
            key = part if not key else key + "/" + part
        st = os.lstat(current)
        if stat.S_ISLNK(st.st_mode):
            raise ValueError("SYMLINK_NOT_ALLOWED")
        if not stat.S_ISDIR(st.st_mode):
            raise ValueError("UNSUPPORTED_TYPE")
        identity = list(_identity(st))
        if key in anchors and anchors[key] != identity:
            raise OSError("祖先目录在预检期间发生变化")
        anchors[key] = identity


def plan(project_root: str, checked: list[tuple[str, str]], *, recursive: bool = False,
         protected_roots: list[str] | None = None) -> dict:
    """展开精确删除清单。非空目录须 recursive；根目录和所有软链均拒绝。"""
    root = os.path.abspath(project_root)
    protected = {root}
    for p in protected_roots or []:
        protected.add(os.path.abspath(os.path.join(root, p)))
    roots, blocked, entries, anchors = {}, [], {}, {}
    for original, full in checked:
        full = os.path.abspath(full)
        try:
            if os.path.commonpath([full, root]) != root:
                raise ValueError("PATH_NOT_ALLOWED")
        except ValueError:
            blocked.append({"path": original, "state": "path-not-allowed",
                            "error": "PATH_NOT_ALLOWED", "recoverable": False})
            continue
        rel = os.path.relpath(full, root).replace(os.sep, "/")
        if full in protected or os.path.realpath(full) in protected:
            blocked.append({"path": rel, "state": "protected-root",
                            "error": "PROTECTED_ROOT", "recoverable": False})
            continue
        roots[rel] = full

    # 父目录请求覆盖子项请求；执行不会重复删除同一路径。
    selected = []
    for rel in sorted(roots, key=lambda p: (p.count("/"), p)):
        if not any(rel.startswith(parent + "/") for parent in selected):
            selected.append(rel)

    def visit(rel: str, full: str) -> None:
        try:
            _lineage(root, rel, anchors)
            if not os.path.lexists(full):
                entries[rel] = {"path": rel, "full_path": full, "kind": "missing"}
                return
            snap = _fingerprint(full)
            entries[rel] = {"path": rel, "full_path": full, "kind": snap["kind"],
                            "snapshot": snap}
            if snap["kind"] == "directory":
                if snap["children"] and not recursive:
                    blocked.append({"path": rel, "state": "directory-not-empty",
                                    "error": "DIRECTORY_NOT_EMPTY", "recoverable": False,
                                    "detail": "非空目录须显式 recursive=True"})
                else:
                    for name in snap["children"]:
                        visit(rel + "/" + name, os.path.join(full, name))
        except ValueError as e:
            code = str(e)
            blocked.append({"path": rel,
                            "state": STATE_BY_ERROR.get(code, code.lower().replace("_", "-")),
                            "error": code, "recoverable": False})
        except OSError as e:
            blocked.append({"path": rel, "state": STATE_BY_ERROR["PRECHECK_FAILED"],
                            "error": "PRECHECK_FAILED", "recoverable": False,
                            "detail": str(e)})

    for rel in selected:
        visit(rel, roots[rel])
    return {"blocked": blocked, "entries": list(entries.values()), "anchors": anchors,
            "checked": [(e["path"], e["full_path"]) for e in entries.values()],
            "requested_paths": list(roots), "recursive": recursive,
            "status": {}}   # 调用方（judgement.delete）填入每个路径的分类结果


def _parent_fd(project_root: str, rel: str, anchors: dict) -> tuple[int | None, str]:
    """定位待删项的父目录。

    支持 dir_fd 的平台逐级以 O_NOFOLLOW 打开祖先并返回 fd；不支持时（Windows）
    改用路径级复验（_lineage 逐级比对 inode）并返回 None。第二项：fd 分支是子项名，
    路径分支是完整路径，交给 _*_child 助手分流。
    """
    root = os.path.abspath(project_root)
    parts = rel.split("/")
    if not _DIR_FD:
        _lineage(root, rel, anchors)
        return None, os.path.join(root, *parts)
    fd = os.open(root, os.O_RDONLY | _O_DIRECTORY | _O_NOFOLLOW)
    try:
        if list(_identity(os.fstat(fd))) != anchors.get(""):
            raise OSError("项目根在预检后发生变化")
        key = ""
        for part in parts[:-1]:
            child = os.open(part, os.O_RDONLY | _O_DIRECTORY | _O_NOFOLLOW, dir_fd=fd)
            os.close(fd)
            fd = child
            key = part if not key else key + "/" + part
            if list(_identity(os.fstat(fd))) != anchors.get(key):
                raise OSError("祖先目录在预检后发生变化")
        return fd, parts[-1]
    except Exception:
        os.close(fd)
        raise


def _stat_child(handle: int | None, name: str) -> os.stat_result:
    """按句柄 stat 子项；无 dir_fd 的平台按路径 stat，同样不跟随软链。"""
    if handle is None:
        return os.stat(name, follow_symlinks=False)
    return os.stat(name, dir_fd=handle, follow_symlinks=False)


def _unlink_child(handle: int | None, name: str) -> None:
    if handle is None:
        os.unlink(name)
    else:
        os.unlink(name, dir_fd=handle)


def _rmdir_child(handle: int | None, name: str) -> None:
    if handle is None:
        os.rmdir(name)
    else:
        os.rmdir(name, dir_fd=handle)


def _close_handle(handle: int | None) -> None:
    if handle is not None:
        os.close(handle)


def execute(project_root: str, planned: dict) -> dict:
    """先整批复核，再按已检清单 unlink/rmdir；发生竞态或 I/O 失败立即停止并如实报告。"""
    deleted, failed = [], []
    if planned.get("blocked"):
        return {"status": "ERROR", "error": "PLAN_BLOCKED", "deleted": [],
                "failed": planned["blocked"]}
    entries = [e for e in planned["entries"] if e["kind"] != "missing"]
    try:
        for e in planned["entries"]:
            rel, full = e["path"], e["full_path"]
            handle, name = _parent_fd(project_root, rel, planned["anchors"])
            try:
                if e["kind"] == "missing":
                    try:
                        _stat_child(handle, name)
                    except FileNotFoundError:
                        continue
                    raise OSError("原先不存在的路径在预检后出现")
                if _fingerprint(full) != e["snapshot"]:
                    raise OSError("路径在预检后发生变化")
            finally:
                _close_handle(handle)
    except (OSError, ValueError) as ex:
        return {"status": "ERROR", "error": "PRECHECK_CHANGED", "deleted": [],
                "failed": [{"path": rel, "error": str(ex)}]}

    # 深层项先删；同层文件先于目录。仅处理枚举到的条目，rmdir 拒绝新增子项。
    ordered = sorted(entries, key=lambda e: (-e["path"].count("/"),
                                            e["kind"] == "directory", e["path"]))
    for e in ordered:
        rel = e["path"]
        try:
            handle, name = _parent_fd(project_root, rel, planned["anchors"])
            try:
                current = _stat_child(handle, name)
                if list(_identity(current)) != e["snapshot"]["identity"]:
                    raise OSError("路径在执行期间被替换")
                if e["kind"] == "file":
                    if _fingerprint(e["full_path"]) != e["snapshot"]:
                        raise OSError("文件在执行期间发生变化")
                    _unlink_child(handle, name)
                else:
                    _rmdir_child(handle, name)
                info = (planned.get("status") or {}).get(rel) or {
                    "state": "unknown", "recoverable": False}
                deleted.append({"path": rel, "kind": e["kind"], "state": info["state"],
                                "recoverable": info["recoverable"]})
            finally:
                _close_handle(handle)
        except (OSError, ValueError) as ex:
            failed.append({"path": rel, "error": str(ex)})
            break
    return {"status": "ERROR" if failed else "OK", "deleted": deleted, "failed": failed,
            **({"error": "DELETE_FAILED"} if failed else {})}


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


# ════════════════════════════════════════════════════════════════
#  共用判定 — MCP delete_gated 与 CLI 必须同源，不能各判一套
# ════════════════════════════════════════════════════════════════

def evaluate(project_root: str, checked: list[tuple[str, str]]) -> dict:
    """收集阻断项，并回带分类结果。

    checked = [(项目相对路径, 绝对路径)]。
    返回 {"blocked": [...], "status": {path: classify 结果}}；
    blocked 为空即整批可删（调用方负责 all-or-nothing）。
    """
    status = classify(project_root, [p for p, _ in checked])
    blocked = [{"path": p, **info} for p, info in status.items()
               if info["state"] in ("tracked-dirty", "unknown")]

    deleting = {p for p, _ in checked}
    for p, full in checked:
        if os.path.isdir(full) and not os.path.islink(full):
            # 空目录不承载文件内容；子文件的 dirty / GUID 安全性已各自判定。
            status[p] = {"state": "directory", "recoverable": True}
            continue
        guid = read_guid(full)
        if not guid:
            continue
        refs = [r for r in guid_referenced_elsewhere(project_root, p, guid) if r not in deleting]
        if refs:
            blocked.append({"path": p, "state": "guid-referenced", "recoverable": False,
                            "detail": f"GUID {guid} 仍被其他文件引用",
                            "referenced_by": refs[:5]})
    return {"blocked": blocked, "status": status}


# ════════════════════════════════════════════════════════════════
#  删除审计 — 写入有产物即记录，删除没有，故必须落盘
# ════════════════════════════════════════════════════════════════

AUDIT_FILE = os.path.join(".mcp", "deletes.jsonl")


def append_audit(project_root: str, record: dict) -> None:
    """追加一条删除记录（JSONL）。失败静默 —— 与 state.json 同策略，审计不阻塞删除。"""
    try:
        path = os.path.join(project_root, AUDIT_FILE)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps({"ts": datetime.now(timezone.utc).isoformat(), **record},
                               ensure_ascii=False) + "\n")
    except OSError:
        pass
