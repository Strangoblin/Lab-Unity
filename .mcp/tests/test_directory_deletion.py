#!/usr/bin/env python3
"""Directory deletion contracts. Run through tests/run_isolated.py, never live state."""
from __future__ import annotations

import asyncio
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[2]
MCP = ROOT / ".mcp"


def require_isolated_project() -> None:
    """Only the runner's disposable project owns the Git/state this suite changes."""
    configured = os.environ.get("PROJECT_ROOT", "")
    temporary = Path(os.environ.get("TMPDIR", "/nonexistent")).resolve()
    if (not configured or Path(configured).resolve() != ROOT
            or not ROOT.name.startswith("mcp-isolated-")
            or not temporary.is_relative_to(ROOT)
            or not (ROOT / "empty-git-hooks").is_dir()
            or not (ROOT / ".git").is_dir()):
        raise RuntimeError("Use .mcp/tests/run_isolated.py; this suite never resets production state.")


require_isolated_project()
sys.path.insert(0, str(MCP))
POLICY = Path(os.environ["TMPDIR"]) / "directory-deletion-strict-policy.json"
POLICY.write_text(json.dumps({
    "checks": {
        "ceremony.recipe_gate": {"level": "block"},
        "ceremony.knowledge_high_priority": {"level": "block"},
        "ceremony.knowledge_resolvable": {"level": "block"},
        "scope.workspace": {"level": "block"},
        "deletion.recoverability": {"level": "block"},
        "deletion.guid-referenced": {"level": "block"},
    },
    "workspace": {"required": True, "applies_to": ["Assets/Mine/"]},
}), encoding="utf-8")
os.environ["MCP_POLICY_FILE"] = str(POLICY)
os.environ["MCP_FRICTION_FILE"] = str(Path(os.environ["TMPDIR"]) / "directory-friction.jsonl")

from validation import deletion, judgement, policy  # noqa: E402

policy.reload()
READY_GATE = {"status": "OK", "recipe": "Quick", "passed": ["g_entry", "g_knowledge"]}
HIGH_PRIO = "unity/standard/shader/shader-structure.md, unity/standard/script/script-structure.md"


async def mcp_call(tool: str, **arguments) -> dict:
    from server import server
    result = await server.call_tool(tool, arguments)
    for block in result.content:
        if hasattr(block, "text"):
            return json.loads(block.text)
    raise AssertionError(f"No JSON result from {tool}: {result}")


def call(tool: str, **arguments) -> dict:
    return asyncio.run(mcp_call(tool, **arguments))


def snapshot(path: Path) -> dict:
    """Content and entry names, without following a symlink."""
    result = {}

    def visit(current: Path):
        relative = str(current.relative_to(path.parent))
        if current.is_symlink():
            result[relative] = ("symlink", os.readlink(current))
        elif current.is_dir():
            result[relative] = ("directory",)
            for child in sorted(current.iterdir()):
                visit(child)
        elif current.is_file():
            result[relative] = ("file", hashlib.sha256(current.read_bytes()).hexdigest())
        elif current.exists():
            result[relative] = ("special", current.lstat().st_mode)

    visit(path)
    return result


