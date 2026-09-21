"""Gate tests — 后果验证门禁 (v2).

链统一为 [g_entry, g_knowledge]:
  g_knowledge = 知识证据（真实文件解析）
  write_gated  = 内容规范后果验证（error 阻断 / warning 提示）+ 注解审计
"""
import sys, json, asyncio, os, subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # .mcp/
sys.path.insert(0, ROOT)

from gate_center import RECIPES, GATE_REGISTRY, RETIRED_GATES
from gates.g_entry import ALLOWED_AGENTS
from validation.project_paths import validate_path

TMP = os.path.join(os.path.dirname(ROOT), "tmp")   # 项目根/tmp


async def call(tool_name: str, **kwargs) -> dict:
    from server import server
    result = await server.call_tool(tool_name, kwargs)
    for block in result.content:
        if hasattr(block, 'text'):
            return json.loads(block.text)
    return {"error": "no text"}


def cleanup(name: str):
    p = os.path.join(TMP, name)
    if os.path.isfile(p):
        os.remove(p)


# ── 测试内容 ──

GOOD_SHADER = '''Shader "Test/OK"
{
    Properties {}
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
'''

HIGH_PRIO = "unity/standard/shader/shader-structure.md, unity/standard/script/script-structure.md"


def self_check():
    """注册表自检 — 链统一 / 退役门禁不在配方 / requires 一致. 违反即回归."""
    assert ALLOWED_AGENTS == {"unity-developer"}, \
        f"Unity MCP 只允许 unity-developer，当前: {ALLOWED_AGENTS}"
    assert set(RECIPES) == {"Production", "Research", "Experiment", "Debug", "Minimal", "Quick"}, \
        f"配方集合异常: {set(RECIPES)}"
    for recipe, gates in RECIPES.items():
        assert gates == ["g_entry", "g_knowledge"], \
            f"配方 '{recipe}' 链未统一为 [g_entry, g_knowledge]: {gates}"
    assert set(RETIRED_GATES) == {"g_mode", "g_script", "g_file", "g_web_search", "g_plan"}, \
        f"退役门禁集合异常: {RETIRED_GATES}"
    for g in RECIPES["Production"]:
        earlier = set(RECIPES["Production"][:RECIPES["Production"].index(g)])
        for req in GATE_REGISTRY[g]["requires"]:
            assert req in earlier, f"'{g}' 的 requires {req} 不在前置门禁集 {sorted(earlier)} 中"
    for g in RETIRED_GATES:
        assert g not in [x for gates in RECIPES.values() for x in gates], \
            f"退役门禁 '{g}' 不应出现在任何配方中"
    print("  ✅ 自检通过: 链统一 / 退役集合 / requires 一致")


