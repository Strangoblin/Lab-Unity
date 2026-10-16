"""项目路径规则 — 作用域、工作目录边界、受管文件落点."""

import os

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# 基础作用域（仓库相对）—— validate_path 与工作目录校验同源，防止两处各列一份
ALLOWED_PREFIXES = [
    "Assets/Mine",
    ".agents/skills/unity-editor/scripts/roslyn",
    "tmp",
]

# 已迁出的 camera 只保留同一文件的权利，不授权所属 skill 目录。
CANONICAL_ROSLYN_ROOT = ".agents/skills/unity-editor/scripts/roslyn"
ALLOWED_EXACT_FILES = {".agents/skills/unity-render-debug/scripts/camera-offscreen-capture.cs"}


def _canonical(path: str) -> str:
    """比较用规范形：绝对化 + 分隔符归一 + 大小写归一（Windows）。

    为什么必须归一：Windows 上 os.path.join(PROJECT_ROOT, "Assets/Mine") 的前缀内部
    残留正斜杠（混合分隔符），而 os.path.commonpath 的返回值是全反斜杠形——
    `commonpath(...) == prefix` 的字符串相等因此在 Windows 上恒为 False。
    """
    return os.path.normcase(os.path.normpath(os.path.abspath(path)))


def validate_path(path: str) -> dict:
    """校验写入路径是否在合法目录内."""
    allowed_prefixes = [os.path.join(PROJECT_ROOT, p) for p in ALLOWED_PREFIXES]

    full = os.path.join(PROJECT_ROOT, path) if not os.path.isabs(path) else path
    full = os.path.abspath(full)
    resolved = os.path.realpath(full)
    c_full, c_resolved = _canonical(full), _canonical(resolved)

    for target in ALLOWED_EXACT_FILES:
        canonical_target = os.path.abspath(os.path.join(PROJECT_ROOT, target))
        if (full == canonical_target and resolved == canonical_target
                and (not os.path.lexists(full) or (os.path.isfile(full) and not os.path.islink(full)))):
            return {"status": "OK", "path": path, "full_path": full}

    for prefix in allowed_prefixes:
        c_prefix = _canonical(prefix)
        c_resolved_prefix = _canonical(os.path.realpath(prefix))
        try:
            if (os.path.commonpath([c_full, c_prefix]) == c_prefix
                    and os.path.commonpath([c_resolved, c_resolved_prefix]) == c_resolved_prefix):
                return {"status": "OK", "path": path, "full_path": full}
        except ValueError:
            continue   # 不同盘符（Windows）——不在任何前缀作用域内，按拒绝处理

    return {
        "status": "DENIED",
        "error": "PATH_NOT_ALLOWED",
        "path": path,
        "hint": f"路径必须在以下目录内: {', '.join(p + '/' for p in ALLOWED_PREFIXES)}；"
                f"或命中精确文件: {', '.join(sorted(ALLOWED_EXACT_FILES))}",
    }


# ════════════════════════════════════════════════════════════════
#  执行层 — 「这个文件放对地方了吗」（write_gated 调用）
#
#  与咨询层的分工：咨询层回答「该放哪」（从未生效），本层回答「放对了吗」。
#  规则照着 Assets/Mine 的**现实布局**写，不是照着理想布局写。
# ════════════════════════════════════════════════════════════════

# 类别 → 基础目录 —— check_placement 用它校验 `category` 注解与路径是否一致。
# （旧 g_file 门禁的「该放哪」咨询层已随该门禁删除；这里只留执行层要用的映射。）
CATEGORY_BASE: dict[str, str] = {
    "PostProcess": "Assets/Mine/Shaders/PostProcess",
    "Render":      "Assets/Mine/Shaders/Render",
    "Graph":       "Assets/Mine/Shaders/Graph",
    "Script":      "Assets/Mine/Scripts",
}

# 受管扩展名 —— 由 write_gated 落盘、因而需要管放置的文件类型。
#
# 白名单刻意**只收开发者自撰的代码与文档**。其余（.mat/.prefab/.png/.asset/
# .shadergraph/.exr/.meta/…）由 Unity 导入或工具产出，不在此列：把导入类型也列进来，
# 白名单会随 Unity 每加一种资产类型而失效，最终变成误拦 —— 那是限制功能使用。
MANAGED_EXTS = {".shader", ".hlsl", ".cs", ".compute", ".md"}

# 受管文件必须落在其一的顶层根。
#
# 为什么只到「顶层根」这一层：现实里 Shaders/<Cat>/<Effect>/ 之外，Scripts/<Module>/
# 还带 Editor/ Shaders/ Noises/ Water/ 等嵌套子目录，Scripts/TestAuto.cs 更是直接躺在
# 根下。任何按深度或「必须进效果子目录」的规则，第一次运行就会误拦既有文件。
# 本层只拦「扔在根下或扔进非代码区」这一类真实错误。
MANAGED_ROOTS = ("Effects", "Scripts", "Shaders", "Special")


