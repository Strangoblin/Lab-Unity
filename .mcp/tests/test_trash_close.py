"""垃圾桶与出口验收的契约 —— 「删除可逆」与「交付有据」这两件事必须被机器验证.

v3 把删除验成不可恢复性（tracked-dirty / GUID 被引用都硬拦），代价是所有正常清理都要
先提交、先解引用。v4 反过来：不可逆由工具消掉（搬进 `.mcp/trash` + manifest），
留底之后删除就是可逆的。

出口验收则是 v4 新增的主要旋钮：每项义务都要落到一条可核查的事实上。
这套测试全部在临时目录里跑 —— 生产垃圾桶与真实 Assets 必须零改动。
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

MCP_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MCP_ROOT / "lib"))

import close as close_mod  # noqa: E402
import profiles  # noqa: E402
import session  # noqa: E402
from validation import policy, trash  # noqa: E402


class FixtureMixin:
    """临时项目根 —— 每个用例自己的一份，绝不碰真实仓库。"""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = (Path(self.temp.name) / "project").resolve()
        self.root.mkdir()
        self._saved = {key: os.environ.get(key) for key in
                       ("MCP_SESSION_FILE", "MCP_WRITES_FILE", "MCP_READS_FILE")}
        base = Path(self.temp.name)
        os.environ["MCP_SESSION_FILE"] = str(base / "session.json")
        os.environ["MCP_WRITES_FILE"] = str(base / "writes.jsonl")
        os.environ["MCP_READS_FILE"] = str(base / "reads.jsonl")
        self.addCleanup(self._restore)

    def _restore(self) -> None:
        for key, value in self._saved.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value

    def put(self, relative: str, content: str = "body\n") -> Path:
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8", newline="")
        return target


class TrashRoundTripTests(FixtureMixin, unittest.TestCase):
    def test_delete_then_restore_returns_identical_content(self) -> None:
        original = "Shader \"Hidden/X\"\n{\n}\n"
        self.put("Assets/Mine/Shaders/X/A.shader", original)

        planned = trash.plan(str(self.root), ["Assets/Mine/Shaders/X/A.shader"])
        self.assertEqual(planned["blocked"], [])
        result = trash.execute(str(self.root), planned, reason="test", channel="test")
        self.assertEqual(result["status"], "OK")
        self.assertFalse((self.root / "Assets/Mine/Shaders/X/A.shader").exists())

        restored = trash.restore(str(self.root), result["trash_id"])
        self.assertEqual(restored["status"], "OK")
        self.assertEqual((self.root / "Assets/Mine/Shaders/X/A.shader").read_text(), original)

    def test_manifest_records_facts_not_just_paths(self) -> None:
        """留底不是审计：审计回答意图，manifest 回答内容在哪、原来什么状态。"""
        self.put("Assets/Mine/Scripts/A.cs", "class A { }\n")
        planned = trash.plan(str(self.root), ["Assets/Mine/Scripts/A.cs"])
        result = trash.execute(str(self.root), planned, reason="manifest check")
        manifest_path = self.root / result["trash_root"] / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        entry = manifest["entries"][0]
        self.assertEqual(entry["path"], "Assets/Mine/Scripts/A.cs")
        self.assertEqual(entry["size"], len("class A { }\n"))
        self.assertEqual(len(entry["sha256"]), 64)
        self.assertIn("git_state", entry)
        self.assertEqual(manifest["reason"], "manifest check")

    def test_directory_delete_records_children(self) -> None:
        self.put("Assets/Mine/Shaders/Old/A.shader")
        self.put("Assets/Mine/Shaders/Old/B.hlsl")
        planned = trash.plan(str(self.root), ["Assets/Mine/Shaders/Old"])
        self.assertEqual(planned["entries"][0]["kind"], "directory")
        children = {child["path"] for child in planned["entries"][0]["children"]}
        self.assertIn("Assets/Mine/Shaders/Old/A.shader", children)

        result = trash.execute(str(self.root), planned, reason="dir")
        self.assertEqual(result["status"], "OK")
        self.assertFalse((self.root / "Assets/Mine/Shaders/Old").exists())
        self.assertTrue((self.root / "Assets/Mine/Shaders").is_dir(), "父目录不该被带走")

    def test_restore_never_overwrites(self) -> None:
        """目标已存在时不覆盖 —— 覆盖等于丢掉别人的改动。"""
        self.put("Assets/Mine/A.shader", "v1\n")
        planned = trash.plan(str(self.root), ["Assets/Mine/A.shader"])
        result = trash.execute(str(self.root), planned, reason="collide")
        self.put("Assets/Mine/A.shader", "v2 belongs to someone else\n")

        restored = trash.restore(str(self.root), result["trash_id"])
        self.assertEqual(restored["status"], "PARTIAL")
        self.assertEqual(restored["blocked"][0]["state"], "target-exists")
        self.assertEqual((self.root / "Assets/Mine/A.shader").read_text(),
                         "v2 belongs to someone else\n")

    def test_symlink_is_not_moved(self) -> None:
        outside = Path(self.temp.name) / "outside.txt"
        outside.write_text("outside\n")
        (self.root / "Assets/Mine").mkdir(parents=True)
        link = self.root / "Assets/Mine/link.txt"
        link.symlink_to(outside)

        planned = trash.plan(str(self.root), ["Assets/Mine/link.txt"])
        self.assertEqual(planned["blocked"][0]["state"], "symlink")
        result = trash.execute(str(self.root), planned, reason="link")
        self.assertEqual(result["status"], "DENIED")
        self.assertTrue(link.is_symlink(), "被拒时零改动")

    def test_scope_roots_are_protected(self) -> None:
        for target in (".", ".mcp/trash"):
            planned = trash.plan(str(self.root), [target])
            self.assertTrue(planned["blocked"], target)

    def test_path_outside_project_is_rejected(self) -> None:
        planned = trash.plan(str(self.root), ["../escape.txt"])
        self.assertEqual(planned["blocked"][0]["state"], "path-not-allowed")

    def test_missing_entries_are_not_errors(self) -> None:
        planned = trash.plan(str(self.root), ["Assets/Mine/Gone.shader"])
        result = trash.execute(str(self.root), planned, reason="gone")
        self.assertEqual(result["status"], "OK")
        self.assertEqual(result["count"], 0)

    def test_trash_survives_a_rejected_batch_untouched(self) -> None:
        self.put("Assets/Mine/Keep.shader")
        planned = trash.plan(str(self.root),
                             ["Assets/Mine/Keep.shader", "../escape.txt"])
        self.assertTrue(planned["blocked"])
        trash.execute(str(self.root), planned, reason="mixed")
        self.assertTrue((self.root / "Assets/Mine/Keep.shader").exists(),
                        "整批拒绝时一条都不能搬")

    def test_list_buckets_reads_manifests(self) -> None:
        self.put("Assets/Mine/A.shader")
        planned = trash.plan(str(self.root), ["Assets/Mine/A.shader"])
        result = trash.execute(str(self.root), planned, reason="listed")
        buckets = trash.list_buckets(str(self.root))
        self.assertEqual(len(buckets), 1)
        self.assertEqual(buckets[0]["id"], result["trash_id"])
        self.assertEqual(buckets[0]["reason"], "listed")


class CloseTests(FixtureMixin, unittest.TestCase):
    def open(self, profile: str = "shader", workspace: list[str] | None = None) -> None:
        session.open_task("close contract", workspace or [], profile)

    def test_profile_obligations_are_data(self) -> None:
        for name in profiles.names():
            spec = profiles.PROFILES[name]
            self.assertIn("obligations", spec, name)
            for obligation in spec["obligations"]:
                self.assertIn(obligation, profiles.OBLIGATIONS, f"{name}/{obligation}")

    def test_profiles_actually_differ(self) -> None:
        """v3 的六个 recipe 链完全相同 —— 一个不产生差异的声明就是纯税。"""
        sets = {name: tuple(profiles.PROFILES[name]["obligations"])
                for name in profiles.names()}
        self.assertGreater(len(set(sets.values())), 1)
        self.assertNotEqual(sets["shader"], sets["research"])

    def test_missing_knowledge_blocks_close(self) -> None:
        self.open("shader", ["Assets/Mine/Shaders/PostProcess/SSC"])
        with patch.object(close_mod, "check_compile",
                          return_value={"id": "compile", "status": "OK"}):
            result = close_mod.run("shader")
        self.assertEqual(result["status"], "BLOCKED")
        self.assertIn("knowledge", result["blocking"])

    def test_reading_the_required_files_satisfies_knowledge(self) -> None:
        self.open("shader", ["Assets/Mine/Shaders/PostProcess/SSC"])
        for entry in profiles.PROFILES["shader"]["required"]:
            session.record_read(f".agents/knowledge/{entry}", source="test")
        with patch.object(close_mod, "check_compile",
                          return_value={"id": "compile", "status": "OK"}), \
             patch.object(close_mod, "check_verify",
                          return_value={"id": "verify", "status": "OK"}):
            result = close_mod.run("shader")
        knowledge = next(c for c in result["checks"] if c["id"] == "knowledge")
        self.assertEqual(knowledge["status"], "OK")

    def test_waiver_marks_the_check_and_records_the_reason(self) -> None:
        self.open("shader", ["Assets/Mine/Shaders/PostProcess/SSC"])
        session.add_waiver("verify", "纯逻辑改动，无画面可验")
        with patch.object(close_mod, "check_compile",
                          return_value={"id": "compile", "status": "OK"}):
            result = close_mod.run("shader")
        verify = next(c for c in result["checks"] if c["id"] == "verify")
        self.assertEqual(verify["status"], "WAIVED")
        self.assertIn("无画面可验", verify["waived"])

    def test_unavailable_compile_is_not_ok(self) -> None:
        """「无法验证」与「验证通过」是两件事，混同会让出口验收失去意义。"""
        self.open("shader", ["Assets/Mine/Shaders/PostProcess/SSC"])
        session.record_write("Assets/Mine/Shaders/PostProcess/SSC/A.shader", "asset", "test")
        with patch("subprocess.run", side_effect=FileNotFoundError("unityctl")):
            result = close_mod.check_compile(None)
        self.assertEqual(result["status"], "UNAVAILABLE")

    def test_compile_errors_after_last_write_miss(self) -> None:
        self.open("shader", ["Assets/Mine/Shaders/PostProcess/SSC"])
        session.record_write("Assets/Mine/Shaders/PostProcess/SSC/A.shader", "asset", "test")
        completed = subprocess.CompletedProcess(
            args=(), returncode=0, stdout="Assets/A.cs(3,1): error CS1002: ; expected\n")
        with patch("subprocess.run", return_value=completed):
            result = close_mod.check_compile(None)
        self.assertEqual(result["status"], "MISS")
        self.assertEqual(len(result["errors"]), 1)

    def test_memory_obligation_needs_a_dated_record(self) -> None:
        self.open("script", ["Assets/Mine/Scripts"])
        session.record_write("Assets/Mine/Scripts/A.cs", "asset", "test")
        self.assertEqual(close_mod.check_memory(None)["status"], "MISS")

        session.record_write(".agents/memory/unity/2099-01-01-x.md", "memory", "test")
        partial = close_mod.check_memory(None)
        self.assertEqual(partial["status"], "PARTIAL", "有记录但索引未更新")

        session.record_write(".agents/memory/unity/MEMORY.md", "memory", "test")
        self.assertEqual(close_mod.check_memory(None)["status"], "OK")

    def test_no_asset_writes_means_no_obligations_to_prove(self) -> None:
        self.open("script", ["Assets/Mine/Scripts"])
        self.assertEqual(close_mod.check_compile(None)["status"], "OK")
        self.assertEqual(close_mod.check_verify(None)["status"], "OK")
        self.assertEqual(close_mod.check_memory(None)["status"], "OK")

    def test_verification_search_reaches_nested_roots(self) -> None:
        """回归：从项目根起 `rel_root="."` 曾被整棵剪掉，verify 永远 MISS。

        判据是「能否到达验证根」：`Assets` 任意深度的 `Validation~` 与项目根的
        `tmp/sessions` 都要被遍历，验证根之外的产物不算证据。
        """
        self.put("Validation~/2026-01-01-x/report.json", "{}\n")
        self.put("tmp/sessions/2026-01-01-run/output/results.json", "{}\n")
        self.put("Assets/Mine/Shaders/X/Validation~/fixtures/probe.json", "{}\n")
        self.put("Assets/Mine/Shaders/X/result.json", "{}\n")
        self.put("Library/Big/result.json", "{}\n")
        with patch.object(close_mod, "PROJECT_ROOT", str(self.root)):
            found = {item["path"] for item in close_mod.find_verification(0)}
        self.assertEqual(found, {
            "Validation~/2026-01-01-x/report.json",
            "tmp/sessions/2026-01-01-run/output/results.json",
            "Assets/Mine/Shaders/X/Validation~/fixtures/probe.json",
        })

    def test_verification_evidence_clears_verify_obligation(self) -> None:
        """同一回归的端到端形状：写入之后的报告让 verify 从 MISS 变为 OK。"""
        self.open("script", ["Assets/Mine/Scripts"])
        session.record_write("Assets/Mine/Scripts/A.cs", "asset", "test")
        with patch.object(close_mod, "PROJECT_ROOT", str(self.root)):
            self.assertEqual(close_mod.check_verify(None)["status"], "MISS")
            self.put("tmp/sessions/2026-01-01-run/output/results.json", "{}\n")
            result = close_mod.check_verify(None)
        self.assertEqual(result["status"], "OK")

    def test_research_profile_needs_nothing(self) -> None:
        self.open("research")
        self.assertEqual(close_mod.run("research")["status"], "OK")

    def test_close_verify_is_warn_by_policy_not_block(self) -> None:
        """默认策略里 verify 是 warn —— 画面验证常受环境限制，先提示。"""
        self.assertEqual(policy.level_of("close.verify"), "warn")
        self.assertEqual(policy.level_of("close.compile"), "block")


class SessionLedgerTests(FixtureMixin, unittest.TestCase):
    def test_open_replaces_the_previous_session(self) -> None:
        session.open_task("first", ["Assets/Mine/A"], "shader")
        session.add_waiver("verify", "old")
        session.open_task("second", ["Assets/Mine/B"], "script")
        data = session.load()
        self.assertEqual(data["task"], "second")
        self.assertEqual(data["workspace"], ["Assets/Mine/B"])
        self.assertEqual(data["waivers"], [], "新任务不带旧豁免")

    def test_corrupt_session_reads_as_empty(self) -> None:
        Path(os.environ["MCP_SESSION_FILE"]).write_text("{not json", encoding="utf-8")
        data = session.load()
        self.assertIsNone(data["task"])
        self.assertEqual(data["workspace"], [])

    def test_ledger_filters_by_time(self) -> None:
        session.record_write("Assets/Mine/A.cs", "asset", "test")
        entries = session.writes_since(None)
        self.assertEqual(len(entries), 1)
        self.assertTrue(all(entry["zone"] == "asset" for entry in entries))

    def test_expand_adds_to_the_sandbox_without_resetting_evidence(self) -> None:
        """扩充是用户对话里确认的，命令只负责留痕 —— 证据起点不能因此重置。

        重置会让「扩充前读过什么 / 写过什么」从出口验收里消失，等于用扩充换豁免。
        """
        session.open_task("expand contract", ["Assets/Mine/A"], "shader")
        opened_at = session.load()["opened_at"]
        result = session.expand_task(["Assets/Mine/B"], "用户确认: 共享库一起改")
        self.assertEqual(result["added"], ["Assets/Mine/B"])
        data = session.load()
        self.assertEqual(data["workspace"], ["Assets/Mine/A", "Assets/Mine/B"])
        self.assertEqual(data["opened_at"], opened_at, "扩充不得重置证据起点")
        self.assertEqual(data["expansions"][0]["reason"], "用户确认: 共享库一起改")

    def test_expand_is_idempotent(self) -> None:
        session.open_task("expand idempotent", ["Assets/Mine/A"], "shader")
        session.expand_task(["Assets/Mine/A"], "重复给同一目录")
        data = session.load()
        self.assertEqual(data["workspace"], ["Assets/Mine/A"])
        self.assertEqual(data["expansions"][0]["dirs"], [])


if __name__ == "__main__":
    unittest.main()
