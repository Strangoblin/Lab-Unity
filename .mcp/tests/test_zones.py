"""分区、沙箱与 hook 判定的契约 —— 业务式管理的核心边界.

这套测试回答三个问题：
  1. 路径属于哪个区？（zones.zone_of）
  2. 这个区允许怎么改？（evaluate_write / evaluate_delete）
  3. hook 在真实 tool_input 形状下给出的判定，与上面两条是否一致？

第 3 条是重点：v3 的教训是判定写在传输层旁边，两条通道迟早分叉。v4 只有一份判定，
本套件通过 hooks.py 的真实入口验证它 —— 而不是只测内部函数。
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

MCP_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MCP_ROOT / "lib"))

import judgement  # noqa: E402
import session  # noqa: E402
import zones  # noqa: E402
from validation import policy  # noqa: E402

REPO = Path(MCP_ROOT).parent
HOOKS_SH = MCP_ROOT / "bin" / "hooks.sh"


class LedgerMixin:
    """每个用例一份独立账本 —— 生产账本必须零改动。"""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name)
        self._saved = {key: os.environ.get(key) for key in
                       ("MCP_SESSION_FILE", "MCP_WRITES_FILE", "MCP_READS_FILE")}
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


class ZoneTests(LedgerMixin, unittest.TestCase):
    def test_zone_assignment(self) -> None:
        """`zone_of` 是纯查表 —— 目录级判断要的是表面事实，不是「实际能不能写」。"""
        cases = {
            "Assets/Mine/Shaders/X/Y.shader": "asset",
            "Assets/Scenes/Test.unity": "denied",
            "Library/x.so": "denied",
            "tmp/a.txt": "scratch",
            ".mcp/trash/bucket/manifest.json": "scratch",
            ".agents/memory/meta/MEMORY.md": "memory",
            ".agents/knowledge/meta/architecture.md": "system",
            ".agents/skills/agent-workflow/AutoMode.md": "system",
            ".mcp/policy.json": "system",
            "README.md": "denied",
        }
        for path, expected in cases.items():
            self.assertEqual(zones.zone_of(path), expected, path)

    def test_effective_zone_only_lifts_denied(self) -> None:
        """已入库的仓库文件算体系区；资产区即使被 git 兜底也仍是 asset（出口验收按它挑义务）。"""
        self.assertEqual(zones.effective_zone("AGENTS.md"), "system")
        self.assertEqual(zones.effective_zone("Library/x.so"), "denied")
        self.assertEqual(zones.effective_zone("Assets/Mine/Anything.shader"), "asset")
        self.assertEqual(zones.zone_of("AGENTS.md"), "denied", "查表结果不变，只是实际归属被抬起")

    def test_trash_precedes_its_parent_zone(self) -> None:
        """`.mcp/trash` 必须先命中，否则删除留底会被当成体系区写入。"""
        self.assertEqual(zones.zone_of(".mcp/trash/x/files/a.shader"), "scratch")
        self.assertEqual(zones.zone_of(".mcp/zones.json"), "system")

    def test_absolute_paths_are_relativised(self) -> None:
        absolute = str(REPO / "Assets/Mine/Shaders/A.shader")
        self.assertEqual(zones.zone_of(absolute), "asset")
        self.assertEqual(zones.normalize(absolute), "Assets/Mine/Shaders/A.shader")

    def test_exposed_zone_prefixes_are_ordered_and_complete(self) -> None:
        prefixes = zones.zone_prefixes()
        names = [prefix for _, prefix in prefixes]
        self.assertLess(names.index(".mcp/trash"), names.index(".mcp"))
        self.assertIn("Assets/Mine", names)
        # 平台目录来自数据，不是代码里的字面量
        self.assertEqual(len(names), len(set(names)), "重复前缀会让顺序失去意义")
        self.assertEqual(zones.git_allow_zones(), ["denied", "asset"], "git 兜底作用于哪些区是数据")

    def test_sibling_prefix_is_not_inside(self) -> None:
        """`Assets/MineOther` 不在 `Assets/Mine` 内 —— 前缀匹配必须是分隔符边界。"""
        self.assertFalse(zones.under_any("Assets/MineOther/x.cs", ["Assets/Mine"]))
        self.assertTrue(zones.under_any("Assets/Mine/x.cs", ["Assets/Mine"]))
        self.assertTrue(zones.under_any("Assets/Mine", ["Assets/Mine"]))


class SandboxTests(LedgerMixin, unittest.TestCase):
    def test_asset_write_requires_sandbox(self) -> None:
        result = zones.evaluate_write("Assets/Mine/Shaders/X/A.shader", [])
        self.assertEqual(result["status"], "DENIED")
        self.assertEqual(result["error"], "SANDBOX_NOT_SET")
        self.assertIn("cli.py open", result["hint"])

    def test_asset_write_inside_and_outside_sandbox(self) -> None:
        workspace = ["Assets/Mine/Shaders/PostProcess/SSC"]
        inside = zones.evaluate_write("Assets/Mine/Shaders/PostProcess/SSC/A.shader", workspace)
        self.assertEqual(inside["status"], "OK")
        # 跨到兄弟目录、而且**未入库** —— 新建没有 git 兜底，仍要拦住
        outside = zones.evaluate_write("Assets/Mine/Special/HLSL/New.hlsl", workspace)
        self.assertEqual(outside["status"], "DENIED")
        self.assertEqual(outside["error"], "SANDBOX_VIOLATION")
        self.assertIn("cli.py expand", outside["hint"], "拒绝理由要指向扩充通道")

    def test_multiple_workspace_roots_cover_shared_library(self) -> None:
        workspace = ["Assets/Mine/Shaders/PostProcess/SSC", "Assets/Mine/Special/HLSL"]
        self.assertEqual(
            zones.evaluate_write("Assets/Mine/Special/HLSL/PBR.hlsl", workspace)["status"], "OK")

    def test_memory_is_writable_without_sandbox(self) -> None:
        """收尾写记忆曾被工作目录边界挡在界外（实测 2 次）—— 记忆区不在沙箱内。"""
        self.assertEqual(
            zones.evaluate_write(".agents/memory/meta/MEMORY.md", [])["status"], "OK")
        self.assertEqual(
            zones.evaluate_write(".agents/memory/unity/evidence/x/report.json", [])["status"], "OK")

    def test_existing_memory_record_is_editable_in_place(self) -> None:
        """2026-10-28 用户裁决：绕路新建一条副本不如原地改 —— 历史由 git 兜底。"""
        # 不钉具体文件：结构自检会按年龄退役记录，钉死一条等于给归档埋雷。
        records = sorted((REPO / ".agents/memory/meta").glob("20??-??-??-*.md"))
        self.assertTrue(records, "夹具依赖至少一条 dated 记录存在")
        existing = str(records[0].relative_to(REPO))
        result = zones.evaluate_write(existing, [])
        self.assertEqual(result["status"], "OK")
        self.assertNotIn("error", result)

    def test_new_dated_record_is_allowed(self) -> None:
        fresh = ".agents/memory/meta/2099-01-01-not-yet-written.md"
        self.assertFalse((REPO / fresh).exists())
        self.assertEqual(zones.evaluate_write(fresh, [])["status"], "OK")

    def test_git_tracked_path_outside_zones_is_writable(self) -> None:
        """git 保护 = 可随意编辑（用户裁决）：根级入口文档不再是够不到的地方。

        `AGENTS.md` 曾撞 `ZONE_DENIED` —— 项目第一个入口文件恰好在模型够不到处。
        """
        self.assertTrue((REPO / "AGENTS.md").is_file(), "夹具依赖 AGENTS.md 存在")
        result = zones.evaluate_write("AGENTS.md", [])
        self.assertEqual(result["status"], "OK")
        self.assertTrue(result.get("git_protected"), "要靠 git 兜底，不是靠白名单")
        self.assertEqual(result["zone"], "system", "按体系区处理并记录")

    def test_untracked_path_outside_zones_is_rejected(self) -> None:
        """分区表之外、又不在 git 里 —— 没有兜底，拒绝是对的。"""
        result = zones.evaluate_write("Library/generated.bin", [])
        self.assertEqual(result["status"], "DENIED")
        self.assertEqual(result["error"], "ZONE_DENIED")

    def test_git_fact_is_asked_not_assumed(self) -> None:
        self.assertTrue(zones.is_git_tracked("AGENTS.md"))
        self.assertFalse(zones.is_git_tracked("Library/generated.bin"))

    def test_git_protected_asset_bypasses_the_sandbox(self) -> None:
        """已入库资产改错可 checkout 取回 —— 沙箱只管新建与未跟踪。"""
        from unittest.mock import patch
        workspace = ["Assets/Mine/Shaders/PostProcess/SSC"]
        with patch.object(zones, "is_git_tracked", return_value=True):
            result = zones.evaluate_write("Assets/Mine/Special/HLSL/PBR.hlsl", workspace)
        self.assertEqual(result["status"], "OK")
        self.assertTrue(result.get("git_protected"))

    def test_untracked_asset_outside_sandbox_is_still_denied(self) -> None:
        """跨目录改共享库的误拦已消失（见上一个用例），但**新建**没有兜底，仍要拦住。"""
        workspace = ["Assets/Mine/Shaders/PostProcess/SSC"]
        result = zones.evaluate_write("Assets/Mine/Special/HLSL/New.hlsl", workspace)
        self.assertEqual(result["status"], "DENIED")
        self.assertEqual(result["error"], "SANDBOX_VIOLATION")

    def test_scratch_is_free(self) -> None:
        self.assertEqual(zones.evaluate_write("tmp/anything.txt", [])["status"], "OK")

    def test_delete_respects_the_same_boundary(self) -> None:
        workspace = ["Assets/Mine/Shaders/PostProcess/SSC"]
        self.assertEqual(zones.evaluate_delete("Assets/Mine/Shaders/PostProcess/SSC/A.shader",
                                               workspace)["status"], "OK")
        self.assertEqual(zones.evaluate_delete("Assets/Mine/Shaders/Other/A.shader",
                                               workspace)["error"], "SANDBOX_VIOLATION")
        self.assertEqual(zones.evaluate_delete(".agents/memory/meta/MEMORY.md", [])["status"], "OK")
        # 已入库的历史记录也允许删（trash 留底 + git 双兜底）
        self.assertEqual(
            zones.evaluate_delete(".agents/memory/meta/2026-08-03-experiment-mode.md", [])["status"],
            "OK")


class NormTests(LedgerMixin, unittest.TestCase):
    def test_shader_without_declaration_is_blocked(self) -> None:
        decision = judgement.decide_write(
            "Assets/Mine/Shaders/PostProcess/SSC/A.shader",
            "float4 frag() : SV_Target { return 0; }\n",
            None, ["Assets/Mine/Shaders/PostProcess/SSC"])
        self.assertEqual(decision["status"], "DENIED")
        self.assertEqual(decision["error"], "NORM_VIOLATION")

    def test_complete_shader_is_allowed(self) -> None:
        content = 'Shader "Hidden/Test"\n{\n    SubShader { Pass { } }\n}\n'
        decision = judgement.decide_write(
            "Assets/Mine/Shaders/PostProcess/SSC/A.shader", content, None,
            ["Assets/Mine/Shaders/PostProcess/SSC"])
        self.assertEqual(decision["status"], "OK")
        self.assertTrue(decision["is_new"])

    def test_cs_without_type_declaration_is_blocked(self) -> None:
        decision = judgement.decide_write(
            "Assets/Mine/Scripts/Test.cs", "void Hello() { }\n", None,
            ["Assets/Mine/Scripts"])
        self.assertEqual(decision["status"], "DENIED")

    def test_existing_file_only_checks_added_lines(self) -> None:
        """改缩进不是内容变更 —— 整段被判成新增会让纯格式化被拦。"""
        old = "using UnityEngine;\n\nclass A\n{\n    void F()\n    {\n        int x = 1;\n    }\n}\n"
        new = "using UnityEngine;\n\nclass A\n{\n  void F()\n  {\n    int x = 1;\n  }\n}\n"
        decision = judgement.decide_write("Assets/Mine/Scripts/A.cs", new, old,
                                          ["Assets/Mine/Scripts"])
        self.assertEqual(decision["status"], "OK")

    def test_placement_blocks_new_file_at_wrong_root(self) -> None:
        decision = judgement.decide_write(
            "Assets/Mine/Loose/A.hlsl", "float4 f() : SV_Target { return 0; }\n", None,
            ["Assets/Mine"])
        self.assertEqual(decision["status"], "DENIED")
        self.assertIn("placement.root.new",
                      [v.get("check") for v in decision["violations"]])

    def test_placement_only_warns_when_editing_in_place(self) -> None:
        decision = judgement.decide_write(
            "Assets/Mine/Loose/A.hlsl", "float4 f() : SV_Target { return 0; }\n",
            "float4 g() : SV_Target { return 0; }\n", ["Assets/Mine"])
        self.assertEqual(decision["status"], "OK")
        self.assertTrue(decision["warnings"])

    def test_policy_downgrade_is_respected(self) -> None:
        """策略只能放宽：把 region-added 降为 warn 后不应阻断。"""
        saved = policy._cache
        policy._cache = {"checks": {"norm.region-added": {"level": "warn"}}}
        self.addCleanup(lambda: setattr(policy, "_cache", saved))
        decision = judgement.decide_write(
            "Assets/Mine/Scripts/A.cs",
            "using UnityEngine;\nclass A\n{\n    int x;\n}\n",
            "using UnityEngine;\nclass A\n{\n}\n", ["Assets/Mine/Scripts"])
        self.assertEqual(decision["status"], "OK")

    def test_policy_cannot_tighten_beyond_intrinsic(self) -> None:
        saved = policy._cache
        policy._cache = {"checks": {"norm.divider-added": {"level": "block"}}}
        self.addCleanup(lambda: setattr(policy, "_cache", saved))
        self.assertEqual(policy.level_of("norm.divider-added"), "warn")


class HookContractTests(LedgerMixin, unittest.TestCase):
    """通过真实 PreToolUse 入口验证判定 —— 不做内部函数断言就不知道 hook 是否真接上了。"""

    def hook(self, tool: str, tool_input: dict, event: str = "PreToolUse") -> dict:
        payload = json.dumps({"tool_name": tool, "tool_input": tool_input})
        proc = subprocess.run(["bash", str(HOOKS_SH), event], cwd=str(REPO),
                              input=payload, capture_output=True, text=True, timeout=60)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        out = proc.stdout.strip()
        if not out:
            return {}
        return json.loads(out).get("hookSpecificOutput", {})

    def open(self, workspace: list[str], profile: str = "shader") -> None:
        session.open_task("hook contract", workspace, profile)

    def test_write_in_sandbox_is_allowed(self) -> None:
        self.open(["Assets/Mine/Shaders/PostProcess/SSC"])
        result = self.hook("Write", {
            "file_path": str(REPO / "Assets/Mine/Shaders/PostProcess/SSC/A.shader"),
            "content": 'Shader "Hidden/A"\n{\n    SubShader { Pass { } }\n}\n'})
        self.assertEqual(result["permissionDecision"], "allow")

    def test_write_outside_sandbox_is_denied_with_a_reason(self) -> None:
        self.open(["Assets/Mine/Shaders/PostProcess/SSC"])
        result = self.hook("Write", {
            "file_path": str(REPO / "Assets/Mine/Shaders/PostProcess/SSGI/A.shader"),
            "content": 'Shader "Hidden/A"\n{\n}\n'})
        self.assertEqual(result["permissionDecision"], "deny")
        self.assertIn("沙箱", result["permissionDecisionReason"])

    def test_memory_record_edit_is_allowed_in_place(self) -> None:
        """2026-10-28 用户裁决：既有 dated 记录可以原地改，绕路新建副本是错的。"""
        records = sorted((REPO / ".agents/memory/meta").glob("20??-??-??-*.md"))
        self.assertTrue(records, "夹具依赖至少一条 dated 记录存在")
        target = records[0]
        result = self.hook("Edit", {
            "file_path": str(target), "old_string": "ZZZ_NOT_PRESENT", "new_string": "x"})
        self.assertEqual(result["permissionDecision"], "allow")

    def test_root_entry_document_is_writable(self) -> None:
        """AGENTS.md 曾撞 ZONE_DENIED —— 项目入口文档不该在模型够不到的地方。"""
        result = self.hook("Edit", {
            "file_path": str(REPO / "AGENTS.md"),
            "old_string": "ZZZ_NOT_PRESENT", "new_string": "x"})
        self.assertEqual(result["permissionDecision"], "allow")

    def test_edit_that_cannot_apply_still_gets_zone_verdict(self) -> None:
        self.open(["Assets/Mine/Shaders/PostProcess/SSC"],
                  profile="script")
        result = self.hook("Edit", {
            "file_path": str(REPO / "Assets/Mine/Shaders/PostProcess/SSGI/A.shader"),
            "old_string": "ZZZ_NOT_PRESENT", "new_string": "x"})
        self.assertEqual(result["permissionDecision"], "deny")

    def test_unknown_tool_is_always_allowed(self) -> None:
        result = self.hook("Glob", {"pattern": "**/*.shader"})
        self.assertEqual(result["permissionDecision"], "allow")

    def test_bash_redlines(self) -> None:
        self.open([])
        redlines = [
            "git " + "stash --all",
            "rm -rf " + "Assets/Mine/Shaders",
        ]
        for command in redlines:
            result = self.hook("Bash", {"command": command})
            self.assertEqual(result["permissionDecision"], "deny", command)

    def test_bash_asset_delete_is_redirected_to_trash(self) -> None:
        self.open([])
        result = self.hook("Bash", {"command": "rm -f Assets/Mine/Shaders/A.shader"})
        self.assertEqual(result["permissionDecision"], "deny")
        self.assertIn("cli.py delete", result["permissionDecisionReason"])

    def test_bash_move_uses_native_tools(self) -> None:
        """2026-10-28 用户裁决：移动等其他操作让 agent 用原生工具自己跑。

        `mv` 会顺带搬 `.meta`，GUID 随文件走，本来就安全 —— 拦它只是逼一次往返。
        """
        self.open(["Assets/Mine/Shaders/PostProcess/SSC"])
        result = self.hook("Bash", {"command": "mv Assets/Mine/Shaders/A.shader "
                                               "Assets/Mine/Shaders/B.shader"})
        self.assertEqual(result["permissionDecision"], "allow")

    def test_ordinary_bash_is_allowed(self) -> None:
        self.open([])
        result = self.hook("Bash", {"command": "ls Assets/Mine"})
        self.assertEqual(result["permissionDecision"], "allow")

    def test_post_tool_use_records_reads_and_writes(self) -> None:
        self.open(["Assets/Mine/Shaders/PostProcess/SSC"])
        knowledge = REPO / ".agents/knowledge/meta/architecture.md"
        self.hook("Read", {"file_path": str(knowledge)}, event="PostToolUse")
        self.assertTrue(session.reads_since(None), "Read 观测必须落账 —— 申报机制由它取代")

        self.hook("Write", {"file_path": str(REPO / "Assets/Mine/Shaders/PostProcess/SSC/A.shader")},
                  event="PostToolUse")
        applied = [w for w in session.writes_since(None)
                   if str(w.get("source", "")).endswith(":applied")]
        self.assertTrue(applied, "PostToolUse 要能区分「被允许」与「真落盘」")

    def test_reads_of_tracked_repo_files_are_knowledge_evidence(self) -> None:
        """根级文件已入库时按体系区处理 —— 读它同样构成知识证据。"""
        self.hook_read(REPO / "AGENTS.md")
        self.assertTrue(session.reads_since(None))

    def hook_read(self, path: Path) -> None:
        payload = json.dumps({"tool_name": "Read", "tool_input": {"file_path": str(path)}})
        subprocess.run(["bash", str(HOOKS_SH), "PostToolUse"], cwd=str(REPO),
                       input=payload, capture_output=True, text=True, timeout=60)

    def test_reads_of_scratch_are_not_knowledge_evidence(self) -> None:
        session.record_read("tmp/output.json", source="hook:Read")
        self.assertEqual(session.reads_since(None), [])

    def test_denials_record_policy_check_ids(self) -> None:
        """账本要按策略 id 记账 —— 只记信号名，摩擦报告会把确实拦过的检查说成「从未命中」。"""
        import json as _json
        friction = Path(self.temp.name) / "friction.jsonl"
        os.environ["MCP_FRICTION_FILE"] = str(friction)
        self.addCleanup(os.environ.pop, "MCP_FRICTION_FILE", None)

        self.open([])
        self.hook("Write", {"file_path": str(REPO / "Assets/Mine/Shaders/A.shader"),
                            "content": "x"})
        self.hook("Write", {"file_path": str(REPO / "Library/generated.bin"), "content": "x"})

        checks = {_json.loads(line).get("check") for line in friction.read_text().splitlines()}
        self.assertIn("sandbox.asset", checks)
        self.assertIn("scope.zone", checks)

    def test_bash_guard_only_fires_on_asset_targets(self) -> None:
        """正常的临时清理不该被拦，哪怕同一行里还有对资产区的 cli.py 调用。"""
        import hooks
        allowed = [
            "rm -f tmp/x && python3 .mcp/cli.py delete Assets/Mine/A.shader --reason y",
            "rm -f tmp/x",
            "ls Assets/Mine && rm tmp/out.json",
            "python3 .mcp/cli.py delete Assets/Mine/Old.shader --reason x",
            "git stash push -- Assets/Mine/A.cs",
            "mv tmp/a.txt tmp/b.txt",
            # 移动 / 改名用原生命令（用户裁决），只有删除才要求留底
            "mv Assets/Mine/A.shader Assets/Mine/B.shader",
            "mv Assets/Mine/A.shader Assets/Mine/A.shader.meta Assets/Mine/Sub/",
        ]
        for command in allowed:
            self.assertIsNone(hooks._bash_guard(command), command)

        denied = [
            "unlink Assets/Mine/A.meta",
            "rmdir Assets/Mine/Empty",
            "truncate -s0 Assets/Mine/A.hlsl",
            "rm -f Assets/Mine/A.shader",
        ]
        for command in denied:
            self.assertIsNotNone(hooks._bash_guard(command), command)


class HookFailureTests(LedgerMixin, unittest.TestCase):
    """判定核坏掉时的方向：资产区失败即拒，读取与收尾失败即放行。"""

    def make_broken_transport(self) -> Path:
        container = Path(self.temp.name) / "broken"
        container.mkdir()
        (container / "hooks.py").write_text("this is not python(((\n", encoding="utf-8")
        transport = container / "hooks.sh"
        transport.write_text(HOOKS_SH.read_text(encoding="utf-8"), encoding="utf-8")
        return transport

    def run_transport(self, transport: Path, event: str, payload: dict) -> dict:
        proc = subprocess.run(["bash", str(transport), event], cwd=str(REPO),
                              input=json.dumps(payload), capture_output=True,
                              text=True, timeout=60)
        out = proc.stdout.strip()
        return json.loads(out)["hookSpecificOutput"] if out.startswith("{") else {}

    def test_untracked_denied_write_fails_closed(self) -> None:
        """没有 git 兜底的路径仍按失败即拒 —— 兜底消失就该回到保守侧。"""
        result = self.run_transport(self.make_broken_transport(), "PreToolUse", {
            "tool_name": "Write",
            "tool_input": {"file_path": str(REPO / "Assets/Mine/Shaders/A.shader"), "content": "x"}})
        self.assertEqual(result.get("permissionDecision"), "deny")

    def test_memory_and_scratch_fail_open(self) -> None:
        transport = self.make_broken_transport()
        for path in (".agents/memory/meta/MEMORY.md", "tmp/x.txt"):
            result = self.run_transport(transport, "PreToolUse", {
                "tool_name": "Write",
                "tool_input": {"file_path": str(REPO / path), "content": "x"}})
            self.assertNotEqual(result.get("permissionDecision"), "deny", path)


if __name__ == "__main__":
    unittest.main()
