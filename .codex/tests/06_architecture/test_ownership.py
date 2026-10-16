#!/usr/bin/env python3
"""Role/library, plain-routing and historical-source controls in disposable roots."""

from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("ownership_verify", Path(__file__).with_name("verify.py"))
assert SPEC is not None and SPEC.loader is not None
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class OwnershipTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="lab-architecture-ownership-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        patched = patch.object(VERIFY, "ROOT", self.root)
        patched.start()
        self.addCleanup(patched.stop)
        self.write(".agents/agents/README.md", "# Shared role inventory\n")
        self.write(".agents/agents/unity-developer/AGENT.md", "# Brain\n")
        self.target = self.write(".agents/skills/unity-editor/scripts/roslyn/scene-query.cs", "// tool\n")
        self.routes = [".claude/CLAUDE.md", ".claude/agents/README.md",
                       ".codex/AGENTS.md", ".codex/agents/README.md"]
        self.write(".codex/tests/06_architecture/platform-whitelist.json", json.dumps({"platform_routes": self.routes}))
        for relative in self.routes:
            path = self.root / relative
            target = os.path.relpath(self.root / ".agents/agents/README.md", path.parent)
            self.write(relative, f"[Role inventory]({target})\n")

    def write(self, relative: str, content: str = "fixture\n") -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def link(self, relative: str, target: Path | None = None) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        destination = target or self.target
        path.symlink_to(os.path.relpath(destination, path.parent))
        return path

    def test_clean_definitions_and_separate_libraries_pass(self) -> None:
        self.write(".agents/knowledge/unity/guide.md")
        self.write(".agents/memory/unity/MEMORY.md")
        self.assertEqual(VERIFY.agent_ownership_errors(), [])
        self.assertEqual(VERIFY.architecture_symlink_errors(), [])
        self.assertEqual(VERIFY.platform_route_errors(), [])

    def test_real_script_in_role_fails(self) -> None:
        source = ".agents/agents/unity-developer/scripts/query.cs"
        self.write(source)
        self.assertTrue(any(source in error for error in VERIFY.agent_ownership_errors()))

    def test_role_cannot_own_fact_memory_or_compatibility_indexes(self) -> None:
        for folder in ("references", "memory", "templates", "cli", "scripts"):
            relative = f".agents/agents/unity-developer/{folder}/README.md"
            self.write(relative)
            self.assertTrue(any(folder in error for error in VERIFY.agent_ownership_errors()))

    def test_any_real_architecture_layer_symlink_fails(self) -> None:
        for relative in (".agents/knowledge/unity/link.cs", ".claude/agents/link.cs",
                         ".codex/agents/link.cs", ".mcp/validation/link.cs"):
            self.link(relative)
            self.assertTrue(any(relative in error for error in VERIFY.architecture_symlink_errors()))

    def test_broken_architecture_symlink_fails(self) -> None:
        relative = ".agents/knowledge/unity/broken.md"
        self.link(relative, self.root / "missing.md")
        self.assertTrue(any(relative in error for error in VERIFY.architecture_symlink_errors()))

    def test_directory_symlink_fails(self) -> None:
        relative = ".codex/skills"
        self.link(relative, self.root / ".agents/skills")
        self.assertTrue(any(relative in error for error in VERIFY.architecture_symlink_errors()))

    def test_architecture_root_symlink_is_rejected_without_traversal(self) -> None:
        self.link(".mcp", self.root / ".agents/skills")
        self.assertTrue(any(".mcp" in error for error in VERIFY.architecture_symlink_errors()))

    def test_checker_source_symlinks_are_not_hidden_by_negative_text_exemption(self) -> None:
        relative = ".codex/tests/06_architecture/module-link.py"
        self.link(relative)
        with patch.object(VERIFY, "TEST_ROOT", self.root / ".codex/tests/06_architecture"):
            self.assertTrue(any(relative in error for error in VERIFY.architecture_symlink_errors()))

    def test_plain_platform_routes_pass_without_discovery_links(self) -> None:
        self.assertEqual(VERIFY.platform_route_errors(), [])
        self.assertEqual(VERIFY.broken_markdown_links(VERIFY.text_map()), [])

    def test_platform_route_inventory_must_exist(self) -> None:
        (self.root / ".agents/agents/README.md").unlink()
        self.assertTrue(VERIFY.platform_route_errors())

    def test_route_symlink_is_rejected_even_when_content_is_correct(self) -> None:
        relative = ".claude/agents/README.md"
        (self.root / relative).unlink()
        self.link(relative, self.root / ".agents/agents/README.md")
        self.assertTrue(any(relative in error for error in VERIFY.platform_route_errors()))

    def test_duplicate_platform_skill_body_is_rejected(self) -> None:
        self.write(".claude/skills/copied/SKILL.md")
        self.assertTrue(any("copied" in error for error in VERIFY.platform_route_errors()))

    def test_registered_runtime_config_can_remain_a_thin_route(self) -> None:
        self.write(".codex/agents/unity-developer.toml",
                   'name = "unity-developer"\n'
                   'developer_instructions = "Read .agents/agents/README.md and .agents/agents/unity-developer/AGENT.md"\n')
        self.assertEqual(VERIFY.platform_route_errors(), [])

    def test_runtime_config_cannot_copy_workflow_or_add_an_unregistered_role(self) -> None:
        self.write(".codex/agents/unity-developer.toml", "workflow body\n")
        self.write(".codex/agents/another-role.toml")
        errors = VERIFY.platform_route_errors()
        self.assertTrue(any("thin inventory route" in error for error in errors))
        self.assertTrue(any("another-role.toml" in error for error in errors))

    def test_active_knowledge_bad_link_still_fails(self) -> None:
        relative = ".agents/knowledge/unity/2026-10-14-historical-guide.md"
        path = self.write(relative, "Historical wording is not an exemption. [bad](missing.md)\n")
        self.assertEqual(VERIFY.broken_markdown_links({path: path.read_text()}), [
            {"file": relative, "line": 1, "target": "missing.md"},
        ])

    def test_live_memory_index_bad_link_still_fails(self) -> None:
        relative = ".agents/memory/unity/MEMORY.md"
        path = self.write(relative, "[bad](missing.md)\n")
        self.assertEqual(VERIFY.broken_markdown_links({path: path.read_text()}), [
            {"file": relative, "line": 1, "target": "missing.md"},
        ])

    def test_dated_memory_links_use_original_directory_context_without_byte_changes(self) -> None:
        relative = ".agents/memory/unity/2026-10-14-record.md"
        path = self.write(relative, "[guide](../references/guide.md)\n")
        guide = self.write(".agents/knowledge/unity/guide.md")
        before = path.read_bytes()
        origin = VERIFY.historical_content_origin(path)
        self.assertEqual(VERIFY.relocated_historical_path(origin.parent / "../references/guide.md"), guide)
        self.assertEqual(VERIFY.broken_markdown_links({path: path.read_text()}), [])
        self.assertEqual(path.read_bytes(), before)

    def test_dated_archive_can_keep_references_to_retired_sources(self) -> None:
        path = self.write(".agents/memory/meta/2026-10-14-record.md", "[retired](retired-source.md)\n")
        before = path.read_bytes()
        self.assertEqual(VERIFY.broken_markdown_links({path: path.read_text()}), [])
        self.assertEqual(path.read_bytes(), before)

    def test_history_exemption_is_limited_to_dated_memory_paths(self) -> None:
        evidence = self.write(".agents/memory/unity/evidence/2026-10-14-task/report.json")
        active = self.write(".agents/knowledge/unity/2026-10-14-history.json")
        self.assertTrue(VERIFY._skip_absolute_case_scan(evidence))
        self.assertFalse(VERIFY._skip_absolute_case_scan(active))

    def test_archived_evidence_and_dependency_links_are_not_active_sources(self) -> None:
        self.link(".agents/memory/unity/evidence/2026-10-14-task/link.cs")
        self.link(".mcp/.venv/bin/python")
        self.assertEqual(VERIFY.architecture_symlink_errors(), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
