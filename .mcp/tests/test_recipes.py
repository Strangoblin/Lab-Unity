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


def test_moving_plan():
    """moving.plan 预检 + 与 deletion 的**分歧点**（隔离临时 git 仓库）.

    分歧点才是本模块存在的理由：同一份 tracked-dirty 输入，
    deletion 拦（单独删它，内容只剩历史版本）、moving 放（内容只是换了位置）。
    两条断言成对写，缺一条这个测试就退化成「移动能跑」。
    """
    print("\n── 移动后果验证: 预检 + 与 deletion 的分歧 (隔离临时仓库) ──")
    import tempfile
    from validation.moving import plan, find_guid_owners
    from validation.deletion import evaluate

    with tempfile.TemporaryDirectory() as repo:
        def git(*args):
            subprocess.run(("git", "-c", "user.email=t@t", "-c", "user.name=t", *args),
                           cwd=repo, capture_output=True, text=True, check=True)

        def put(rel, text):
            full = os.path.join(repo, rel)
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "w", encoding="utf-8") as f:
                f.write(text)

        def blocked_ids(moves):
            return [b["error"] for b in plan(repo, moves)["blocked"]]

        git("init", "-q")
        guid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        put("Assets/A/x.shader", 'Shader "X" {}\n')
        put("Assets/A/x.shader.meta", f"fileFormatVersion: 2\nguid: {guid}\n")
        git("add", "-A")
        git("commit", "-qm", "init")

        # ── 分歧点：同一份输入，两边判定相反 ──
        put("Assets/A/x.shader", 'Shader "X" {}\n// 未提交改动\n')   # → tracked-dirty
        ev = evaluate(repo, [("Assets/A/x.shader", os.path.join(repo, "Assets/A/x.shader"))])
        assert ev["blocked"] and ev["blocked"][0]["state"] == "tracked-dirty", \
            f"deletion 应拦 tracked-dirty: {ev['blocked']}"
        mv = plan(repo, [("Assets/A/x.shader", "Assets/B/x.shader")])
        assert mv["blocked"] == [], f"moving 应放行同一份输入: {mv['blocked']}"
        assert mv["pairs"][0]["guid"] == guid, f"应带出 GUID: {mv['pairs']}"
        assert mv["pairs"][0]["meta_from"] == "Assets/A/x.shader.meta", \
            f"应配对 .meta: {mv['pairs']}"

        # ── 预检各条 ──
        assert blocked_ids([("Assets/A/x.shader", "Assets/A/x.shader")]) == ["SAME_PATH"]
        assert blocked_ids([("Assets/A/nope.shader", "Assets/B/n.shader")]) == ["MISSING_SOURCE"]
        put("Assets/B/x.shader", 'Shader "B" {}\n')
        assert blocked_ids([("Assets/A/x.shader", "Assets/B/x.shader")]) == ["TARGET_EXISTS"]
        # 逐对报：重复的每一对都是一次独立的移动意图，各报一条（不是去重成一条）
        assert blocked_ids([("Assets/A/x.shader", "Assets/B/1.shader"),
                            ("Assets/A/x.shader", "Assets/B/2.shader")]) == \
            ["DUPLICATE_SOURCE", "DUPLICATE_SOURCE"]
        assert blocked_ids([("Assets/A/x.shader", "Assets/B/3.shader"),
                            ("Assets/A/x.shader.meta", "Assets/B/3.shader")]) == \
            ["DUPLICATE_TARGET", "DUPLICATE_TARGET"]
        # 链式：一个的目标是另一个的源 → 顺序有歧义。链条成员各报一条，
        # 且必须报 CHAINED_MOVE 而不是 MISSING_SOURCE（后者会把人引向错误方向）
        assert blocked_ids([("Assets/A/x.shader", "Assets/B/y.shader"),
                            ("Assets/B/y.shader", "Assets/B/z.shader")]) == \
            ["CHAINED_MOVE", "CHAINED_MOVE"]

        # ── .meta 目标已存在 ──
        put("Assets/B/y.shader.meta", "fileFormatVersion: 2\nguid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n")
        assert blocked_ids([("Assets/A/x.shader", "Assets/B/y.shader")]) == ["META_TARGET_EXISTS"]

        # ── GUID 已被别人声明 → 拦（同 GUID 两份 = Unity 报错）──
        other = "Assets/C/other.shader.meta"
        put(other, f"fileFormatVersion: 2\nguid: {guid}\n")
        mv = plan(repo, [("Assets/A/x.shader", "Assets/B/z.shader")])
        assert [b["error"] for b in mv["blocked"]] == ["GUID_TAKEN"], f"got {mv['blocked']}"
        assert mv["blocked"][0]["owners"] == [other], f"应报出占用方: {mv['blocked'][0]}"

        # find_guid_owners 只认「声明」该 GUID 的 .meta，且剔除自身
        os.remove(os.path.join(repo, other))
        assert find_guid_owners(repo, guid, exclude=["Assets/A/x.shader.meta"]) == [], \
            "无占用时应为空"
        assert find_guid_owners(repo, guid, exclude=[]) == ["Assets/A/x.shader.meta"], \
            "不排除自身时应报出它"
    print("  ✅ 预检各条 + .meta 配对 + GUID 占用；tracked-dirty 上 deletion 拦 / moving 放")


