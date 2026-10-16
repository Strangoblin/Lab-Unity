"""规范数据化 — 机械可校验的结构规范规则（后果验证门禁 · 规则表）.

级别说明: 表内 level 是**固有级别**；实际生效级别由 policy.json 覆盖（只能放宽）。

来源:
  references/urp-shader-lib/shader-structure.md（§1 整体布局 / §7 注释规范）
  references/csharp-dev/script-structure.md（§1 整体布局 / §6 注释规范）

两级:
  error    — 阻断写入（write_gated 返回 DENIED）
  warning  — 提示不阻断（写入放行，结果携带提示）

新增行检测: 对已存在的文件只检查本次写入新增的行（新内容 - 现有文件行集），
历史遗留不合规（旧文件早于规范）不阻断新写入；全新文件与 CLI 检查为全量。

注: from __future__ import annotations — CLI 需兼容系统 python3（3.9，无 PEP 604 语法）。
"""

from __future__ import annotations

import os
import re

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# ── 规则表 — id / 名称 / 规范来源 / 适用扩展名 / 级别 / 检测方式 ──
NORM_RULES: list[dict] = [
    {
        "id": "shader-decl",
        "name": "Shader 声明",
        "source": "shader-structure.md §1 整体布局",
        "exts": [".shader"],
        "level": "error",
        "scope": "content",                      # 全量必须命中
        "match": re.compile(r'Shader\s+"[^"]+"'),
        "detail": "文件必须包含 Shader \"名称\" 声明。",
    },
    {
        "id": "cs-type-decl",
        "name": "类型声明",
        "source": "script-structure.md §1 整体布局",
        "exts": [".cs"],
        "level": "error",
        "scope": "content",
        "match": re.compile(r'\b(class|struct|interface|enum|record)\s+\w+'),
        "detail": "文件必须包含类型声明 (class/struct/interface/enum/record)。",
    },
    {
        "id": "region-added",
        "name": "禁止 #region",
        "source": "script-structure.md §6 注释规范·禁止行为",
        "exts": [".cs"],
        "level": "error",
        "scope": "added",                        # 仅新增行
        "match": re.compile(r'#region\b'),
        "detail": "不使用 #region（与区块注释线冲突，风格不统一）。",
    },
    {
        "id": "divider-added",
        "name": "分隔线风格",
        "source": "shader-structure.md §7 / script-structure.md §6 禁止行为",
        "exts": [".shader", ".hlsl", ".cs"],
        "level": "warning",
        "scope": "added",
        "match": re.compile(r'^\s*//\s*[-=]{4,}\s*$'),
        "detail": "不用 // --- 或 // === 纯分隔线，统一使用 // ══════... 装饰块。",
    },
]


def _existing_lines(path: str) -> set[str]:
    """现有文件的行集合（不存在返回空集）."""
    if not os.path.isfile(path):
        return set()
    with open(path, encoding="utf-8") as f:
        return set(f.read().splitlines())


def _lines_to_check(content: str, existing: set[str] | None) -> list[tuple[int, str]]:
    """待检查行: existing=None 全量；否则仅新增行."""
    lines = content.splitlines()
    if existing is None:
        return list(enumerate(lines, 1))
    return [(i, ln) for i, ln in enumerate(lines, 1) if ln not in existing]


def check_content(path: str, content: str, existing: set[str] | None = None) -> dict:
    """对 (path, content) 执行规范检查.

    Args:
        path: 项目相对路径（扩展名决定适用规则）
        content: 写入内容
        existing: 现有文件行集合；None = 全量检查（新文件 / CLI 检查）
    Returns:
        {"status": "OK"|"VIOLATIONS", "errors": [...], "warnings": [...], "checked": <ext>}
    """
    ext = os.path.splitext(path)[1].lower()
    errors: list[dict] = []
    warnings: list[dict] = []
    checked = _lines_to_check(content, existing)

    for rule in NORM_RULES:
        if ext not in rule["exts"]:
            continue
        if rule["scope"] == "content":
            # 必须命中 → 未命中即违规
            if rule["match"].search(content) is None:
                violation = {
                    "id": rule["id"], "level": rule["level"], "name": rule["name"],
                    "source": rule["source"], "lines": [], "detail": rule["detail"],
                }
                (errors if rule["level"] == "error" else warnings).append(violation)
        else:  # added — 仅新增行命中即违规
            for lineno, line in checked:
                if rule["match"].search(line):
                    violation = {
                        "id": rule["id"], "level": rule["level"], "name": rule["name"],
                        "source": rule["source"], "lines": [lineno], "detail": rule["detail"],
                    }
                    (errors if rule["level"] == "error" else warnings).append(violation)

    return {
        "status": "OK" if not errors else "VIOLATIONS",
        "errors": errors,
        "warnings": warnings,
        "checked": ext,
    }
