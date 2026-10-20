"""Canonical knowledge, script locations and exact tool-resource authorization."""

from __future__ import annotations

import sys
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
MCP_ROOT = ROOT / ".mcp"
sys.path.insert(0, str(MCP_ROOT))

from gates import g_knowledge  # noqa: E402
from validation.knowledge_paths import (  # noqa: E402
    AmbiguousKnowledgePath,
    MissingKnowledgePath,
    find_project_root,
    resolve_entry,
)
from validation.project_paths import validate_path  # noqa: E402
from validation import project_paths  # noqa: E402
from validation.script_library import SCRIPTS_DIR, script_exists  # noqa: E402


def test_project_root_and_stable_ids() -> None:
    assert find_project_root(MCP_ROOT) == ROOT
    resolution = resolve_entry(
        "unity\\standard\\shader\\shader-structure.md",
        ROOT,
        g_knowledge.KB_ROOTS,
    )
    assert resolution.knowledge_id == "unity/standard/shader/shader-structure.md"
    assert resolution.canonical_relative_path == (
        ".agents/knowledge/unity/standard/shader/shader-structure.md"
    )
    assert resolution.resolved_path.is_file()


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


def test_g_knowledge_reports_canonical_resolution() -> None:
    result = g_knowledge.check(
        {
            "status": "COMPLETE",
            "loaded_files": (
                "unity/standard/shader/shader-structure.md, "
                "unity/standard/script/script-structure.md"
            ),
        }
    )
    assert result["status"] == "OK"
    assert all(item["knowledge_id"].startswith("unity/") for item in result["resolved_details"])


def test_roslyn_paths_use_only_canonical_skill_root() -> None:
    # Path 比较而非字符串子串：Windows 上 os.path.join 产出反斜杠，
    # 正斜杠子串断言在此平台恒失败（曾掩蔽本文件内的 Assets/Mine 作用域用例）。
    assert Path(SCRIPTS_DIR) == ROOT / ".agents" / "skills" / "unity-editor" / "scripts" / "roslyn"
    assert script_exists("scene-query.cs")
    assert validate_path(".agents/skills/unity-editor/scripts/roslyn/scene-query.cs")["status"] == "OK"
    assert validate_path(".agents/agents/unity-developer/scripts/roslyn/scene-query.cs")["status"] == "DENIED"
    legacy_path = "." + "claude/agents/unity-developer/scripts/roslyn/scene-query.cs"
    assert validate_path(legacy_path)["status"] == "DENIED"


def test_mine_write_scope() -> None:
    for path in (
        "Assets/Mine/Special/HLSL/PBRFunction.hlsl",
        "Assets/Mine/Shaders/Render/Test.shader",
        "Assets/Mine/Scripts/Test.cs",
        "Assets/Mine/Textures/Test.png",
        str(ROOT / "Assets/Mine/Special/HLSL/PBRFunction.hlsl"),
        "tmp/test.txt",
    ):
        assert validate_path(path)["status"] == "OK", path
    for path in (
        "Assets/MineOther/test.hlsl",
        "Assets/Mine/../Other/test.hlsl",
        "Assets/Other/test.hlsl",
        "tmpOther/test.txt",
        ".agents/agents/meta-developer/AGENT.md",
    ):
        assert validate_path(path)["status"] == "DENIED", path
    with TemporaryDirectory(dir=ROOT / "tmp") as allowed, TemporaryDirectory() as outside:
        link = Path(allowed) / "outside"
        link.symlink_to(outside, target_is_directory=True)
        assert validate_path(str(link / "test.hlsl"))["status"] == "DENIED"


def test_camera_authorization_is_exact_without_legacy_aliases() -> None:
    with TemporaryDirectory() as temp:
        root = Path(temp).resolve()
        legacy = root / ".agents/agents/unity-developer/scripts/roslyn"
        canonical = root / project_paths.CANONICAL_ROSLYN_ROOT
        camera = root / ".agents/skills/unity-render-debug/scripts/camera-offscreen-capture.cs"
        legacy.mkdir(parents=True)
        canonical.mkdir(parents=True)
        camera.parent.mkdir(parents=True)
        camera.write_text("// camera\n")
        scene = canonical / "scene-query.cs"
        scene.write_text("// scene\n")
        scene_alias = legacy / scene.name
        scene_alias.symlink_to(scene)
        camera_alias = legacy / camera.name
        camera_alias.symlink_to(camera)
        unknown = legacy / "unknown.cs"
        unknown.symlink_to(camera)
        with patch.object(project_paths, "PROJECT_ROOT", str(root)):
            assert validate_path(str(scene))["status"] == "OK"
            assert validate_path(str(scene_alias))["status"] == "DENIED"
            assert validate_path(str(camera_alias))["status"] == "DENIED"
            assert validate_path(str(unknown))["status"] == "DENIED"
            assert validate_path(str(camera))["status"] == "OK"
            assert validate_path(str(camera.parent))["status"] == "DENIED"
            assert validate_path(str(camera.parent / "neighbor.cs"))["status"] == "DENIED"
            assert validate_path(str(camera / "child.cs"))["status"] == "DENIED"
            camera.unlink()
            camera.symlink_to(scene)
            assert validate_path(str(camera))["status"] == "DENIED"
            camera.unlink()
            camera.mkdir()
            assert validate_path(str(camera))["status"] == "DENIED"


if __name__ == "__main__":
    test_project_root_and_stable_ids()
    test_basename_ambiguity_and_missing_are_explicit()
    test_g_knowledge_reports_canonical_resolution()
    test_roslyn_paths_use_only_canonical_skill_root()
    test_mine_write_scope()
    test_camera_authorization_is_exact_without_legacy_aliases()
    print("PASS: knowledge-path and Roslyn path contracts")