async def test_move_gated():
    """move_gated: 门禁 / 作用域 / reason / 形状 / 正文与 .meta 保真 / 审计落盘."""
    print("\n── move_gated: 门禁 / 作用域 / 形状 / 保真 / 审计 ──")
    src = os.path.join(TMP, "mv_src.shader")
    src_meta = src + ".meta"
    dst = os.path.join(TMP, "mv_dst.shader")
    dst_meta = dst + ".meta"
    body = 'Shader "Test/Mv"\n{\n    SubShader { Pass { HLSLPROGRAM\n    ENDHLSL } }\n}\n'
    meta_body = "fileFormatVersion: 2\nguid: cccccccccccccccccccccccccccccccc\n"
    for p in (src, src_meta, dst, dst_meta):
        if os.path.isfile(p):
            os.remove(p)
    with open(src, "w", encoding="utf-8") as f:
        f.write(body)
    with open(src_meta, "w", encoding="utf-8") as f:
        f.write(meta_body)

    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")

    # 未过门禁 → DENIED（与 write/delete 同一道闸）
    r = await call("move_gated", moves=[{"from": "tmp/mv_src.shader", "to": "tmp/mv_dst.shader"}],
                   reason="单元测试")
    assert r["status"] == "DENIED" and r["error"] == "GATE_NOT_PASSED", f"got {r}"

    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")

    # reason 必填
    r = await call("move_gated", moves=[{"from": "tmp/mv_src.shader", "to": "tmp/mv_dst.shader"}],
                   reason="  ")
    assert r["status"] == "DENIED" and r["error"] == "MISSING_REASON", f"got {r}"

    # 空清单
    r = await call("move_gated", moves=[], reason="空")
    assert r["status"] == "DENIED" and r["error"] == "EMPTY_MOVES", f"got {r}"

    # 入参形状：缺键当场指出，不猜
    r = await call("move_gated", moves=[{"from": "tmp/mv_src.shader"}], reason="缺 to")
    assert r["status"] == "DENIED" and r["error"] == "BAD_MOVE_SHAPE", f"got {r}"
    # 非对象由 MCP schema 层（list[dict] 契约）直接拒 —— 到不了函数体，
    # 这是比 BAD_MOVE_SHAPE 更强的保证，钉住它免得日后签名放宽后悄悄失守
    rejected = False
    try:
        await call("move_gated", moves=["tmp/mv_src.shader"], reason="不是对象")
    except Exception:
        rejected = True
    assert rejected, 'moves 的元素类型应由 schema 层守住（[{"from","to"}]）'

    # 作用域：源与目标都过同一个 validate_path
    r = await call("move_gated", moves=[{"from": "/etc/hosts", "to": "tmp/x"}], reason="越界源")
    assert r["status"] == "DENIED" and r["error"] == "PATH_NOT_ALLOWED", f"got {r}"
    r = await call("move_gated", moves=[{"from": "tmp/mv_src.shader", "to": "/etc/x"}], reason="越界目标")
    assert r["status"] == "DENIED" and r["error"] == "PATH_NOT_ALLOWED", f"got {r}"

    # 正常移动：正文与 .meta 逐字保真，源连同 .meta 一并消失
    audit = os.path.join(ROOT, "moves.jsonl")
    before = os.path.getsize(audit) if os.path.isfile(audit) else 0
    r = await call("move_gated", moves=[{"from": "tmp/mv_src.shader", "to": "tmp/mv_dst.shader"}],
                   reason="单元测试移动")
    assert r["status"] == "OK" and r["count"] == 1, f"got {r}"
    assert not os.path.exists(src) and not os.path.exists(src_meta), "源与源 .meta 都应消失"
    assert open(dst, encoding="utf-8").read() == body, "正文应逐字保真"
    assert open(dst_meta, encoding="utf-8").read() == meta_body, ".meta 应逐字保真"

    # 审计落盘：移动不留配对痕迹于文件系统，日志是唯一来源
    assert os.path.getsize(audit) > before, "应追加移动审计"
    last = json.loads(open(audit, encoding="utf-8").read().strip().splitlines()[-1])
    assert last["kind"] == "move" and last["count"] == 1, f"审计应记移动: {last}"
    assert last["moves"][0]["from"] == "tmp/mv_src.shader", f"审计应记 src→dst: {last}"
    assert last["moves"][0]["to"] == "tmp/mv_dst.shader", f"审计应记 src→dst: {last}"

    st = await call("gate_status")
    assert st["moves"] and st["moves"][-1]["count"] == 1, f"gate_status 应含移动审计: {st}"

    # 幂等方向：源已不在 → 预检拦（不是静默跳过，移动没有「重复执行」语义）
    r = await call("move_gated", moves=[{"from": "tmp/mv_src.shader", "to": "tmp/mv_dst.shader"}],
                   reason="重复")
    assert r["status"] == "DENIED" and r["blocked"][0]["error"] == "MISSING_SOURCE", f"got {r}"

    for p in (dst, dst_meta):
        os.remove(p)
    await call("gate_reset")
    print("  ✅ 门禁 / 作用域 / reason / 形状 / 保真 / 落盘审计 / 重置 均正确")


