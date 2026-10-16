"""Gate tests — 后果验证门禁 (v3: 判定核 + 策略数据 + 摩擦账本).

链统一为 [g_entry, g_knowledge]:
  g_knowledge = 知识证据（真实文件解析）
  write_gated  = 内容规范后果验证（error 阻断 / warning 提示）+ 注解审计
"""
import sys, json, asyncio, os, subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # .mcp/
sys.path.insert(0, ROOT)

# 测试写自己的摩擦账本，不污染生产账本（否则 E5 报告被测试噪声淹没）
import tempfile
TEST_LEDGER = os.path.join(tempfile.gettempdir(), "mcp-friction-test.jsonl")
os.environ["MCP_FRICTION_FILE"] = TEST_LEDGER

# 测试钉住「严格」策略：这里断言的是**机制**（该拦就拦），不是当前 policy 取值 ——
# 否则每次调 policy 都要改一批断言。已发布策略的取值由 test_shipped_policy 单独守。
SHIPPED_POLICY = os.path.join(ROOT, "policy.json")
STRICT_POLICY = os.path.join(tempfile.gettempdir(), "mcp-policy-strict.json")
with open(STRICT_POLICY, "w", encoding="utf-8") as _f:
    json.dump({"checks": {
        "ceremony.recipe_gate": {"level": "block"},
        "ceremony.knowledge_high_priority": {"level": "block"},
        "ceremony.knowledge_resolvable": {"level": "block"},
    },
    "workspace": {"required": True, "applies_to": ["Assets/Mine/"]},
    }, _f)
os.environ["MCP_POLICY_FILE"] = STRICT_POLICY


class policy_env:
    """临时切换策略文件；退出时**还原**（不是 pop —— pop 会让后续用例退回已发布策略）。"""

    def __init__(self, path):
        self.path = path

    def __enter__(self):
        from validation import policy
        self._prev = os.environ.get("MCP_POLICY_FILE")
        os.environ["MCP_POLICY_FILE"] = self.path
        policy.reload()
        return policy

    def __exit__(self, *exc):
        from validation import policy
        if self._prev is None:
            os.environ.pop("MCP_POLICY_FILE", None)
        else:
            os.environ["MCP_POLICY_FILE"] = self._prev
        policy.reload()
        return False


def shipped_policy() -> dict:
    """读**已发布**的 policy.json（不经 env 覆盖）。"""
    with open(SHIPPED_POLICY, encoding="utf-8") as f:
        return json.load(f)

from gate_center import RECIPES, GATE_REGISTRY, RETIRED_GATES
from gates.g_entry import ALLOWED_AGENTS
from validation.project_paths import validate_path, PROJECT_ROOT

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


async def test_single_recipe_initialization_preserves_entry():
    """Documented workflow: init once, entry then knowledge; re-init is a negative control."""
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    assert (await call("gate_pass", gate_id="g_entry", agent="unity-developer"))["status"] == "OK"
    # Known failure from the former skill: G1 reinitialized after G0, losing entry.
    await call("gate_set_recipe", name="Production")
    denied = await call("gate_pass", gate_id="g_knowledge", status="COMPLETE", loaded_files=HIGH_PRIO)
    assert denied["status"] == "DENIED" and denied["error"] == "PREREQUISITE", denied
    # One initialization and no reset between the two gates preserves the chain.
    assert (await call("gate_pass", gate_id="g_entry", agent="unity-developer"))["status"] == "OK"
    passed = await call("gate_pass", gate_id="g_knowledge", status="COMPLETE", loaded_files=HIGH_PRIO)
    assert passed["status"] == "OK", passed
    await call("gate_reset")
    print("  ✅ single recipe initialization preserves entry; duplicate-init control rejects knowledge")


async def test_meta_bypass_boundary():
    print("\n── Meta 边界: 不进入 Unity MCP ──")
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    r = await call("gate_pass", gate_id="g_entry", agent="meta-developer")
    assert r["status"] == "DENIED" and r["error"] == "G0_FAILED", f"got {r}"
    r = validate_path(".agents/agents/meta-developer/AGENT.md")
    assert r["status"] == "DENIED" and r["error"] == "PATH_NOT_ALLOWED", f"got {r}"
    print("  ✅ meta-developer 与 .agents 路径均不进入 Unity MCP")