def check_placement(path: str, is_new: bool, category: str = "") -> dict:
    """校验受管文件的落点。返回 {"errors": [...], "warnings": [...]}。

    强度与 norms.py 的「新增行 diff」同源：
      新文件   → 落点是一次全新的决定，错了就是 error
      已有文件 → 只是就地编辑，落点是历史决定；此时硬拦会让人改不动文件，
                 反而堵死修正的入口，故降为 warning

    非受管扩展名直接放行 —— .meta 要能跟着资产走，导入资产不该被路径规则干预。
    """
    errors: list[dict] = []
    warnings: list[dict] = []

    ext = os.path.splitext(path)[1].lower()
    if ext not in MANAGED_EXTS:
        return {"errors": errors, "warnings": warnings}

    rel = path.replace("\\", "/")
    prefix = "Assets/Mine/"
    bucket = errors if is_new else warnings

    if not rel.startswith(prefix):
        return {"errors": errors, "warnings": warnings}   # 非 Assets/Mine 作用域不归本层管

    rest = rel[len(prefix):]
    segments = rest.split("/")
    if len(segments) < 2 or segments[0] not in MANAGED_ROOTS:
        bucket.append({
            "id": "placement-root",
            "level": "error" if is_new else "warning",
            "name": "受管文件落点",
            "detail": f"{ext} 应落在 {'/'.join(MANAGED_ROOTS)} 之一的子目录下，"
                      f"当前是 Assets/Mine/{rest}。",
            "path": path,
        })

    # category 注解与路径前缀不一致 → 提示（注解此前是自由文本，记录不校验）
    if category and category in CATEGORY_BASE:
        base = CATEGORY_BASE[category]
        if not rel.startswith(base + "/") and rel != base:
            warnings.append({
                "id": "placement-category",
                "level": "warning",
                "name": "category 注解与路径不符",
                "detail": f"category='{category}' 对应 {base}/，"
                          f"而路径是 {path}。",
                "path": path,
            })

    return {"errors": errors, "warnings": warnings}


# ════════════════════════════════════════════════════════════════
#  工作目录边界 — 「本次任务允许在哪个子树里操作」（用户下达，任务级）
#
#  基础作用域（validate_path）回答「框架允许写哪里」，本层回答「这次任务只许动哪里」。
#  是本层**收窄**而非放宽：工作目录必须先落在基础作用域内（gate_set_workspace 校验）。
#  applies_to 之外的路径不归本层管 —— tmp/ 这类非工程资产不受任务边界约束。
# ════════════════════════════════════════════════════════════════

def _rel_posix(path: str) -> str:
    rel = str(path).replace("\\", "/")
    if os.path.isabs(rel):
        try:
            rel = os.path.relpath(rel, PROJECT_ROOT)
        except ValueError:
            return rel
    return rel.lstrip("./")


def _under(rel: str, prefix: str) -> bool:
    prefix = prefix.rstrip("/")
    return rel == prefix or rel.startswith(prefix + "/")


def build_scope_error(kind: str, detail: str, path: str) -> dict:
    return {"id": kind, "level": "error", "name": "工作目录边界", "detail": detail, "path": path}


def check_workspace(path: str, roots: list, required: bool, applies_to: list) -> dict:
    """工作目录检查 → {"errors": [...], "warnings": [...]}。

    roots 为空 = 未声明工作目录；required 决定这是不是错误。
    """
    errors: list = []
    warnings: list = []
    rel = _rel_posix(path)

    if not any(_under(rel, p) for p in applies_to):
        return {"errors": errors, "warnings": warnings}   # 非工程资产区（如 tmp/）不设边界

    if not roots:
        if required:
            errors.append(build_scope_error(
                "workspace-required",
                f"未声明工作目录 —— {rel} 在任务边界区内，先 gate_set_workspace(<目录>)。",
                path))
        return {"errors": errors, "warnings": warnings}

    if not any(_under(rel, r) for r in roots):
        errors.append(build_scope_error(
            "workspace-root",
            f"{rel} 不在本次工作目录内。已授权: {roots}。"
            f"确需扩展请走 gate_request_workspace(<目录>, <原因>) 交人工批准。",
            path))
    return {"errors": errors, "warnings": warnings}


def check_workspace_roots(roots: list, base_allowed: list) -> dict:
    """校验工作目录本身合法: 每个 root 必须落在基础作用域内（收窄而非放宽）。"""
    bad = []
    for r in roots:
        rel = _rel_posix(r)
        full = os.path.join(PROJECT_ROOT, rel)
        resolved = os.path.realpath(full)
        if not any(_under(rel, b) and _under(resolved, PROJECT_ROOT) for b in base_allowed):
            bad.append(r)
    if bad:
        return {"status": "DENIED", "error": "WORKSPACE_NOT_ALLOWED", "offending": bad,
                "hint": f"工作目录必须先落在基础作用域内: {base_allowed}"}
    return {"status": "OK"}
