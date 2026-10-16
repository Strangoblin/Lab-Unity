#!/usr/bin/env python3
"""Architecture source-scope controls; all fixtures live in isolated temp roots."""

from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("architecture_verify", Path(__file__).with_name("verify.py"))
assert SPEC is not None and SPEC.loader is not None
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class ScanScopeTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="lab-architecture-scope-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.addCleanup(patch.stopall)
        patch.object(VERIFY, "ROOT", self.root).start()
        patch.object(VERIFY, "TEST_ROOT", self.root / ".codex/tests/06_architecture").start()
        self.source_doc = ".agents/knowledge/unity/README.md"
        self.source_gate = ".mcp/validation/source.py"
        self.write(self.source_doc, "[bad](missing.md)\n/Users/example/source\n")
        self.write(self.source_gate, "old_root = '.claude'\n")
        self.write(".agents/skills/shared/SKILL.md", "shared current\n")
        self.write(".claude/skills/shared/SKILL.md", "shared legacy drift\n")
        self.write(".codex/tmp/README.md", "Legacy workspace routing\n")
        self.generated = [
            ".codex/tmp/isolated/.agents/skills/copy/SKILL.md",
            ".codex/tmp/isolated/.mcp/validation/copy.py",
            ".mcp/tmp/isolated/.agents/knowledge/copy.md",
            ".mcp/tmp/isolated/.mcp/validation/copy.py",
            ".codex/tests/01_smoke/output/.agents/README.md",
            ".codex/tests/01_smoke/run.log",
            ".mcp/.pytest_cache/README.md",
            ".mcp/.venv/package/README.md",
            ".agents/scripts/__pycache__/copied.py",
            ".agents/skills/shared/.pytest_cache/copied.md",
            ".agents/skills/shared/node_modules/copied.py",
            ".codex/tests/06_architecture/fixture.md",
            "tmp/sessions/2026-10-10-scope/.agents/README.md",
        ]
        for path in self.generated:
            self.write(path, "[bad](missing.md)\nroot = '.claude'\n/Users/example/generated\n")

    def write(self, relative: str, content: str) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def relative(self, paths) -> set[str]:
        return {VERIFY.rel(path) for path in paths}

    def test_generated_fixtures_are_excluded_but_legacy_route_is_read(self) -> None:
        files = self.relative(VERIFY.readable_files())
        self.assertIn(".codex/tmp/README.md", files)
        self.assertIn(self.source_doc, files)
        self.assertIn(self.source_gate, files)
        self.assertFalse(files.intersection(self.generated))

    def test_real_architecture_bad_link_and_hardcode_remain_detectable(self) -> None:
        texts = VERIFY.text_map()
        self.assertEqual(
            VERIFY.broken_markdown_links(texts, ".agents"),
            [{"file": self.source_doc, "line": 1, "target": "missing.md"}],
        )
        hardcodes = VERIFY.mcp_hardcodes(texts)
        self.assertEqual([hit["file"] for hit in hardcodes], [self.source_gate])
        absolute, _ = VERIFY.absolute_and_case_hits(texts)
        self.assertEqual([hit["file"] for hit in absolute], [self.source_doc])

    def test_nested_layer_names_do_not_change_owner(self) -> None:
        nested_doc = ".codex/reference/.agents/README.md"
        nested_gate = ".codex/reference/.mcp/source.py"
        self.write(nested_doc, "[bad](missing.md)\n")
        self.write(nested_gate, "root = '.claude'\n")
        texts = VERIFY.text_map()
        self.assertEqual(VERIFY.architecture_root(self.root / nested_doc), ".codex")
        self.assertEqual(VERIFY.architecture_root(self.root / nested_gate), ".codex")
        self.assertEqual([hit["file"] for hit in VERIFY.broken_markdown_links(texts, ".agents")], [self.source_doc])
        self.assertEqual([hit["file"] for hit in VERIFY.mcp_hardcodes(texts)], [self.source_gate])
        self.assertIn(nested_doc, [hit["file"] for hit in VERIFY.broken_markdown_links(texts, ".codex")])

    def test_source_directory_named_output_is_not_blanket_excluded(self) -> None:
        source = ".agents/knowledge/output/README.md"
        self.write(source, "[bad](missing.md)\n")
        texts = VERIFY.text_map()
        self.assertIn(self.root / source, texts)
        self.assertIn(source, [hit["file"] for hit in VERIFY.broken_markdown_links(texts, ".agents")])

    def test_real_legacy_skill_drift_remains_detectable(self) -> None:
        self.write(".agents/skills/shared/.pytest_cache/copied.md", "shared current\n")
        self.write(".claude/skills/shared/.pytest_cache/copied.md", "legacy cache drift\n")
        self.assertEqual(VERIFY.skill_drift(VERIFY.text_map()), ["shared/SKILL.md"])

    def test_real_skill_symlink_is_rejected_even_without_drift(self) -> None:
        legacy = self.root / ".claude/skills/shared/SKILL.md"
        legacy.unlink()
        legacy.symlink_to("../../../.agents/skills/shared/SKILL.md")
        self.assertEqual(VERIFY.skill_drift(VERIFY.text_map()), [])
        self.assertTrue(any(".claude/skills/shared/SKILL.md" in error
                            for error in VERIFY.architecture_symlink_errors()))

    def test_inventory_excludes_tracked_generated_fixtures(self) -> None:
        candidates = [self.source_doc, ".codex/tmp/README.md", *self.generated]
        with patch.object(VERIFY, "tracked", side_effect=lambda prefix: [p for p in candidates if p.startswith(prefix)]):
            inventory = VERIFY.make_inventory(VERIFY.text_map())
        self.assertEqual(
            {entry["current_path"] for entry in inventory},
            {self.source_doc, ".codex/tmp/README.md"},
        )

    def test_repository_prefixes_are_not_matched_by_substring(self) -> None:
        for relative in ("copies/.agents/README.md", ".codexOther/reference.md", "tmp/.mcp/source.py"):
            self.assertIsNone(VERIFY.architecture_root(self.root / relative))
            self.assertFalse(VERIFY.in_source_scope(self.root / relative))


if __name__ == "__main__":
    unittest.main(verbosity=2)