async def test_meta_bypass_boundary():
    print("\n── Meta 边界: 不进入 Unity MCP ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    r = await call("gate_pass", gate_id="g_entry", agent="meta-developer")
    assert r["status"] == "DENIED" and r["error"] == "G0_FAILED", f"got {r}"
    r = validate_path(".agents/agents/meta-developer/AGENT.md")
    assert r["status"] == "DENIED" and r["error"] == "PATH_NOT_ALLOWED", f"got {r}"
    print("  ✅ meta-developer 与 .agents 路径均不进入 Unity MCP")


async def test_production_chain():
    print("\n── Production (2-gate chain → write) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    r = await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    assert r["status"] == "OK", f"expected OK, got {r}"
    assert len(r["resolved"]) == 2 and all(r["resolved"]), f"解析失败: {r['resolved']}"
    r = await call("write_gated", path="tmp/test_gate.txt", content="ok")
    assert r["status"] == "OK", f"expected OK, got {r}"
    assert r["annotations"]["mode"] == "Production", f"注解 mode 应为 Production: {r}"
    cleanup("test_gate.txt")
    print(f"  ✅ 2 门禁链走通 + 写入审计记录 OK")


async def test_knowledge_fabricated():
    print("\n── g_knowledge 编造文件名 (denied) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    r = await call("gate_pass", gate_id="g_knowledge",
                   loaded_files=HIGH_PRIO + ", kuwahara-magic.md", status="COMPLETE")
    assert r["status"] == "DENIED" and r["error"] == "G15_UNRESOLVED_FILE", f"got {r}"
    print(f"  ✅ DENIED: {r['error']} ({r['hint'].split('。')[0]})")


async def test_knowledge_reference_impl():
    print("\n── g_knowledge 参考实现代码文件 (OK, 项目内真实文件) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    r = await call("gate_pass", gate_id="g_knowledge",
                   loaded_files=HIGH_PRIO + ", Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader",
                   status="COMPLETE")
    assert r["status"] == "OK", f"expected OK, got {r}"
    print(f"  ✅ OK: 参考实现代码文件解析成功")


async def test_knowledge_missing_high_prio():
    print("\n── g_knowledge 缺高优先级文件 (denied) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    r = await call("gate_pass", gate_id="g_knowledge", loaded_files="shader-structure.md", status="COMPLETE")
    assert r["status"] == "DENIED" and r["error"] == "G15_MISSING_HIGH_PRIORITY", f"got {r}"
    r = await call("gate_pass", gate_id="g_knowledge", loaded_files="shader-structure.md", status="PARTIAL")
    assert r["status"] == "DENIED" and r["error"] == "G15_INVALID_STATUS", f"got {r}"
    print(f"  ✅ DENIED: 缺高优先级 / PARTIAL 已废弃")


async def test_retired_gates():
    print("\n── 退役门禁 → GATE_NOT_IN_RECIPE (带退役提示) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    for gid, kw in (("g_mode", {"mode": "Production"}),
                    ("g_script", {"decision": "NONE"}),
                    ("g_file", {"file_type": ".shader"}),):
        r = await call("gate_pass", gate_id=gid, **kw)
        assert r["status"] == "DENIED" and r["error"] == "GATE_NOT_IN_RECIPE", f"{gid}: {r}"
        assert "退役" in r["hint"], f"{gid}: 应带退役提示: {r['hint']}"
    print(f"  ✅ 3 个退役门禁均拒绝且提示退役原因")


async def test_norm_block():
    print("\n── 后果验证: 无 Shader 声明的 .shader (denied) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    r = await call("write_gated", path="tmp/test_norm.shader", content="// 这不是一个 shader")
    assert r["status"] == "DENIED" and r["error"] == "NORM_VIOLATION", f"got {r}"
    assert r["violations"][0]["id"] == "shader-decl", f"got {r}"
    print(f"  ✅ DENIED: NORM_VIOLATION ({r['violations'][0]['name']})")

    print("\n── 后果验证: 合法最小 shader (OK) ──")
    r = await call("write_gated", path="tmp/test_ok.shader", content=GOOD_SHADER)
    assert r["status"] == "OK", f"expected OK, got {r}"
    cleanup("test_ok.shader")
    print("  ✅ OK: 合法 shader 放行")


async def test_norm_added_line():
    print("\n── 后果验证: 新增 #region 行 (denied, 仅新增行) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    # 首次写入合法内容
    r = await call("write_gated", path="tmp/test_region.cs", content="public class A { }")
    assert r["status"] == "OK", f"expected OK, got {r}"
    # 重写加入 #region → 新增行违规
    r = await call("write_gated", path="tmp/test_region.cs",
                   content="#region Lifecycle\npublic class A { }\n#endregion")
    assert r["status"] == "DENIED" and r["error"] == "NORM_VIOLATION", f"got {r}"
    assert r["violations"][0]["id"] == "region-added", f"got {r}"
    cleanup("test_region.cs")
    print(f"  ✅ DENIED: 新增行 #region 拦截 ({r['violations'][0]['lines']})")

    print("\n── 后果验证: 新增 // ==== 分隔线 (warning 不阻断) ──")
    r = await call("write_gated", path="tmp/test_warn.cs",
                   content="public class B { }\n// ============")
    assert r["status"] == "OK", f"expected OK, got {r}"
    assert r.get("warnings") and r["warnings"][0]["id"] == "divider-added", f"got {r}"
    cleanup("test_warn.cs")
    print(f"  ✅ OK + warning: 分隔线提示不阻断")


async def test_annotations_non_blocking():
    print("\n── 注解: 非法 script_decision 不阻断, 记录校验结果 ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    r = await call("write_gated", path="tmp/test_ann.txt", content="x",
                   script_decision="USE nope.cs", file_type=".txt")
    assert r["status"] == "OK", f"expected OK, got {r}"
    assert r["annotations"]["script_decision"]["status"] == "DENIED", f"got {r}"
    cleanup("test_ann.txt")
    print("  ✅ OK + 注解记录校验失败（非阻塞）")


async def test_no_gates_denied():
    print("\n── 未过门禁写入 (denied) ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    r = await call("write_gated", path="tmp/test_deny.txt", content="x")
    assert r["status"] == "DENIED", f"got {r}"
    r = await call("write_gated", path="/etc/hosts", content="x")
    assert r["status"] == "DENIED", f"got {r}"
    print(f"  ✅ DENIED: 缺门禁 / 非法路径")


async def test_restart_recovery():
    """进程重启恢复 — MCP server 空闲回收重启后, 配方/门禁从 state.json 恢复."""
    print("\n── 重启恢复: state.json 持久化 (子进程模拟) ──")
    STATE_FILE = os.path.join(ROOT, "state.json")
    if os.path.isfile(STATE_FILE):
        os.remove(STATE_FILE)
    env = dict(os.environ, PYTHONPATH=ROOT)

    # 子进程 1: 声明配方 + 过完整链 → 写 state.json
    p1 = subprocess.run([sys.executable, "-c", (
        "import sys; sys.path.insert(0, %r);"
        "from gate_center import state;"
        "state.set_recipe('Production');"
        "state.pass_gate('g_entry', agent='unity-developer');"
        "state.pass_gate('g_knowledge', loaded_files=%r, status='COMPLETE');"
    ) % (ROOT, HIGH_PRIO)], capture_output=True, text=True, env=env)
    assert p1.returncode == 0, f"p1 failed: {p1.stderr}"
    assert os.path.isfile(STATE_FILE), "state.json 应已写入"

    # 子进程 2: 全新进程 = 模拟 server 重启 → 状态应自动恢复
    p2 = subprocess.run([sys.executable, "-c", (
        "import sys, json; sys.path.insert(0, %r);"
        "from gate_center import state;"
        "print(json.dumps({'recipe': state.recipe, 'passed': sorted(state.passed),"
        " 'ok': state.can_write()['status']}))"
    ) % ROOT], capture_output=True, text=True, env=env)
    assert p2.returncode == 0, f"p2 failed: {p2.stderr}"
    out = json.loads(p2.stdout.strip().splitlines()[-1])
    assert out["recipe"] == "Production", f"recipe 未恢复: {out}"
    assert out["passed"] == ["g_entry", "g_knowledge"], f"passed 未恢复: {out}"
    assert out["ok"] == "OK", f"恢复后应可直接写入: {out}"
    print("  ✅ 重启后 recipe/passed 恢复, can_write OK")

    # gate_reset 应清掉 state.json
    p3 = subprocess.run([sys.executable, "-c", (
        "import sys, os; sys.path.insert(0, %r);"
        "from gate_center import state, STATE_FILE;"
        "state.reset();"
        "print(os.path.isfile(STATE_FILE))"
    ) % ROOT], capture_output=True, text=True, env=env)
    assert p3.returncode == 0, f"p3 failed: {p3.stderr}"
    assert p3.stdout.strip().splitlines()[-1] == "False", f"gate_reset 后 state.json 应删除: {p3.stdout}"
    print("  ✅ gate_reset 显式清空持久化")


async def test_atomic_write():
    """原子写: 同目录隐藏临时文件 + rename — 无 .uetmp 残留, 内容完整."""
    print("\n── 原子写: 无中间态临时文件残留 ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    target = os.path.join(TMP, "test_atomic.shader")
    if os.path.isfile(target):
        os.remove(target)
    r = await call("write_gated", path="tmp/test_atomic.shader", content=GOOD_SHADER)
    assert r["status"] == "OK", f"expected OK, got {r}"
    with open(target, encoding="utf-8") as f:
        assert f.read() == GOOD_SHADER, "内容应完整一致"
    residue = os.path.join(TMP, ".test_atomic.shader.uetmp")
    assert not os.path.isfile(residue), f"临时文件残留: {residue}"
    cleanup("test_atomic.shader")
    print("  ✅ OK: 内容完整, 无 .uetmp 残留")


async def test_cli_check_norm():
    print("\n── check_norm.py CLI (Codex 对等通道) ──")
    good = os.path.join(TMP, "test_cli_ok.shader")
    bad = os.path.join(TMP, "test_cli_bad.shader")
    with open(good, "w", encoding="utf-8") as f:
        f.write(GOOD_SHADER)
    with open(bad, "w", encoding="utf-8") as f:
        f.write("// 无 Shader 声明")
    env = dict(os.environ, PYTHONPATH=ROOT)
    r_good = subprocess.run([sys.executable, os.path.join(ROOT, "validation", "check_norm.py"), good],
                            capture_output=True, text=True, env=env)
    r_bad = subprocess.run([sys.executable, os.path.join(ROOT, "validation", "check_norm.py"), bad],
                           capture_output=True, text=True, env=env)
    assert r_good.returncode == 0, f"合法文件应 exit 0: {r_good.stdout} {r_good.stderr}"
    assert r_bad.returncode == 1, f"违规文件应 exit 1: {r_bad.stdout} {r_bad.stderr}"
    assert "shader-decl" in r_bad.stdout or "Shader 声明" in r_bad.stdout
    cleanup("test_cli_ok.shader")
    cleanup("test_cli_bad.shader")
    print("  ✅ CLI: 合法 exit 0 / 违规 exit 1")


def test_deletion_classify():
    """deletion.classify: tracked-clean / tracked-dirty / untracked / missing + GUID 引用.

    在隔离的临时 git 仓库里验 —— 不碰本仓库的工作区与索引。
    """
    print("\n── 删除后果验证: 工作区分类 (隔离临时仓库) ──")
    import tempfile
    from validation.deletion import classify, read_guid, guid_referenced_elsewhere

    with tempfile.TemporaryDirectory() as repo:
        def git(*args):
            subprocess.run(("git", "-c", "user.email=t@t", "-c", "user.name=t", *args),
                           cwd=repo, capture_output=True, text=True, check=True)

        git("init", "-q")
        for name in ("clean.txt", "dirty.txt"):
            with open(os.path.join(repo, name), "w", encoding="utf-8") as f:
                f.write("x")
        git("add", "-A")
        git("commit", "-qm", "init")
        with open(os.path.join(repo, "dirty.txt"), "w", encoding="utf-8") as f:
            f.write("changed")

        r = classify(repo, ["clean.txt", "dirty.txt", "nope.txt"])
        assert r["clean.txt"]["state"] == "tracked-clean", r
        assert r["dirty.txt"]["state"] == "tracked-dirty", r
        assert r["nope.txt"]["state"] == "missing", r
        assert r["clean.txt"]["recoverable"], f"tracked-clean 应可恢复: {r}"
        assert not r["dirty.txt"]["recoverable"], f"tracked-dirty 不可恢复: {r}"

        # GUID 引用: Assets/B.asset 引用了 Assets/A.meta 的 guid
        guid = "0123456789abcdef0123456789abcdef"
        os.makedirs(os.path.join(repo, "Assets"))
        with open(os.path.join(repo, "Assets", "A.meta"), "w", encoding="utf-8") as f:
            f.write(f"fileFormatVersion: 2\nguid: {guid}\n")
        with open(os.path.join(repo, "Assets", "B.asset"), "w", encoding="utf-8") as f:
            f.write(f"m_Script: {{guid: {guid}, type: 3}}\n")

        assert read_guid(os.path.join(repo, "Assets", "A.meta")) == guid
        assert read_guid(os.path.join(repo, "clean.txt")) == "", "非 .meta 应返回空串"
        refs = guid_referenced_elsewhere(repo, "Assets/A.meta", guid)
        assert refs == ["Assets/B.asset"], f"应只报出引用方: {refs}"
        assert guid_referenced_elsewhere(repo, "Assets/B.asset", guid) == ["Assets/A.meta"], \
            "无引用时应剔除自身"
    print("  ✅ 四分状态 + recoverable 标记 + GUID 引用（剔自身）判定正确")


async def test_delete_gated():
    print("\n── delete_gated: 门禁 / 作用域 / reason / 审计 / 幂等 ──")
    target = os.path.join(TMP, "test_delete.txt")
    with open(target, "w", encoding="utf-8") as f:
        f.write("x")

    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")

    # 未过门禁 → DENIED（与 write_gated 同一道闸）
    r = await call("delete_gated", paths=["tmp/test_delete.txt"], reason="单元测试")
    assert r["status"] == "DENIED" and r["error"] == "GATE_NOT_PASSED", f"got {r}"

    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")

    # 作用域（与 write_gated 同一函数）
    r = await call("delete_gated", paths=["/etc/hosts"], reason="越界测试")
    assert r["status"] == "DENIED" and r["error"] == "PATH_NOT_ALLOWED", f"got {r}"

    # reason 必填
    r = await call("delete_gated", paths=["tmp/test_delete.txt"], reason="   ")
    assert r["status"] == "DENIED" and r["error"] == "MISSING_REASON", f"got {r}"

    # 正常删除: tmp/ 未入库 → 放行且标注不可恢复
    r = await call("delete_gated", paths=["tmp/test_delete.txt"], reason="单元测试清理")
    assert r["status"] == "OK" and r["count"] == 1, f"got {r}"
    assert not os.path.isfile(target), "文件应已删除"
    assert r["deleted"][0]["state"] == "untracked", f"got {r}"
    assert r["deleted"][0]["recoverable"] is False, f"untracked 应标注不可恢复: {r}"

    # 审计落 deletes
    st = await call("gate_status")
    assert st["deletes"] and st["deletes"][-1]["count"] == 1, f"审计未记录: {st.get('deletes')}"
    assert st["deletes"][-1]["reason"] == "单元测试清理", f"审计应含 reason: {st['deletes'][-1]}"

    # 幂等: 重复执行同一清单 → missing 跳过, 不报错
    r = await call("delete_gated", paths=["tmp/test_delete.txt"], reason="重复执行")
    assert r["status"] == "OK" and r["count"] == 0, f"got {r}"

    # gate_reset 清空删除审计
    await call("gate_reset")
    st = await call("gate_status")
    assert st["deletes"] == [], f"gate_reset 应清空删除审计: {st['deletes']}"
    print("  ✅ 门禁 / 作用域 / reason 必填 / 删除 / 审计 / 幂等 / 重置 均正确")


async def test_cli_delete_gated():
    """delete_gated.py CLI: 与 MCP 同一道门禁、同一套判定、同一份落盘审计。"""
    print("\n── delete_gated.py CLI (Bash 通道) ──")
    cli = os.path.join(ROOT, "validation", "delete_gated.py")
    env = dict(os.environ, PYTHONPATH=ROOT)
    audit = os.path.join(ROOT, "deletes.jsonl")
    target = os.path.join(TMP, "cli_del.txt")

    def run(*args):
        return subprocess.run([sys.executable, cli, *args],
                              capture_output=True, text=True, env=env)

    # 用法: 无路径 / 无 reason → exit 2
    assert run("--reason", "x").returncode == 2, "无路径应 exit 2"
    assert run("tmp/cli_del.txt").returncode == 2, "无 reason 应 exit 2"

    # 门禁未过（前序用例已 gate_reset → state.json 不存在）→ 与 MCP 同一道闸
    r = run("--reason", "单元测试", "tmp/cli_del.txt")
    assert r.returncode == 1 and "NO_RECIPE" in r.stdout, f"got {r.stdout}"

    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")

    # 作用域（与 MCP 同一个 validate_path）
    r = run("--reason", "越界", "/etc/hosts")
    assert r.returncode == 1 and "PATH_NOT_ALLOWED" in r.stdout, f"got {r.stdout}"

    with open(target, "w", encoding="utf-8") as f:
        f.write("x")

    # --dry-run: 判定可删但不执行
    r = run("--reason", "干跑", "--dry-run", "tmp/cli_del.txt")
    assert r.returncode == 0 and json.loads(r.stdout)["dry_run"] is True, f"got {r.stdout}"
    assert os.path.isfile(target), "--dry-run 不应删除"

    # 实删 + 落盘审计（删除不留产物，日志是唯一痕迹）
    before = os.path.getsize(audit) if os.path.isfile(audit) else 0
    r = run("--reason", "CLI 单元测试清理", "tmp/cli_del.txt")
    assert r.returncode == 0 and json.loads(r.stdout)["count"] == 1, f"got {r.stdout}"
    assert not os.path.isfile(target), "文件应已删除"
    assert os.path.getsize(audit) > before, "应追加审计"
    last = json.loads(open(audit, encoding="utf-8").read().strip().splitlines()[-1])
    assert last["channel"] == "cli", f"审计应标 channel=cli: {last}"
    assert last["reason"] == "CLI 单元测试清理", f"审计应含 reason: {last}"
    assert last["paths"] == ["tmp/cli_del.txt"], f"审计应含路径: {last}"

    # 幂等: 重复执行同一清单 → missing 跳过
    r = run("--reason", "重复", "tmp/cli_del.txt")
    assert r.returncode == 0 and json.loads(r.stdout)["count"] == 0, f"got {r.stdout}"

    await call("gate_reset")
    print("  ✅ 用法 / 门禁同源 / 作用域 / dry-run / 删除 / 落盘审计 / 幂等 均正确")


async def main():
    print("=" * 50)
    print("  Gate Tests (consequence-verification v2)")
    print("=" * 50)

    print("\n── Self-check (registry invariants) ──")
    self_check()

    await test_meta_bypass_boundary()
    await test_production_chain()
    await test_knowledge_fabricated()
    await test_knowledge_reference_impl()
    await test_knowledge_missing_high_prio()
    await test_retired_gates()
    await test_norm_block()
    await test_norm_added_line()
    await test_annotations_non_blocking()
    await test_no_gates_denied()
    await test_atomic_write()
    await test_cli_check_norm()
    test_deletion_classify()
    await test_delete_gated()
    await test_cli_delete_gated()
    await test_restart_recovery()   # 放最后: 子进程管理自己的 state.json, 不干扰进程内用例

    print("\n" + "=" * 50)
    print("  All tests passed ✓")
    print("=" * 50)


if __name__ == "__main__":
    asyncio.run(main())