def test_validate_path_scope():
    print("\n── validate_path 作用域（Windows 分隔符回归）──")
    # 正向：Assets/Mine 全域放行。35a2fd6（2026-09-18）起前缀字符串是混合分隔符
    # （...\Lab\Assets/Mine），而 commonpath 返回全反斜杠形，字符串相等在 Windows 上
    # 恒为 False → Assets/Mine 全域误判 PATH_NOT_ALLOWED。此前正向写入用例只落在
    # tmp/（前缀无内部正斜杠，恰好通过），故回归长期未被捕获。
    for p in ("Assets/Mine/Scripts/TerrainManager/Plan.md",
              "Assets/Mine/Scripts/TerrainManager.cs.meta",
              "Assets/Mine/Shaders/PostProcess/SSGI/AO/AO.shader",
              "Assets/Mine/Special/HLSL/BlurFunction.hlsl"):
        r = validate_path(p)
        assert r["status"] == "OK", f"{p} 应放行，got {r}"

    # 绝对路径 / 混合分隔符 / 盘符大小写均不改变判定
    abs_ok = os.path.join(PROJECT_ROOT, "Assets", "Mine", "Scripts", "Test.cs")
    for p in (abs_ok, abs_ok.replace("\\", "/"), abs_ok[0].lower() + abs_ok[1:]):
        r = validate_path(p)
        assert r["status"] == "OK", f"{p} 应放行，got {r}"

    # 反向：前缀扩展目录不得被放行（commonpath 语义，非 startswith），越界路径拦截
    for p in ("Assets/MineElsewhere/x.cs", "Assets/Other/x.cs",
              "Assets/Mine/../Other/x.cs", "Library/x.cs"):
        r = validate_path(p)
        assert r["status"] == "DENIED" and r["error"] == "PATH_NOT_ALLOWED", f"{p} 应拦截，got {r}"
    print("  ✅ Assets/Mine 放行 / 前缀扩展与越界拦截 / 分隔符与大小写归一")


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


async def test_assets_mine_prefix_gated_ops():
    print("\n── Assets/Mine 前缀: write/move/delete 端到端 ──")
    # 回归（Codex 2026-09-26 报告）：validate_path 的 Assets/Mine 前缀在 Windows 上
    # 恒判 PATH_NOT_ALLOWED，而此前端到端用例全部只落在 tmp/，三通道在真实前缀上
    # 从未被测过。探针用 '.' 前缀文件名：Unity 忽略导入（与 atomic_io 的隐藏临时
    # 文件同一机制），同轮创建 → 移动 → 删除，无 .meta、无残留。
    probe = "Assets/Mine/Shaders/PostProcess/.gate_probe.shader"
    moved = "Assets/Mine/Shaders/PostProcess/.gate_probe_moved.shader"
    await call("gate_reset")
    await call("gate_set_recipe", name="Production")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    await call("gate_set_workspace", path="Assets/Mine")   # 2026-10-09 起 Assets/Mine 写入须先声明工作目录
    r = await call("write_gated", path=probe, content=GOOD_SHADER)
    assert r["status"] == "OK", f"write_gated 应放行 Assets/Mine, got {r}"
    r = await call("move_gated", moves=[{"from": probe, "to": moved}],
                   reason="门禁回归: Assets/Mine 前缀移动探针")
    assert r["status"] == "OK", f"move_gated 应放行 Assets/Mine, got {r}"
    r = await call("delete_gated", paths=[moved], reason="门禁回归: Assets/Mine 前缀清理探针")
    assert r["status"] == "OK", f"delete_gated 应放行 Assets/Mine, got {r}"
    for p in (probe, moved, probe + ".meta", moved + ".meta"):
        assert not os.path.exists(os.path.join(os.path.dirname(ROOT), p)), f"残留: {p}"
    await call("gate_reset")
    print("  ✅ 三通道在 Assets/Mine 前缀均放行且无残留")


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
        "state.set_workspace('Assets/Mine/Scripts');"
    ) % (ROOT, HIGH_PRIO)], capture_output=True, text=True, env=env)
    assert p1.returncode == 0, f"p1 failed: {p1.stderr}"
    assert os.path.isfile(STATE_FILE), "state.json 应已写入"

    # 子进程 2: 全新进程 = 模拟 server 重启 → 状态应自动恢复
    p2 = subprocess.run([sys.executable, "-c", (
        "import sys, json; sys.path.insert(0, %r);"
        "from gate_center import state;"
        "print(json.dumps({'recipe': state.recipe, 'passed': sorted(state.passed),"
        " 'ok': state.can_write()['status'], 'ws': state.workspace_view()['roots']}))"
    ) % ROOT], capture_output=True, text=True, env=env)
    assert p2.returncode == 0, f"p2 failed: {p2.stderr}"
    out = json.loads(p2.stdout.strip().splitlines()[-1])
    assert out["recipe"] == "Production", f"recipe 未恢复: {out}"
    assert out["passed"] == ["g_entry", "g_knowledge"], f"passed 未恢复: {out}"
    assert out["ok"] == "OK", f"恢复后应可直接写入: {out}"
    assert out["ws"] == ["Assets/Mine/Scripts"], f"工作目录未恢复: {out}"
    print("  ✅ 重启后 recipe/passed/工作目录 恢复, can_write OK")

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
    await call("gate_set_workspace", path="Assets/Mine")   # 声明边界后才轮到落点校验

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


