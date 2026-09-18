"""项目路径规则 — 文件类型 + 类别 → 合法目录."""

import os
from typing import Optional

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

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