def test_placement_check():
    """check_placement: 受管扩展名 / 落点 / 新旧强度 / category 注解一致性."""
    print("\n── 文件落点: 受管扩展名 / 新文件 error、既有 warning ──")
    from validation.project_paths import check_placement

    def ids(r):
        return ([v["id"] for v in r["errors"]], [v["id"] for v in r["warnings"]])

    # 非受管扩展名一律放行 —— .meta 要能跟着资产走，导入资产不该被路径规则干预
    for p in ("Assets/Mine/x.meta", "Assets/Mine/Data/c.asset",
              "Assets/Mine/Checkers/a.png", "Assets/Mine/Special/SubGraph/s.shadersubgraph"):
        assert ids(check_placement(p, is_new=True)) == ([], []), p

    # 现存形态一律放行（规则是照现实写的，不是照理想）：包括根下直放、嵌套子目录
    for p in ("Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl",
              "Assets/Mine/Shaders/PostProcess/SSGI/SSGI_Content_Skeleton.md",
              "Assets/Mine/Scripts/TestAuto.cs",
              "Assets/Mine/Scripts/CurveGenerator/Editor/CurveBakeEditor.cs",
              "Assets/Mine/Scripts/Picker/Picker.shader",
              "Assets/Mine/Special/HLSL/BlurFunction.hlsl",
              "Assets/Mine/Effects/Stars/Stars.shader"):
        assert ids(check_placement(p, is_new=True)) == ([], []), p

    # 扔在 Mine 根下 / 扔进非代码区：新文件 error，已有文件降为 warning
    # （已有的落点是历史决定，硬拦会让人改不动文件，反而堵死修正入口）
    assert ids(check_placement("Assets/Mine/foo.shader", is_new=True)) == (["placement-root"], [])
    assert ids(check_placement("Assets/Mine/foo.shader", is_new=False)) == ([], ["placement-root"])
    assert ids(check_placement("Assets/Mine/Data/x.cs", is_new=True)) == (["placement-root"], [])
    assert ids(check_placement("Assets/Mine/notes.md", is_new=True)) == (["placement-root"], [])

    # category 注解与路径不符 → warning（注解此前是自由文本，记录不校验）
    r = check_placement("Assets/Mine/Shaders/Render/Water/Water.shader",
                        is_new=True, category="PostProcess")
    assert ids(r) == ([], ["placement-category"]), r
    assert ids(check_placement("Assets/Mine/Shaders/PostProcess/SSGI/AO/AO.shader",
                               is_new=True, category="PostProcess")) == ([], [])
    print("  ✅ 受管扩展名 / 落点 / 新旧强度 / category 一致性 均正确")


