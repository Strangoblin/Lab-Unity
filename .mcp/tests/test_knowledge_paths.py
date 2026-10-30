"""Canonical knowledge IDs, script locations and exact tool-resource authorization.

v4 删除了 ``validate_path``（写入边界现在由 ``zones.py`` 按分区判定），但**知识 ID 解析**
与**脚本库精确授权**两件事都还在用，且都是防幻觉的关键：前者保证 profile 声明的必读项
指向真实文件，后者保证「用了哪个脚本」不是编的。
"""

from __future__ import annotations

import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[2]
MCP_ROOT = ROOT / ".mcp"
sys.path.insert(0, str(MCP_ROOT / "lib"))

import profiles  # noqa: E402
from validation.knowledge_paths import (  # noqa: E402
    AmbiguousKnowledgePath,
    MissingKnowledgePath,
    default_kb_roots,
    find_project_root,
    resolve_entry,
)
from validation.script_library import SCRIPTS_DIR, script_exists, validate_decision  # noqa: E402


def test_project_root_and_stable_ids() -> None:
    assert find_project_root(MCP_ROOT) == ROOT
    roots = default_kb_roots(find_project_root(MCP_ROOT))
    resolution = resolve_entry(
        "unity\\standard\\shader\\shader-structure.md",
        ROOT,
        roots,
    )
    assert resolution.knowledge_id == "unity/standard/shader/shader-structure.md"
    assert resolution.canonical_relative_path == (
        ".agents/knowledge/unity/standard/shader/shader-structure.md"
    )
    assert resolution.resolved_path.is_file()


def test_every_profile_knowledge_id_resolves() -> None:
    """profile 声明了必读却没指向真实文件 = 编造。这条必须由机器守着。"""
    roots = default_kb_roots(find_project_root(MCP_ROOT))
    for name, spec in profiles.PROFILES.items():
        for entry in spec["required"] + spec["recommended"]:
            try:
                resolution = resolve_entry(entry, ROOT, roots)
            except (MissingKnowledgePath, AmbiguousKnowledgePath) as error:
                raise AssertionError(f"{name}: {entry} -> {type(error).__name__}: {error}")
            assert resolution.resolved_path.is_file(), f"{name}: {entry}"


def test_basename_ambiguity_and_missing_are_explicit() -> None:
    with TemporaryDirectory() as temp:
        root = Path(temp)
        (root / ".agents").mkdir()
        (root / "AGENTS.md").write_text("# test\n", encoding="utf-8")
        left = root / "left"
        right = root / "right"
        left.mkdir()
        right.mkdir()
        (left / "same.md").write_text("left\n", encoding="utf-8")
        (right / "same.md").write_text("right\n", encoding="utf-8")
        roots = [("left", left), ("right", right)]

        try:
            resolve_entry("same.md", root, roots)
        except AmbiguousKnowledgePath:
            pass
        else:
            raise AssertionError("duplicate basenames must be rejected")

        try:
            resolve_entry("missing.md", root, roots)
        except MissingKnowledgePath:
            pass
        else:
            raise AssertionError("missing knowledge paths must be rejected")


def test_roslyn_scripts_resolve_only_from_the_canonical_skill() -> None:
    # Path 比较而非字符串子串：Windows 上 os.path.join 产出反斜杠。
    assert Path(SCRIPTS_DIR) == ROOT / ".agents" / "skills" / "unity-editor" / "scripts" / "roslyn"
    assert script_exists("scene-query.cs")


def test_script_decision_rejects_fabricated_names() -> None:
    assert validate_decision("USE camera-offscreen-capture.cs")["status"] == "OK"
    assert validate_decision("USE deliberately-missing.cs")["status"] == "DENIED"


def test_profile_obligations_cover_required_knowledge() -> None:
    """声明了必读，就必须把 knowledge 列进完成义务 —— 否则必读是纯装饰。"""
    for name, spec in profiles.PROFILES.items():
        if spec["required"]:
            assert "knowledge" in spec["obligations"], (
                f"{name} 有必读项却没把 knowledge 列入完成义务")


if __name__ == "__main__":
    test_project_root_and_stable_ids()
    test_every_profile_knowledge_id_resolves()
    test_basename_ambiguity_and_missing_are_explicit()
    test_roslyn_scripts_resolve_only_from_the_canonical_skill()
    test_script_decision_rejects_fabricated_names()
    test_profile_obligations_cover_required_knowledge()
    print("PASS: knowledge-path, script-library and profile contracts")
