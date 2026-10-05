"""项目路径规则 — 文件类型 + 类别 → 合法目录."""

import os
from typing import Optional

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# ════════════════════════════════════════════════════════════════
#  咨询层（退役）— 「这个文件该放哪」
#
#  CATEGORY_BASE / VALID_FILE_TYPES / resolve_target_dir 服务于已退役的 g_file 门禁，
#  当前唯一调用方 gates/g_file.py 不在任何配方中（调用即 GATE_NOT_IN_RECIPE），
#  故本层实际不可达。保留是为了不动物化 GATE_REGISTRY 的退役集合（测试钉住了它）。
#  生效的放置校验在下方「执行层」。
# ════════════════════════════════════════════════════════════════

# ── 类别 → 基础目录 ──
CATEGORY_BASE: dict[str, str] = {
    "PostProcess": "Assets/Mine/Shaders/PostProcess",
    "Render":      "Assets/Mine/Shaders/Render",
    "Graph":       "Assets/Mine/Shaders/Graph",
    "Script":      "Assets/Mine/Scripts",
}

VALID_FILE_TYPES = {".shader", ".compute", ".hlsl", ".cs"}
VALID_CATEGORIES = set(CATEGORY_BASE.keys())

# ── 类别判断示例 ──
CATEGORY_EXAMPLES: dict[str, list[str]] = {
    "PostProcess": ["SSR", "SSSM", "Kuwahara", "PCSS", "POSS", "DDOF", "SNN", "RimToon", "SSC", "SSO", "SSL"],
    "Render":      ["Water", "Grass", "XRay", "RainDrops", "SimpleGrass"],
    "Graph":       ["CelToon", "Cloud", "Outline", "Scan", "Gate", "SunShadow", "TreeLeaves", "CubeSphereCloud", "WorldChange", "WaterTotal", "HeightCloud"],
    "Script":      ["CamController", "InteractionManager", "FGDLutBaker", "NoiseGenerator", "InstanceManager", "CurveGenerator", "CustomRenderer", "Picker"],
}


def resolve_target_dir(file_type: str, category: str, effect: str = "") -> dict:
    """根据类型 + 类别 + 效果名，返回合法的 TargetDir.

    category=PostProcess/Render/Graph: TargetDir = <base>/<effect>/
    category=Script:                    TargetDir = <base>/<module>/
    """
    if file_type not in VALID_FILE_TYPES:
        return {"status": "DENIED", "error": "INVALID_FILE_TYPE",
                "hint": f"FileType 必须为 {VALID_FILE_TYPES}。收到: '{file_type}'"}

    if category not in VALID_CATEGORIES:
        return {"status": "DENIED", "error": "INVALID_CATEGORY",
                "hint": f"Category 必须为 {VALID_CATEGORIES}。收到: '{category}'"}

    base = CATEGORY_BASE[category]

    if effect:
        target_dir = os.path.join(base, effect)
    else:
        target_dir = base

    # 转为项目相对路径
    rel_dir = os.path.relpath(target_dir, PROJECT_ROOT) if os.path.isabs(target_dir) else target_dir

    # 检查目录是否存在
    full_path = os.path.join(PROJECT_ROOT, rel_dir)
    dir_exists = os.path.isdir(full_path)

    # 子目录是否已存在效果
    existing_effects = []
    if dir_exists:
        try:
            existing_effects = [d for d in sorted(os.listdir(full_path))
                              if os.path.isdir(os.path.join(full_path, d))
                              and not d.startswith(".") and not d.startswith("_")]
        except PermissionError:
            pass

    return {
        "status": "OK",
        "file_type": file_type,
        "category": category,
        "effect": effect,
        "target_dir": rel_dir,
        "full_path": full_path,
        "dir_exists": dir_exists,
        "existing_effects": existing_effects[:15],  # 截断避免过长
        "category_examples": CATEGORY_EXAMPLES.get(category, []),
    }


def validate_path(path: str) -> dict:
    """校验写入路径是否在合法目录内."""
    allowed_prefixes = [
        os.path.join(PROJECT_ROOT, "Assets/Mine"),
        os.path.join(PROJECT_ROOT, ".agents", "agents", "unity-developer", "scripts", "roslyn"),
        os.path.join(PROJECT_ROOT, "tmp"),
    ]

    full = os.path.join(PROJECT_ROOT, path) if not os.path.isabs(path) else path
    full = os.path.abspath(full)
    resolved = os.path.realpath(full)

    for prefix in allowed_prefixes:
        if (os.path.commonpath([full, prefix]) == prefix
                and os.path.commonpath([resolved, os.path.realpath(prefix)]) == os.path.realpath(prefix)):
            return {"status": "OK", "path": path, "full_path": full}

    return {
        "status": "DENIED",
        "error": "PATH_NOT_ALLOWED",
        "path": path,
        "hint": "路径必须在以下目录内: Assets/Mine/, .agents/agents/unity-developer/scripts/roslyn/, tmp/",
    }


# ════════════════════════════════════════════════════════════════
#  执行层 — 「这个文件放对地方了吗」（write_gated 调用）
#
#  与咨询层的分工：咨询层回答「该放哪」（从未生效），本层回答「放对了吗」。
#  规则照着 Assets/Mine 的**现实布局**写，不是照着理想布局写。
# ════════════════════════════════════════════════════════════════

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