async def test_write_gated_placement_wired():
    """落点校验是否真接进了 write_gated —— 规则写了不接=没有（本层休眠过一次）."""
    print("\n── write_gated 落点接线 ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")

    # 新受管文件扔在 Assets/Mine 根下 → DENIED，且**不落盘**（被拒的写入代价是零）
    probe = os.path.join(os.path.dirname(ROOT), "Assets", "Mine", "_placement_probe.shader")
    r = await call("write_gated", path="Assets/Mine/_placement_probe.shader", content=GOOD_SHADER)
    assert r["status"] == "DENIED" and r["error"] == "NORM_VIOLATION", f"got {r}"
    assert [v["id"] for v in r["violations"]] == ["placement-root"], f"got {r['violations']}"
    assert not os.path.exists(probe), "被拒的写入不应留下文件"

    # 非受管扩展名同位置放行 —— .meta 是 Unity 生成的，不能拦
    r = await call("write_gated", path="Assets/Mine/_placement_probe.meta",
                   content="fileFormatVersion: 2\nguid: dddd\n")
    assert r["status"] == "OK", f"非受管扩展名应放行: {r}"
    os.remove(os.path.join(os.path.dirname(ROOT), "Assets", "Mine", "_placement_probe.meta"))

    await call("gate_reset")
    print("  ✅ 落点校验已接线；被拒写入零落盘；非受管扩展名不受影响")