class DirectoryDeletionTests(unittest.TestCase):
    def setUp(self):
        self.case_id = uuid4().hex[:12]
        self.base = ROOT / "tmp" / "directory-deletion" / self.case_id
        self.base.mkdir(parents=True)
        self.assets = ROOT / "Assets/Mine/Shaders/DirectoryDeletion" / self.case_id
        self.assets.mkdir(parents=True)
        self.workspace = {"primary": self.rel(self.assets), "roots": [self.rel(self.assets)]}

    def rel(self, path: Path) -> str:
        return path.relative_to(ROOT).as_posix()

    def put(self, path: Path, content: str = "fixture\n") -> Path:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def git(self, *arguments: str) -> subprocess.CompletedProcess:
        result = subprocess.run(["git", *arguments], cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        return result

    def commit(self, *paths: Path):
        self.git("add", "--", *(self.rel(path) for path in paths))
        self.git("-c", "user.name=Gate Test", "-c", "user.email=gate-test@example.invalid",
                 "commit", "-q", "-m", "isolated directory fixture")

    def delete(self, *paths: Path | str, **options) -> dict:
        return judgement.delete(
            [self.rel(path) if isinstance(path, Path) else path for path in paths],
            "owned isolated directory fixture", channel="test", tool="directory-contract",
            gate=READY_GATE, workspace=options.pop("workspace", self.workspace), **options)

    def assert_denied_unchanged(self, paths: list[Path], result: dict, before: list[dict]):
        self.assertEqual(result["status"], "DENIED", result)
        self.assertEqual([snapshot(path) for path in paths], before)
        self.assertFalse(result.get("deleted"), result)

    def plan(self, path: Path) -> dict:
        planned = deletion.plan(str(ROOT), [(self.rel(path), str(path))], recursive=True,
                                protected_roots=["tmp", "Assets/Mine"])
        self.assertEqual(planned["blocked"], [], planned)
        return planned

    def test_empty_directory_default_and_missing_repeat(self):
        target = self.base / "empty"
        target.mkdir()
        result = self.delete(target)
        self.assertEqual(result["status"], "OK", result)
        self.assertEqual([item["path"] for item in result["deleted"]], [self.rel(target)])
        self.assertEqual(result["count"], 1)
        self.assertFalse(target.exists())
        repeated = self.delete(target)
        self.assertEqual(repeated["status"], "OK", repeated)
        self.assertEqual(repeated["count"], 0)

    def test_nonempty_directory_requires_explicit_recursive(self):
        target = self.base / "nonempty"
        self.put(target / "nested/file.txt")
        before = [snapshot(target)]
        result = self.delete(target)
        self.assert_denied_unchanged([target], result, before)
        self.assertTrue(any(item.get("state") == "directory-not-empty"
                            for item in result.get("blocked", [])), result)

    def test_recursive_removes_full_subtree_and_reports_each_entry(self):
        target = self.base / "tree"
        file = self.put(target / "nested/leaf.txt")
        (target / "empty").mkdir()
        expected = {self.rel(path) for path in (target, target / "nested", file, target / "empty")}
        result = self.delete(target, recursive=True)
        self.assertEqual(result["status"], "OK", result)
        self.assertEqual({item["path"] for item in result["deleted"]}, expected)
        self.assertEqual(result["count"], len(expected))
        self.assertFalse(target.exists())

    def test_tracked_clean_nested_files_can_be_restored_by_git(self):
        target = self.base / "clean"
        file = self.put(target / "nested/file.txt", "committed bytes\n")
        self.commit(file)
        result = self.delete(target, recursive=True)
        self.assertEqual(result["status"], "OK", result)
        entry = next(item for item in result["deleted"] if item["path"] == self.rel(file))
        self.assertEqual(entry["state"], "tracked-clean")
        self.assertTrue(entry["recoverable"])
        self.git("restore", "--", self.rel(file))
        self.assertEqual(file.read_text(), "committed bytes\n")

    def test_dirty_descendant_denies_entire_mixed_batch(self):
        target = self.base / "dirty"
        file = self.put(target / "nested/file.txt", "committed\n")
        innocent = self.put(self.base / "keep.txt")
        self.commit(file)
        file.write_text("unique unpublished change\n")
        paths = [target, innocent]
        before = [snapshot(path) for path in paths]
        result = self.delete(innocent, target, recursive=True)
        self.assert_denied_unchanged(paths, result, before)
        self.assertTrue(any(item.get("path") == self.rel(file)
                            and item.get("state") == "tracked-dirty"
                            for item in result.get("blocked", [])), result)

    def test_staged_descendant_is_also_dirty(self):
        target = self.base / "staged"
        file = self.put(target / "file.txt", "committed\n")
        self.commit(file)
        file.write_text("staged unpublished change\n")
        self.git("add", "--", self.rel(file))
        before = [snapshot(target)]
        result = self.delete(target, recursive=True)
        self.assert_denied_unchanged([target], result, before)
        self.assertTrue(any(item.get("path") == self.rel(file)
                            and item.get("state") == "tracked-dirty"
                            for item in result.get("blocked", [])), result)
        self.git("restore", "--staged", "--", self.rel(file))

    def test_git_failure_fails_closed_for_descendants(self):
        target = self.base / "git-failure"
        self.put(target / "file.txt")
        before = [snapshot(target)]
        with patch.object(deletion, "_git", side_effect=OSError("synthetic git failure")):
            result = self.delete(target, recursive=True)
        self.assert_denied_unchanged([target], result, before)
        self.assertTrue(any(item.get("state") == "unknown"
                            for item in result.get("blocked", [])), result)

    def test_guid_reference_inside_batch_is_allowed(self):
        guid = "1" * 32
        target = self.assets / "internal-guid"
        self.put(target / "asset.txt")
        self.put(target / "asset.txt.meta", f"fileFormatVersion: 2\nguid: {guid}\n")
        self.put(target / "holder.prefab", f"material: {{fileID: 0, guid: {guid}}}\n")
        result = self.delete(target, recursive=True)
        self.assertEqual(result["status"], "OK", result)
        self.assertFalse(target.exists())

    def test_guid_reference_in_another_requested_subtree_is_allowed(self):
        guid = "2" * 32
        first, second = self.assets / "first", self.assets / "second"
        self.put(first / "asset.txt.meta", f"guid: {guid}\n")
        self.put(second / "holder.prefab", f"guid: {guid}\n")
        result = self.delete(first, second, recursive=True)
        self.assertEqual(result["status"], "OK", result)
        self.assertFalse(first.exists())
        self.assertFalse(second.exists())

    def test_external_guid_reference_denies_all_subtrees(self):
        guid = "3" * 32
        target = self.assets / "external-guid"
        meta = self.put(target / "asset.txt.meta", f"guid: {guid}\n")
        external = self.put(self.assets / "keep.prefab", f"guid: {guid}\n")
        innocent = self.put(self.base / "keep.txt")
        paths = [target, external, innocent]
        before = [snapshot(path) for path in paths]
        result = self.delete(innocent, target, recursive=True)
        self.assert_denied_unchanged(paths, result, before)
        blocked = next(item for item in result["blocked"] if item.get("state") == "guid-referenced")
        self.assertEqual(blocked["path"], self.rel(meta))
        self.assertIn(self.rel(external), blocked["referenced_by"])

    def test_folder_meta_not_implicitly_deleted_and_warning_is_preserved(self):
        target = self.assets / "folder-meta"
        target.mkdir()
        meta = self.put(Path(str(target) + ".meta"), "guid: " + "4" * 32 + "\n")
        result = self.delete(target)
        self.assertEqual(result["status"], "OK", result)
        self.assertTrue(meta.exists())
        self.assertTrue(any(item.get("id") == "orphan-meta"
                            for item in result.get("warnings", [])), result)

    def test_workspace_cannot_be_widened_by_recursive_parent(self):
        target = self.assets / "scope"
        child = self.put(target / "permitted/child.txt")
        self.put(target / "outside.txt")
        scope = {"primary": self.rel(child.parent), "roots": [self.rel(child.parent)]}
        before = [snapshot(target)]
        result = self.delete(target, recursive=True, workspace=scope)
        self.assert_denied_unchanged([target], result, before)
        self.assertEqual(result["error"], "WORKSPACE_VIOLATION", result)

    def test_protected_scope_roots_cannot_be_deleted(self):
        for relative in ("tmp", "Assets/Mine", ".agents/skills/unity-editor/scripts/roslyn"):
            with self.subTest(root=relative):
                result = self.delete(relative, recursive=True,
                                     workspace={"primary": "Assets/Mine", "roots": ["Assets/Mine"]})
                self.assertEqual(result["status"], "DENIED", result)
                self.assertTrue((ROOT / relative).is_dir())
                self.assertTrue(any(item.get("state") == "protected-root"
                                    for item in result.get("blocked", [])), result)

    def test_escaping_descendant_symlink_denies_without_following(self):
        target = self.base / "escape"
        target.mkdir()
        external = self.put(ROOT / "Assets" / ("outside-" + self.case_id + ".txt"), "external sentinel\n")
        (target / "escape-link").symlink_to(external)
        paths = [target, external]
        before = [snapshot(path) for path in paths]
        result = self.delete(target, recursive=True)
        self.assert_denied_unchanged(paths, result, before)

    def test_internal_directory_symlink_is_not_traversed(self):
        target = self.base / "internal-symlink"
        target.mkdir()
        preserved = self.base / "preserved"
        self.put(preserved / "sentinel.txt", "must not be followed\n")
        (target / "linked-directory").symlink_to(preserved, target_is_directory=True)
        paths = [target, preserved]
        before = [snapshot(path) for path in paths]
        result = self.delete(target, recursive=True)
        self.assert_denied_unchanged(paths, result, before)
        self.assertTrue(any(item.get("state") == "symlink"
                            for item in result.get("blocked", [])), result)

    def test_symlink_parent_alias_is_rejected(self):
        preserved = self.base / "parent-target"
        self.put(preserved / "nested/sentinel.txt")
        alias = self.base / "parent-alias"
        alias.symlink_to(preserved, target_is_directory=True)
        before = [snapshot(preserved), snapshot(alias)]
        result = self.delete(alias / "nested", recursive=True)
        self.assert_denied_unchanged([preserved, alias], result, before)

    def test_fifo_is_not_treated_as_a_regular_file(self):
        target = self.base / "fifo"
        target.mkdir()
        os.mkfifo(target / "pipe")
        before = [snapshot(target)]
        result = self.delete(target, recursive=True)
        self.assert_denied_unchanged([target], result, before)
        self.assertTrue(any(item.get("state") == "unsupported-type"
                            for item in result.get("blocked", [])), result)

    def test_overlapping_parent_child_and_absolute_alias_are_deduplicated(self):
        target = self.base / "overlap"
        file = self.put(target / "nested/file.txt")
        expected = {self.rel(path) for path in (target, target / "nested", file)}
        result = self.delete(file, target, str(target), self.rel(target) + "/nested", recursive=True)
        self.assertEqual(result["status"], "OK", result)
        paths = [item["path"] for item in result["deleted"]]
        self.assertEqual(set(paths), expected)
        self.assertEqual(len(paths), len(expected))
        self.assertFalse(result.get("failed"), result)
        self.assertFalse(target.exists())

    def test_recursive_dry_run_preserves_entire_tree_and_deletion_audit(self):
        target = self.base / "dry-run"
        self.put(target / "nested/file.txt")
        before = snapshot(target)
        audit = MCP / "deletes.jsonl"
        audit_before = audit.read_bytes() if audit.exists() else None
        result = self.delete(target, recursive=True, dry_run=True)
        self.assertEqual(result["status"], "OK", result)
        self.assertTrue(result["dry_run"])
        self.assertEqual(set(result["deletable"]), {self.rel(path) for path in
                                                   (target, target / "nested", target / "nested/file.txt")})
        self.assertEqual(snapshot(target), before)
        self.assertEqual(audit.read_bytes() if audit.exists() else None, audit_before)

    def test_plan_revalidation_rejects_added_child_before_any_deletion(self):
        target = self.base / "new-child-race"
        self.put(target / "original.txt")
        planned = self.plan(target)
        self.put(target / "late.txt", "new unique data\n")
        before = snapshot(target)
        result = deletion.execute(str(ROOT), planned)
        self.assertEqual(result["status"], "ERROR", result)
        self.assertFalse(result.get("deleted"), result)
        self.assertEqual(snapshot(target), before)
        self.assertTrue(result.get("failed"), result)

    def test_plan_revalidation_rejects_changed_file_with_same_size_and_mtime(self):
        target = self.base / "content-race"
        file = self.put(target / "original.txt", "before\n")
        planned = self.plan(target)
        original = file.stat()
        file.write_text("after!\n")
        os.utime(file, ns=(original.st_atime_ns, original.st_mtime_ns))
        before = snapshot(target)
        result = deletion.execute(str(ROOT), planned)
        self.assertEqual(result["status"], "ERROR", result)
        self.assertFalse(result.get("deleted"), result)
        self.assertEqual(snapshot(target), before)
        self.assertTrue(result.get("failed"), result)

    def test_plan_revalidation_rejects_symlink_replacement(self):
        target = self.base / "symlink-race"
        file = self.put(target / "original.txt")
        external = self.put(self.base / "external.txt", "keep external\n")
        planned = self.plan(target)
        file.unlink()
        file.symlink_to(external)
        before = [snapshot(target), snapshot(external)]
        result = deletion.execute(str(ROOT), planned)
        self.assertEqual(result["status"], "ERROR", result)
        self.assertFalse(result.get("deleted"), result)
        self.assertEqual([snapshot(target), snapshot(external)], before)

    def test_mcp_and_both_cli_entries_share_recursive_and_dry_run_contract(self):
        call("gate_reset")
        self.assertEqual(call("gate_set_recipe", name="Quick")["status"], "OK")
        self.assertEqual(call("gate_pass", gate_id="g_entry", agent="unity-developer")["status"], "OK")
        self.assertEqual(call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO,
                              status="COMPLETE")["status"], "OK")
        self.assertEqual(call("gate_set_workspace", path=self.rel(self.assets))["status"], "OK")

        def invoke(channel: str, target: Path, *, recursive=False, dry_run=False):
            if channel == "mcp":
                return call("delete_gated", paths=[self.rel(target)], reason="transport contract",
                            recursive=recursive, dry_run=dry_run)
            script = MCP / "validation" / ("gate_cli.py" if channel == "cli" else "delete_gated.py")
            arguments = ["delete"] if channel == "cli" else []
            arguments += ["--reason", "transport contract"]
            if recursive:
                arguments.append("--recursive")
            if dry_run:
                arguments.append("--dry-run")
            arguments.append(self.rel(target))
            process = subprocess.run([sys.executable, str(script), *arguments], cwd=ROOT,
                                     capture_output=True, text=True)
            result = json.loads(process.stdout)
            self.assertEqual(process.returncode, 0 if result["status"] == "OK" else 1,
                             process.stdout + process.stderr)
            return result

        summaries = []
        for channel in ("mcp", "cli", "compat"):
            with self.subTest(channel=channel):
                target = self.assets / channel
                file = self.put(target / "nested/file.txt")
                before = snapshot(target)
                denied = invoke(channel, target)
                self.assertEqual(denied["status"], "DENIED", denied)
                self.assertEqual(snapshot(target), before)
                dry = invoke(channel, target, recursive=True, dry_run=True)
                self.assertEqual(dry["status"], "OK", dry)
                self.assertTrue(dry["dry_run"])
                self.assertEqual(dry["count"], 3)
                self.assertEqual(snapshot(target), before)
                result = invoke(channel, target, recursive=True)
                self.assertEqual(result["status"], "OK", result)
                self.assertEqual(result["count"], 3)
                expected = {self.rel(path) for path in (target, target / "nested", file)}
                self.assertEqual({item["path"] for item in result["deleted"]}, expected)
                self.assertFalse(target.exists())
                audit = json.loads((MCP / "deletes.jsonl").read_text().splitlines()[-1])
                self.assertEqual(audit["channel"], "mcp" if channel == "mcp" else "cli")
                self.assertEqual(audit["requested_paths"], [self.rel(target)])
                self.assertEqual(set(audit["expanded_paths"]), expected)
                self.assertTrue(audit["recursive"])
                self.assertEqual(audit["reason"], "transport contract")
                self.assertEqual(set(audit["paths"]), expected)
                summaries.append((denied["error"], dry["count"], result["count"]))
        self.assertEqual(summaries, [summaries[0]] * 3)


if __name__ == "__main__":
    unittest.main(verbosity=2)