# ════════════════════════════════════════════════════════════════
#  v3 — 策略数据化 / 摩擦账本 / 统一通道
# ════════════════════════════════════════════════════════════════

def test_policy_levels():
    """policy: 覆盖只能放宽；固有级别是地板；缺失文件回落固有（= v2 行为）。"""
    print("\n── v3 策略数据化: 覆盖只放宽 / 缺省即 v2 ──")
    import tempfile
    from validation import policy

    assert policy.level_of("norm.region-added") == "block"
    assert policy.level_of("norm.divider-added") == "warn"
    assert policy.level_of("不存在的检查") == "block", "未知检查默认 block（安全默认）"

    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"checks": {
            "norm.region-added": {"level": "warn"},     # 放宽 → 生效
            "norm.divider-added": {"level": "block"},   # 收紧 → 应被忽略
        }}, f)
        tmp_policy = f.name
    with policy_env(tmp_policy):
        assert policy.level_of("norm.region-added") == "warn", "覆盖应放宽"
        assert policy.level_of("norm.divider-added") == "warn", "覆盖不得比固有更严"
        assert policy.snapshot()["norm.region-added"]["source"] == "policy"
    os.remove(tmp_policy)
    assert policy.level_of("norm.region-added") == "block", "还原后应回固有"
    print("  ✅ 放宽生效 / 收紧被拒 / 还原回固有 / 未知检查安全默认")


async def test_ceremony_relax():
    """policy 放宽 ceremony.recipe_gate → 未过链也能写，但响应携带 warning（降级不静默）。"""
    print("\n── v3 仪式降级机制: warn 放行留痕 / 退出即回 block ──")
    import tempfile

    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"checks": {"ceremony.recipe_gate": {"level": "warn"}}}, f)
        tmp_policy = f.name
    with policy_env(tmp_policy):
        await call("gate_reset")
        r = await call("write_gated", path="tmp/relax_probe.shader", content=GOOD_SHADER)
        assert r["status"] == "OK", f"policy 放宽后应放行: {r}"
        assert os.path.isfile(os.path.join(TMP, "relax_probe.shader")), "应真的写入"
        assert any(w.get("id") == "ceremony.recipe_gate" for w in r.get("warnings", [])), \
            f"降级必须留 warning: {r}"
        cleanup("relax_probe.shader")
        await call("gate_reset")
    os.remove(tmp_policy)

    # 退出 policy_env → 回到套件钉住的 block: 同一次写入 → DENIED
    r = await call("write_gated", path="tmp/relax_probe.shader", content=GOOD_SHADER)
    assert r["status"] == "DENIED" and r["error"] == "NO_RECIPE", f"钉回 block 应拒: {r}"
    await call("gate_reset")
    print("  ✅ warn 放行且留痕 / 回 block 即拒（策略是数据，不是代码分支）")