async def test_cli_move_gated():
    """move_gated.py CLI: 与 MCP 同一道门禁、同一套判定、同一套执行、同一份落盘审计。

    这条通道存在的理由和删除 CLI 一样：**MCP 工具表按会话冻结**（服务器跑新代码，
    工具却调不到，需 /mcp 重连）。移动同理，故补对称通道。
    """
    print("\n── move_gated.py CLI (Bash 通道) ──")
    cli = os.path.join(ROOT, "validation", "move_gated.py")
    env = dict(os.environ, PYTHONPATH=ROOT)
    audit = os.path.join(ROOT, "moves.jsonl")
    src = os.path.join(TMP, "cli_mv.shader")
    dst = os.path.join(TMP, "cli_mv_out.shader")

    def run(*args):
        return subprocess.run([sys.executable, cli, *args],
                              capture_output=True, text=True, env=env)

    # 用法: 无路径 / 无 reason / 路径奇数个 → exit 2
    assert run("--reason", "x").returncode == 2, "无路径应 exit 2"
    assert run("tmp/cli_mv.shader").returncode == 2, "无 reason 应 exit 2"
    r = run("--reason", "奇数", "tmp/a", "tmp/b", "tmp/c")
    assert r.returncode == 2 and "ODD_PATH_COUNT" in r.stdout, f"got {r.stdout}"

    # 门禁未过（前序用例已 gate_reset）→ 与 MCP 同一道闸，同一份 state.json
    r = run("--reason", "单元测试", "tmp/cli_mv.shader", "tmp/cli_mv_out.shader")
    assert r.returncode == 1 and "NO_RECIPE" in r.stdout, f"got {r.stdout}"

    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")

    # 作用域（与 MCP 同一个 validate_path）：源与目标都要在作用域内
    r = run("--reason", "越界源", "/etc/hosts", "tmp/x")
    assert r.returncode == 1 and "PATH_NOT_ALLOWED" in r.stdout, f"got {r.stdout}"
    r = run("--reason", "越界目标", "tmp/cli_mv.shader", "/etc/x")
    assert r.returncode == 1 and "PATH_NOT_ALLOWED" in r.stdout, f"got {r.stdout}"

    with open(src, "w", encoding="utf-8") as f:
        f.write(GOOD_SHADER)
    with open(src + ".meta", "w", encoding="utf-8") as f:
        f.write("fileFormatVersion: 2\nguid: eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n")

    # --dry-run: 判定可移但不执行
    r = run("--reason", "干跑", "--dry-run", "tmp/cli_mv.shader", "tmp/cli_mv_out.shader")
    assert r.returncode == 0 and json.loads(r.stdout)["dry_run"] is True, f"got {r.stdout}"
    assert os.path.isfile(src), "--dry-run 不应移动"

    # 实移 + 落盘审计（正文与 .meta 都要跟过去）
    before = os.path.getsize(audit) if os.path.isfile(audit) else 0
    r = run("--reason", "CLI 单元测试移动", "tmp/cli_mv.shader", "tmp/cli_mv_out.shader")
    assert r.returncode == 0 and json.loads(r.stdout)["count"] == 1, f"got {r.stdout}"
    assert not os.path.exists(src) and not os.path.exists(src + ".meta"), "源与源 .meta 都应消失"
    assert open(dst, encoding="utf-8").read() == GOOD_SHADER, "正文应保真"
    assert os.path.isfile(dst + ".meta"), ".meta 应成对搬运"
    assert os.path.getsize(audit) > before, "应追加审计"
    last = json.loads(open(audit, encoding="utf-8").read().strip().splitlines()[-1])
    assert last["channel"] == "cli" and last["kind"] == "move", f"审计应标 channel=cli: {last}"
    assert last["reason"] == "CLI 单元测试移动", f"审计应含 reason: {last}"
    assert last["argv"], "审计应记 argv 便于溯源"

    # 幂等方向：源已不在 → 预检拦（移动没有「重复执行」语义，与删除不同）
    r = run("--reason", "重复", "tmp/cli_mv.shader", "tmp/cli_mv_out.shader")
    assert r.returncode == 1 and "MISSING_SOURCE" in r.stdout, f"got {r.stdout}"

    for p in (dst, dst + ".meta"):
        if os.path.isfile(p):
            os.remove(p)
    await call("gate_reset")
    print("  ✅ 用法 / 门禁同源 / 作用域 / dry-run / 移动 / .meta 配对 / 落盘审计 均正确")


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
    test_moving_plan()
    test_placement_check()
    await test_write_gated_placement_wired()
    await test_delete_gated()
    await test_cli_delete_gated()
    await test_move_gated()
    await test_cli_move_gated()
    await test_restart_recovery()   # 放最后: 子进程管理自己的 state.json, 不干扰进程内用例

    print("\n" + "=" * 50)
    print("  All tests passed ✓")
    print("=" * 50)


if __name__ == "__main__":
    asyncio.run(main())
