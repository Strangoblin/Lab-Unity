"""Isolation runner regressions; all writes stay inside test-owned temporary roots."""
from __future__ import annotations

from contextlib import redirect_stdout, redirect_stderr
import io
import json
from pathlib import Path
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

import run_isolated as runner


class IsolatedRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = (Path(self.temporary.name) / "project").resolve()
        self.root.mkdir()
        for relative in runner.FIXTURE_FILES:
            self.put(relative, "fixture\n")
        for relative in runner.FIXTURE_TREES:
            self.put(relative + "/fixture.md", "fixture\n")
        for name in runner.SUITES:
            self.put(".mcp/tests/" + name, "def test_fixture():\n    pass\n")
        self.put(".mcp/policy.json", "{}\n")
        for name in runner.AUDIT_NAMES:
            self.put(".mcp/" + name, "production sentinel\n")
        self.task = self.root / "tmp/sessions/test-task"
        self.task.mkdir(parents=True)
        self.sentinel = self.task / "fixtures" / "preexisting"
        self.sentinel.mkdir(parents=True)
        (self.sentinel / "keep.txt").write_text("keep\n")

    def put(self, relative, content):
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
        return target

    def fake_command(self, command, fixture, env, log, timeout):
        self.assertEqual(env["PROJECT_ROOT"], str(fixture))
        self.assertTrue(Path(env["TMPDIR"]).is_relative_to(fixture))
        self.assertNotIn("MCP_FRICTION_FILE", env)
        log.write_text("stub output\n")
        return 0

    def run_stub(self, fail=False, retain=False):
        def command(*args):
            code = self.fake_command(*args)
            return 7 if fail and str(args[0][-1]).endswith("test_recipes.py") else code

        with patch.object(runner, "run_command", side_effect=command), redirect_stdout(io.StringIO()):
            code, report = runner.run_isolated(self.root, self.task, Path(sys.executable), 30, retain)
        return code, json.loads(report.read_text()), report

    def test_copy_excludes_production_and_caches(self):
        for relative in (".mcp/.venv/secret.py", ".mcp/__pycache__/old.py",
                         ".mcp/.pytest_cache/cache.json", ".mcp/state.json.tmp"):
            self.put(relative, "must not copy\n")
        fixture = self.task / "copy-fixture"
        fixture.mkdir()
        copied = runner.copy_fixture(self.root, fixture)
        self.assertIn(".mcp/policy.json", copied)
        for name in runner.AUDIT_NAMES:
            self.assertFalse((fixture / ".mcp" / name).exists())
        self.assertFalse((fixture / ".mcp/.venv").exists())
        self.assertFalse((fixture / ".mcp/__pycache__").exists())
        self.assertFalse((fixture / ".mcp/.pytest_cache").exists())
        self.assertFalse((fixture / ".mcp/state.json.tmp").exists())

    def test_success_cleans_only_owned_fixture_and_keeps_evidence(self):
        before = runner.fingerprints(self.root)
        code, report, path = self.run_stub()
        self.assertEqual(code, 0)
        self.assertEqual(report["status"], "PASS")
        self.assertEqual(before, runner.fingerprints(self.root))
        self.assertTrue(report["fixture_cleaned"])
        self.assertEqual(Path(report["fixture"]).parent, self.task / "fixtures")
        self.assertFalse(Path(report["fixture"]).exists())
        self.assertTrue(self.sentinel.exists())
        self.assertTrue(path.is_file())
        self.assertTrue(all(Path(item["log"]).is_file() for item in report["results"]))

    def test_failure_exit_and_explicit_retention(self):
        code, report, _ = self.run_stub(fail=True, retain=True)
        self.assertEqual(code, 1)
        self.assertEqual(report["results"][0]["exit_code"], 7)
        self.assertTrue(report["fixture_retained"])
        self.assertTrue(Path(report["fixture"]).is_dir())
        self.assertTrue(report["real_gate_state_and_audit_unchanged"])

    def test_failure_cleans_fixture_by_default(self):
        code, report, _ = self.run_stub(fail=True)
        self.assertEqual(code, 1)
        self.assertTrue(report["fixture_cleaned"])
        self.assertFalse(Path(report["fixture"]).exists())

    def test_live_state_change_is_detected(self):
        def command(*args):
            self.put(".mcp/state.json", "concurrent external update\n")
            return self.fake_command(*args)

        with patch.object(runner, "run_command", side_effect=command), redirect_stdout(io.StringIO()):
            code, path = runner.run_isolated(self.root, self.task, Path(sys.executable), 30)
        report = json.loads(path.read_text())
        self.assertEqual(code, 1)
        self.assertFalse(report["real_gate_state_and_audit_unchanged"])
        self.assertEqual((self.root / ".mcp/state.json").read_text(), "concurrent external update\n")

    def test_escaping_input_symlink_is_rejected(self):
        outside = Path(self.temporary.name) / "external.py"
        outside.write_text("outside\n")
        (self.root / ".mcp/escape.py").symlink_to(outside)
        fixture = self.task / "copy-fixture"
        fixture.mkdir()
        with self.assertRaisesRegex(ValueError, "escapes project"):
            runner.copy_fixture(self.root, fixture)

    def test_missing_interpreter_reports_failure_without_installing(self):
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            code, path = runner.run_isolated(self.root, self.task, self.root / "missing-python", 30)
        report = json.loads(path.read_text())
        self.assertEqual(code, 1)
        self.assertIn("no automatic install", report["error"])
        self.assertNotIn("fixture", report)
        self.assertTrue(report["real_gate_state_and_audit_unchanged"])

    def test_task_directory_cannot_escape_sessions(self):
        with redirect_stderr(io.StringIO()):
            code = runner.main(["--project-root", str(self.root), "--task-dir", ".mcp"])
        self.assertEqual(code, 2)
        self.assertFalse((self.root / ".mcp/output").exists())

    def test_fixture_directory_cannot_escape_task(self):
        task = self.root / "tmp/sessions/symlink-task"
        task.mkdir()
        outside = Path(self.temporary.name) / "outside-fixtures"
        outside.mkdir()
        (task / "fixtures").symlink_to(outside, target_is_directory=True)
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            code, path = runner.run_isolated(self.root, task, Path(sys.executable), 30)
        report = json.loads(path.read_text())
        self.assertEqual(code, 1)
        self.assertIn("inside this task", report["error"])
        self.assertEqual(list(outside.iterdir()), [])
        self.assertTrue(report["real_gate_state_and_audit_unchanged"])


if __name__ == "__main__":
    unittest.main()