async def test_write_cli_channel():
    """gate_cli write — v2 唯一缺失的通道；与 MCP 同一判定核，逐码一致。"""
    print("\n── v3 统一写通道: gate_cli write（MCP 工具表冻结不再等于写入断掉）──")
    cli = os.path.join(ROOT, "validation", "gate_cli.py")
    env = dict(os.environ, PYTHONPATH=ROOT)
    src = os.path.join(TMP, "cli_write_src.shader")
    out = os.path.join(TMP, "cli_write_out.shader")
    bad = os.path.join(TMP, "cli_write_bad.shader")

    def run(*args, stdin=None):
        return subprocess.run([sys.executable, cli, *args],
                              capture_output=True, text=True, env=env, input=stdin)

    with open(src, "w", encoding="utf-8") as f:
        f.write(GOOD_SHADER)
    with open(bad, "w", encoding="utf-8") as f:
        f.write("// 没有 Shader 声明\n")

    # 用法: 缺 --path / 缺内容来源 → exit 2
    assert run("write", "--path", "tmp/x.shader").returncode == 2, "缺内容来源应 exit 2"
    assert run("write", "--content", "x").returncode == 2, "缺 --path 应 exit 2"

    # 未过链（前序用例已 gate_reset）→ 与 MCP 同一道闸
    r = run("write", "--path", "tmp/cli_write_out.shader", "--from", src)
    assert r.returncode == 1 and "NO_RECIPE" in r.stdout, f"got {r.stdout}"

    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")

    # 作用域: 与 MCP 同一个 validate_path（逐码一致）
    r = run("write", "--path", "/etc/hosts", "--content", "x")
    mcp = await call("write_gated", path="/etc/hosts", content="x")
    assert r.returncode == 1 and "PATH_NOT_ALLOWED" in r.stdout, f"got {r.stdout}"
    assert mcp["error"] == "PATH_NOT_ALLOWED", f"got {mcp}"

    # --dry-run: 判定但不落盘
    r = run("write", "--path", "tmp/cli_write_out.shader", "--from", src, "--dry-run")
    assert r.returncode == 0 and json.loads(r.stdout)["dry_run"] is True, f"got {r.stdout}"
    assert not os.path.isfile(out), "--dry-run 不应落盘"

    # 实写（内容来自 stdin）+ 规范违规 → exit 1
    r = run("write", "--path", "tmp/cli_write_out.shader", "-", stdin=GOOD_SHADER)
    assert r.returncode == 0 and json.loads(r.stdout)["status"] == "OK", f"got {r.stdout}"
    assert open(out, encoding="utf-8").read() == GOOD_SHADER, "内容应保真"

    r = run("write", "--path", "tmp/cli_write_bad_out.shader", "--from", bad)
    assert r.returncode == 1 and "NORM_VIOLATION" in r.stdout, f"got {r.stdout}"
    assert not os.path.isfile(os.path.join(TMP, "cli_write_bad_out.shader")), "被拒写入零落盘"

    cleanup("cli_write_src.shader")
    cleanup("cli_write_out.shader")
    cleanup("cli_write_bad.shader")
    await call("gate_reset")
    print("  ✅ 用法 / 门禁同源 / 作用域逐码一致 / dry-run / stdin / 违规阻断 均正确")


def test_friction_report():
    """摩擦账本 → 聚合 → 提案。安全类零命中不提议退役；仪式比过高提议降级。"""
    print("\n── v3 摩擦账本: 聚合口径 / 提案规则 / 安全类保护 ──")
    import tempfile
    import telemetry
    from validation import friction_report

    ledger = tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False).name
    prev_ledger = os.environ.get("MCP_FRICTION_FILE")
    try:
        os.environ["MCP_FRICTION_FILE"] = ledger
        telemetry.clear()
        for _ in range(25):
            telemetry.record(op="write", tool="t", outcome="ok", signal="ok.write")
        for _ in range(3):
            telemetry.record(op="write", tool="t", outcome="denied",
                             signal="deny.norm.region-added", check="norm.region-added")
        for _ in range(10):
            telemetry.record(op="gate", tool="gate_pass", outcome="ok", signal="ceremony.g_entry")
        telemetry.record(op="delete", tool="t", outcome="dry_run", signal="dry_run.delete")

        agg = friction_report.aggregate(telemetry.read_all())
        assert agg["real_ops"] == 28, f"dry-run 不计入真实操作: {agg['real_ops']}"
        assert agg["ceremony_calls"] == 10, f"got {agg['ceremony_calls']}"
        assert agg["sample_sufficient"] is True

        # 「哪些检查不可退役」是**已发布策略**的性质 —— aggregate 会把当时的 policy 快照进去，
        # 故须在 shipped 策略下重算 agg，而不是只换 proposals 的环境。
        with policy_env(SHIPPED_POLICY):
            agg_shipped = friction_report.aggregate(telemetry.read_all())
            assert agg_shipped["policy"]["scope.path"]["retirable"] is False, "断言前提"
            props = friction_report.proposals(agg_shipped)
            assert any(p["kind"] == "safety-healthy" for p in props), \
                f"安全类零命中必须标 health，不得提议退役: {props}"
            for cid in ("deletion.recoverability", "deletion.guid-referenced",
                        "move.presafety", "scope.path", "scope.workspace"):
                assert not any(p["kind"] == "retire-or-demote" and p["check"] == cid
                               for p in props), f"{cid} 不可退役，不得进退役提案: {props}"
            assert not any(p["kind"] == "demote" and p["check"] == "ceremony.recipe_gate"
                           for p in props), \
                f"已降为 warn 的项不该再被提议 demote: {props}"

        # 小样本 → 不出调级提案
        small = friction_report.aggregate(telemetry.read_all()[:2])
        assert small["sample_sufficient"] is False
        assert all(p["severity"] == "info" for p in friction_report.proposals(small)), \
            "小样本不得出 review 级提案"
    finally:
        telemetry.clear()                          # 先清临时账本（env 仍指向它）
        if prev_ledger is None:                    # 还原（不是 pop —— pop 会让后续用例写生产账本）
            os.environ.pop("MCP_FRICTION_FILE", None)
        else:
            os.environ["MCP_FRICTION_FILE"] = prev_ledger
        try:
            os.remove(ledger)
        except OSError:
            pass
    print("  ✅ dry-run 不计真实操作 / 安全类保护 / 小样本不结论")


# ════════════════════════════════════════════════════════════════
#  工作目录边界 — 任务级写入范围（用户下达）
# ════════════════════════════════════════════════════════════════

async def _arm(workspace=None):
    """过链（并可声明工作目录）—— 工作目录用例的公共前置。"""
    await call("gate_reset")
    await call("gate_set_recipe", name="Quick")
    await call("gate_pass", gate_id="g_entry", agent="unity-developer")
    await call("gate_pass", gate_id="g_knowledge", loaded_files=HIGH_PRIO, status="COMPLETE")
    if workspace:
        await call("gate_set_workspace", path=workspace)


async def test_workspace_scope():
    """工作目录: 未声明阻断 / 越界阻断 / tmp 豁免 / 只能收窄 / 删除移动同受约束。"""
    print("\n── v3+ 工作目录: 未声明 / 越界 / 豁免 / 收窄 / 删移同约束 ──")
    md = "Assets/Mine/Scripts/__ws_probe.md"
    shader = "Assets/Mine/Shaders/__ws_probe.shader"
    tmpf = "tmp/__ws_probe.md"
    probe_md = os.path.join(os.path.dirname(ROOT), md)
    probe_shader = os.path.join(os.path.dirname(ROOT), shader)
    for _f in (probe_md, probe_shader):        # 上一次中断的残留
        if os.path.isfile(_f):
            os.remove(_f)

    await _arm()   # 故意不声明工作目录
    r = await call("write_gated", path=md, content="x")
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_NOT_SET", f"got {r}"
    assert r["violations"][0]["id"] == "workspace-required", f"got {r['violations']}"

    # tmp/ 是暂存区，不属任务边界
    r = await call("write_gated", path=tmpf, content="x")
    assert r["status"] == "OK", f"tmp/ 应豁免工作目录边界: {r}"
    cleanup("__ws_probe.md")

    # 工作目录只能收窄 —— 超出基础作用域直接拒绝
    r = await call("gate_set_workspace", path="Assets/Other")
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_NOT_ALLOWED", f"got {r}"

    assert (await call("gate_set_workspace", path="Assets/Mine/Scripts"))["status"] == "OK"
    assert (await call("write_gated", path=md, content="x"))["status"] == "OK"

    r = await call("write_gated", path=shader, content='Shader "T/x"\n{}\n')
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_VIOLATION", f"got {r}"
    assert r["violations"][0]["id"] == "workspace-root", f"got {r['violations']}"
    assert not os.path.exists(probe_shader), "越界写入零落盘"

    # 删除同受约束
    r = await call("delete_gated", paths=["Assets/Mine/Shaders/other.md"], reason="越界删除")
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_VIOLATION", f"got {r}"

    # 移动两端都要在边界内
    r = await call("move_gated", moves=[{"from": md, "to": shader}], reason="搬出边界")
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_VIOLATION", f"got {r}"

    # gate_reset = 新任务 → 边界清空
    await call("gate_reset")
    st = await call("gate_status")
    assert st["workspace"]["roots"] == [], f"reset 应清空工作目录: {st['workspace']}"
    for f in (probe_md, probe_shader):
        if os.path.isfile(f):
            os.remove(f)
    print("  ✅ 未声明阻断 / tmp 豁免 / 收窄校验 / 越界零落盘 / 删移同约束 / reset 清空")


async def test_workspace_request_approval():
    """多工作区: agent 只能登记请求；批准是人工动作（CLI）；未批准仍越界。"""
    print("\n── v3+ 多工作区: request 登记 ≠ 授权 / CLI 人工批准后才生效 ──")
    cli = os.path.join(ROOT, "validation", "gate_cli.py")
    env = dict(os.environ, PYTHONPATH=ROOT)
    inprobe = "Assets/Mine/Scripts/__ws_a.md"
    outprobe = "Assets/Mine/Shaders/__ws_b.shader"
    out_probe_abs = os.path.join(os.path.dirname(ROOT), outprobe)

    def run(*args):
        return subprocess.run([sys.executable, cli, *args],
                              capture_output=True, text=True, env=env)

    await _arm(workspace="Assets/Mine/Scripts")

    # reason 必填；越界目标不得登记
    r = await call("gate_request_workspace", path="Assets/Mine/Shaders", reason="  ")
    assert r["status"] == "DENIED" and r["error"] == "MISSING_REASON", f"got {r}"
    r = await call("gate_request_workspace", path="Assets/Other", reason="x")
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_NOT_ALLOWED", f"got {r}"

    r = await call("gate_request_workspace", path="Assets/Mine/Shaders", reason="需要改 X shader")
    assert r["status"] == "NEEDS_APPROVAL" and r["request_id"], f"got {r}"
    rid = r["request_id"]
    assert any(p["id"] == rid for p in r["pending"]), f"请求应进待批: {r}"

    # 未批准 → 仍然越界（request ≠ grant）
    r = await call("write_gated", path=outprobe, content='Shader "T/x"\n{}\n')
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_VIOLATION", f"got {r}"

    # 人工批准（CLI 通道）→ server 进程须看得见（workspace_view 合并磁盘）
    r = run("workspace", "approve", rid)
    assert r.returncode == 0 and json.loads(r.stdout)["status"] == "OK", f"got {r.stdout}"
    r = await call("write_gated", path=outprobe, content='Shader "T/x"\n{}\n')
    assert r["status"] == "OK", f"批准后应放行: {r}"

    # 拒绝路径: 登记 → reject → 仍越界且不在待批
    r = await call("gate_request_workspace", path="Assets/Mine/Effects", reason="看看")
    rid2 = r["request_id"]
    assert json.loads(run("workspace", "reject", rid2, "--reason", "不需要").stdout)["status"] == "OK"
    st = await call("gate_status")
    assert not any(p["id"] == rid2 for p in st["workspace"]["pending"]), f"reject 应移出待批: {st}"
    r = await call("write_gated", path="Assets/Mine/Effects/__ws_c.md", content="x")
    assert r["status"] == "DENIED" and r["error"] == "WORKSPACE_VIOLATION", f"got {r}"

    # show 可读
    r = run("workspace", "show")
    assert r.returncode == 0 and "roots" in json.loads(r.stdout)["workspace"], f"got {r.stdout}"

    await call("gate_reset")
    for f in (os.path.join(os.path.dirname(ROOT), inprobe), out_probe_abs):
        if os.path.isfile(f):
            os.remove(f)
    print("  ✅ reason 必填 / 非法目标拒绝 / request≠grant / CLI 批准生效 / reject 移出 / show")


def test_workspace_policy_required():
    """policy required=false → 回到 v2 行为（不声明也能写）。"""
    print("\n── v3+ 工作目录策略: required=false 回到 v2 ──")
    import tempfile
    from validation import policy
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"workspace": {"required": False, "applies_to": ["Assets/Mine/"]}, "checks": {}}, f)
        tmp_policy = f.name
    with policy_env(tmp_policy):
        assert policy.workspace_policy()["required"] is False
    os.remove(tmp_policy)
    assert policy.workspace_policy()["required"] is True, "还原回套件策略（required=true）"
    print("  ✅ required 由 policy 数据控制 / 还原回 true")


def test_shipped_policy():
    """已发布策略的取值 = 我们决定过的姿态。改动必须显式，不能悄悄漂移。"""
    print("\n── 已发布策略姿势（防漂移回归）──")
    from validation import policy
    checks = shipped_policy()["checks"]

    # 2026-10-09 人工批准的两项降级
    assert checks["ceremony.recipe_gate"]["level"] == "warn", "2026-10-09 批准降级"
    assert checks["ceremony.knowledge_high_priority"]["level"] == "warn"
    # 未随仪式一起放宽的（防幻觉 + 产物可用性）
    assert checks["ceremony.knowledge_resolvable"]["level"] == "block", "防幻觉不降级"
    assert checks["norm.shader-decl"]["level"] == "block"
    assert checks["norm.cs-type-decl"]["level"] == "block"
    # 安全 / 权限类不可退役
    for cid in ("scope.path", "scope.workspace", "deletion.recoverability",
                "deletion.guid-referenced", "move.presafety", "ceremony.recipe_gate"):
        assert checks[cid].get("retirable") is False, f"{cid} 不可退役"
    # 工作目录边界默认开启
    assert shipped_policy()["workspace"]["required"] is True

    with policy_env(SHIPPED_POLICY):
        assert policy.level_of("ceremony.recipe_gate") == "warn"
        assert policy.level_of("scope.workspace") == "block"
        assert policy.workspace_policy()["required"] is True
    print("  ✅ 降级/保留/不可退役/工作目录开关 与决定一致")


async def test_shipped_relaxed_write():
    """端到端（已发布策略）: 未过链可写但留 warning；防幻觉仍硬拦；缺高优先级只提示。"""
    print("\n── 已发布策略端到端: 未过链可写+留痕 / 防幻觉仍拦 ──")
    with policy_env(SHIPPED_POLICY):
        await call("gate_reset")
        r = await call("write_gated", path="tmp/shipped_probe.shader", content=GOOD_SHADER)
        assert r["status"] == "OK", f"已发布策略应放行未过链写入: {r}"
        assert r["recipe"] is None, f"未声明配方应记为 None: {r}"
        assert any(w.get("id") == "ceremony.recipe_gate" for w in r.get("warnings", [])), \
            f"降级不静默 —— 必须携带 warning: {r}"
        cleanup("shipped_probe.shader")

        # 编造文件名 → 仍然硬拦（没随仪式降级）
        await call("gate_set_recipe", name="Quick")
        assert (await call("gate_pass", gate_id="g_entry",
                           agent="unity-developer"))["status"] == "OK"
        r = await call("gate_pass", gate_id="g_knowledge", status="COMPLETE",
                       loaded_files=HIGH_PRIO + ", unity/standard/shader/不存在.md")
        assert r["status"] == "DENIED" and r["error"] == "G15_UNRESOLVED_FILE", \
            f"编造文件名必须仍拦: {r}"

        # 缺高优先级 → 放宽为提示，但仍标记 RELAXED
        r = await call("gate_pass", gate_id="g_knowledge", status="COMPLETE",
                       loaded_files="unity/standard/shader/shader-structure.md")
        assert r["status"] == "OK" and r.get("knowledge") == "RELAXED", f"got {r}"

        # 全申报 → 正常通过，不标 RELAXED
        r = await call("gate_pass", gate_id="g_knowledge", status="COMPLETE", loaded_files=HIGH_PRIO)
        assert r["status"] == "OK" and r.get("knowledge") == "COMPLETE", f"got {r}"
    await call("gate_reset")
    print("  ✅ 未过链可写+留 warning / 防幻觉硬拦 / 缺项降为 RELAXED / 全申报仍 COMPLETE")


async def main():
    print("=" * 50)
    print("  Gate Tests (consequence-verification v3)")
    print("=" * 50)

    print("\n── Self-check (registry invariants) ──")
    self_check()

    await test_single_recipe_initialization_preserves_entry()
    await test_meta_bypass_boundary()
    test_validate_path_scope()
    await test_production_chain()
    await test_assets_mine_prefix_gated_ops()
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
    test_policy_levels()
    await test_ceremony_relax()
    await test_write_cli_channel()
    test_friction_report()
    await test_workspace_scope()
    await test_workspace_request_approval()
    test_workspace_policy_required()
    test_shipped_policy()
    await test_shipped_relaxed_write()
    await test_restart_recovery()   # 放最后: 子进程管理自己的 state.json, 不干扰进程内用例

    print("\n" + "=" * 50)
    print("  All tests passed ✓")
    print("=" * 50)
    if os.path.isfile(TEST_LEDGER):
        os.remove(TEST_LEDGER)


if __name__ == "__main__":
    asyncio.run(main())
