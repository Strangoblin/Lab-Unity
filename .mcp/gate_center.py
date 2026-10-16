"""Gate Center — 门禁注册中心.

只维护: 门禁注册表 + 配方表 + 状态追踪。
Gate 之间无耦合 — 可随机调用，配方只定义顺序。

2026-08-25 收敛（后果验证门禁）:
  链统一为 [g_entry, g_knowledge] — 门禁只保留「知识证据」实质校验。
  g_mode/g_script/g_file/g_web_search/g_plan 退役（保留注册，出现在任何配方即回归）。
  模式由 gate_set_recipe 声明（会话元数据），脚本决策/文件分类降级为 write_gated 注解（记录不阻塞）。
"""

from __future__ import annotations
import json, os
from dataclasses import dataclass, field
from importlib import import_module
from typing import Optional

from validation.policy import level_of
from validation.project_paths import ALLOWED_PREFIXES, check_workspace_roots
from telemetry import record as emit


# ═══ 注册表 — 门禁元信息（Gate 模块路径）═══

GATE_REGISTRY: dict[str, dict] = {
    "g_entry":     {"name": "框架入口", "module": "gates.g_entry",
                    "requires": [], "context_keys": ["agent"]},
    "g_knowledge": {"name": "知识加载证据校验", "module": "gates.g_knowledge",
                    "requires": ["g_entry"], "context_keys": ["loaded_files", "status"]},
    # ── 退役门禁 ──
    # 保留注册只为两件事：`gate_list` 能告诉 agent「这个名字退役了、该走哪条路」，
    # 以及测试钉住「退役项出现在任何配方 = 回归」。**故不带 `module`** —— 它们
    # 永不进配方，`_load_gate` 到不了，模块文件已于 2026-10-09 删除（见 dated memory）。
    # 若某天要复活某个：先写回 gates/<name>.py，再加回 `module` 与配方条目。
    "g_mode":      {"name": "模式确认（退役→gate_set_recipe）",
                    "requires": ["g_entry"], "context_keys": ["mode", "reason"], "retired": True},
    "g_script":    {"name": "脚本决策（退役→write_gated 注解）",
                    "requires": ["g_knowledge"], "context_keys": ["decision"], "retired": True},
    "g_file":      {"name": "文件放置（退役→write_gated file_type/category/effect 注解）",
                    "requires": ["g_knowledge"], "context_keys": ["file_type", "category", "effect"], "retired": True},
    "g_web_search":{"name": "联网搜索（退役）",
                    "requires": ["g_entry"], "context_keys": ["query", "summary"], "retired": True},
    "g_plan":      {"name": "方案设计（退役）",
                    "requires": ["g_web_search"], "context_keys": ["plan_summary"], "retired": True},
}

RETIRED_GATES = [g for g, d in GATE_REGISTRY.items() if d.get("retired")]


# ═══ 配方表 — 会话模式声明（链统一为最短实质链）═══

RECIPES: dict[str, list[str]] = {
    "Production":  ["g_entry", "g_knowledge"],
    "Research":    ["g_entry", "g_knowledge"],
    "Experiment":  ["g_entry", "g_knowledge"],
    "Debug":       ["g_entry", "g_knowledge"],
    "Minimal":     ["g_entry", "g_knowledge"],
    "Quick":       ["g_entry", "g_knowledge"],   # 配方 = 模式声明；门禁链唯一
}


# ═══ 惰性加载 Gate 模块 ═══

_gate_cache: dict[str, object] = {}

def _load_gate(gate_id: str):
    if gate_id not in _gate_cache:
        _gate_cache[gate_id] = import_module(GATE_REGISTRY[gate_id]["module"])
    return _gate_cache[gate_id]


# ═══ policy — 把检查结果按 policy 级别降级（block→warn/off）═══

_ERROR_TO_CHECK = {
    "G15_MISSING_HIGH_PRIORITY": "ceremony.knowledge_high_priority",
    "G15_UNRESOLVED_FILE": "ceremony.knowledge_resolvable",
    "G15_AMBIGUOUS_FILE": "ceremony.knowledge_resolvable",
}


def _apply_check_policy(gate_id: str, result: dict) -> dict:
    """policy 把该检查降为 warn/off → DENIED 转 OK（携带 warning），仍记账。

    只作用于知识证据类检查；身份/顺序类错误（G0_FAILED / PREREQUISITE）不受 policy 影响。
    """
    if result.get("status") == "OK":
        return result
    check_id = _ERROR_TO_CHECK.get(result.get("error", ""))
    if not check_id:
        return result
    level = level_of(check_id)
    if level == "block":
        return result
    emit(op="gate", tool="gate_pass", outcome="warn" if level == "warn" else "ok",
         signal=f"muted.{check_id}", check=check_id, level=level,
         detail=result.get("hint", ""))
    return {
        "status": "OK",
        "knowledge": "RELAXED",
        "warning": {"id": check_id, "level": level,
                    "detail": result.get("hint", "policy 已放宽此检查")},
    }


# ═══ SessionState ═══

@dataclass
class SessionState:
    recipe: Optional[str] = None
    passed: set[str] = field(default_factory=set)
    contexts: dict = field(default_factory=dict)
    # 工作目录（任务级写入边界，用户下达）: primary 主目录 + roots 已授权列表 + pending 待批请求
    workspace: dict = field(default_factory=lambda: {
        "primary": None, "roots": [], "pending": [], "set_at": None})
    writes: list[dict] = field(default_factory=list)   # 写入审计日志（注解随写记录）
    deletes: list[dict] = field(default_factory=list)  # 删除审计日志（write_gated 的对称面）
    moves: list[dict] = field(default_factory=list)    # 移动审计日志（写删复合面，记 src→dst 对）

    @property
    def remaining(self) -> list[str]:
        if not self.recipe:
            return []
        return [g for g in RECIPES.get(self.recipe, []) if g not in self.passed]

    def set_recipe(self, name: str) -> dict:
        if name not in RECIPES:
            emit(op="gate", tool="gate_set_recipe", outcome="denied",
                 signal="deny.ceremony.invalid_recipe", detail=name)
            return {"status": "DENIED", "error": "INVALID_RECIPE",
                    "hint": f"Recipe 必须为 {list(RECIPES.keys())} 之一。"}
        self.recipe = name
        self.passed.clear()
        self.contexts.clear()
        _save_state(self)
        emit(op="gate", tool="gate_set_recipe", outcome="ok", signal="ceremony.recipe",
             recipe=name)
        return {"status": "OK", "recipe": name, "gates": RECIPES[name],
                "names": {g: GATE_REGISTRY[g]["name"] for g in RECIPES[name]}}

    def pass_gate(self, gate_id: str, **kwargs) -> dict:
        if gate_id not in GATE_REGISTRY:
            return {"status": "DENIED", "error": "INVALID_GATE",
                    "hint": f"Gate 必须为 {list(GATE_REGISTRY.keys())} 之一。收到: '{gate_id}'"}
        if not self.recipe:
            return {"status": "DENIED", "error": "NO_RECIPE",
                    "hint": "请先 gate_set_recipe(name)。"}
        if gate_id not in RECIPES[self.recipe]:
            retired = GATE_REGISTRY.get(gate_id, {}).get("retired", False)
            hint = (f"'{gate_id}' 已退役（后果验证门禁只保留 [g_entry, g_knowledge]）。"
                    f"模式走 gate_set_recipe，脚本决策/文件分类走 write_gated 注解。"
                    if retired else
                    f"'{gate_id}' 不在配方 '{self.recipe}' 中。配方: {RECIPES[self.recipe]}")
            return {"status": "DENIED", "error": "GATE_NOT_IN_RECIPE", "hint": hint}

        # 检查前置（门禁自身 requires）
        for req in GATE_REGISTRY[gate_id]["requires"]:
            if req not in self.passed:
                return {"status": "DENIED", "error": "PREREQUISITE",
                        "hint": f"'{gate_id}' 需先通过 '{req}'。已通过: {sorted(self.passed)}"}

        # 检查配方顺序（配方中 gate_id 之前的所有门禁必须已通过）
        recipe_gates = RECIPES[self.recipe]
        pos = recipe_gates.index(gate_id)
        for earlier in recipe_gates[:pos]:
            if earlier not in self.passed:
                return {"status": "DENIED", "error": "PREREQUISITE_ORDER",
                        "hint": f"配方顺序: '{gate_id}' 前还需通过 '{earlier}'。"
                                f"已通过: {sorted(self.passed)}。配方: {recipe_gates}"}

        # 收集上下文 → 调用 gate.check()
        ctx = {k: v for k, v in kwargs.items()
               if k in GATE_REGISTRY[gate_id]["context_keys"]}
        gate_mod = _load_gate(gate_id)
        result = gate_mod.check(ctx)
        result = _apply_check_policy(gate_id, result)

        if result.get("status") == "OK":
            self.passed.add(gate_id)
            self.contexts[gate_id] = ctx
            result["gate_name"] = GATE_REGISTRY[gate_id]["name"]
            result["remaining"] = self.remaining
            _save_state(self)
            emit(op="gate", tool="gate_pass", outcome="ok", signal=f"ceremony.{gate_id}",
                 recipe=self.recipe)
            return result
        emit(op="gate", tool="gate_pass", outcome="denied",
             signal=f"deny.{result.get('error', 'gate')}", recipe=self.recipe,
             detail=result.get("hint", ""))
        return result

    # ── 工作目录 ──

    def workspace_roots(self) -> list:
        return list(self.workspace_view().get("roots", []))

    def workspace_view(self) -> dict:
        """工作目录视图 —— 合并内存与 state.json。

        为什么: 人工批准走 CLI，写的是磁盘；MCP server 进程的内存态看不见 —— 不合并就会
        「用户批了、agent 重试仍被拒」。roots 取并集（批准只增不减 = 安全方向）；
        pending 以磁盘为准（人工动作的落点）。
        """
        disk: dict = {}
        try:
            if os.path.isfile(STATE_FILE):
                with open(STATE_FILE, encoding="utf-8") as f:
                    disk = json.load(f).get("workspace") or {}
        except (OSError, ValueError):
            disk = {}
        if not disk:
            return self.workspace
        roots = list(dict.fromkeys(
            list(self.workspace.get("roots", [])) + list(disk.get("roots", []))))
        merged = {
            "primary": self.workspace.get("primary") or disk.get("primary"),
            "roots": roots,
            "pending": disk.get("pending", self.workspace.get("pending", [])),
            "set_at": self.workspace.get("set_at") or disk.get("set_at"),
        }
        self.workspace = merged
        return merged

    def set_workspace(self, path: str) -> dict:
        """声明本次任务的工作目录。必须是单个目录，且落在基础作用域内（收窄而非放宽）。"""
        p = (path or "").strip().rstrip("/")
        if not p:
            return {"status": "DENIED", "error": "EMPTY_WORKSPACE",
                    "hint": "gate_set_workspace(path) 需要一个目录，如 Assets/Mine/Scripts。"}
        check = check_workspace_roots([p], ALLOWED_PREFIXES)
        if check["status"] != "OK":
            emit(op="gate", tool="gate_set_workspace", outcome="denied",
                 signal="deny.workspace.not_allowed", detail=p)
            return check
        self.workspace = {"primary": p, "roots": [p], "pending": [],
                          "set_at": int(__import__("time").time() * 1000)}
        _save_state(self)
        emit(op="gate", tool="gate_set_workspace", outcome="ok", signal="workspace.set",
             detail=p, recipe=self.recipe)
        return {"status": "OK", "workspace": p, "roots": [p],
                "hint": "本次任务的写入 / 删除 / 移动都限定在此目录内；"
                        "需扩展走 gate_request_workspace(path, reason) 交人工批准。"}

    def request_workspace(self, path: str, reason: str) -> dict:
        """申请扩展工作目录。**只登记待批请求，不授权** —— 批准是人工动作（CLI）。"""
        p = (path or "").strip().rstrip("/")
        if not p:
            return {"status": "DENIED", "error": "EMPTY_WORKSPACE", "hint": "缺少目录。"}
        if not reason or not reason.strip():
            return {"status": "DENIED", "error": "MISSING_REASON",
                    "hint": "申请扩展工作目录必须给 reason（进审计，供人裁决）。"}
        check = check_workspace_roots([p], ALLOWED_PREFIXES)
        if check["status"] != "OK":
            emit(op="gate", tool="gate_request_workspace", outcome="denied",
                 signal="deny.workspace.not_allowed", detail=p)
            return check
        if p in self.workspace_roots():
            return {"status": "OK", "already_granted": True, "workspace": p,
                    "hint": "该目录已在授权列表内，无需申请。"}
        import uuid
        rid = "ws-" + uuid.uuid4().hex[:8]
        self.workspace.setdefault("pending", []).append(
            {"id": rid, "path": p, "reason": reason.strip()})
        _save_state(self)
        emit(op="gate", tool="gate_request_workspace", outcome="ok",
             signal="workspace.requested", detail=f"{rid} {p}")
        return {
            "status": "NEEDS_APPROVAL", "request_id": rid, "path": p,
            "pending": self.workspace.get("pending", []),
            "hint": "请求已登记。**批准是人工动作，agent 不得自行执行** —— "
                    f"请由用户运行: python3 .mcp/validation/gate_cli.py workspace approve {rid}",
        }

    def approve_workspace(self, request_id: str, by: str = "human") -> dict:
        """批准一条待批请求（人工动作，经 CLI 调用；agent 自行调用违反规则且留痕）。"""
        pending = self.workspace.get("pending", [])
        hit = next((r for r in pending if r["id"] == request_id), None)
        if not hit:
            return {"status": "DENIED", "error": "NO_SUCH_REQUEST",
                    "hint": f"待批列表: {[r['id'] for r in pending]}"}
        self.workspace["pending"] = [r for r in pending if r["id"] != request_id]
        self.workspace.setdefault("roots", [])
        if hit["path"] not in self.workspace["roots"]:
            self.workspace["roots"].append(hit["path"])
        _save_state(self)
        emit(op="gate", tool="gate_approve_workspace", outcome="ok",
             signal="workspace.approved", detail=f"{request_id} {hit['path']} by={by}")
        return {"status": "OK", "approved": hit["path"], "by": by,
                "roots": self.workspace["roots"]}

    def reject_workspace(self, request_id: str, reason: str = "") -> dict:
        pending = self.workspace.get("pending", [])
        hit = next((r for r in pending if r["id"] == request_id), None)
        if not hit:
            return {"status": "DENIED", "error": "NO_SUCH_REQUEST",
                    "hint": f"待批列表: {[r['id'] for r in pending]}"}
        self.workspace["pending"] = [r for r in pending if r["id"] != request_id]
        _save_state(self)
        emit(op="gate", tool="gate_reject_workspace", outcome="ok",
             signal="workspace.rejected", detail=f"{request_id} {hit['path']} {reason}")
        return {"status": "OK", "rejected": hit["path"], "reason": reason}

    def can_write(self) -> dict:
        """配方门禁。policy 把 ceremony.recipe_gate 降为 warn/off 时，仪式不再阻断写入。

        这是「框架随 agent 能力放宽」的主旋钮：默认 block（v2 行为），
        实测 agent 已稳定自读知识后，可经 friction_report 提案把级别调低。
        """
        relaxed = level_of("ceremony.recipe_gate") != "block"
        if not self.recipe:
            if relaxed:
                return {"status": "OK", "recipe": None, "passed": [],
                        "warnings": [{"id": "ceremony.recipe_gate",
                                      "level": "warning",
                                      "detail": "未声明配方（policy 已放宽）"}]}
            return {"status": "DENIED", "error": "NO_RECIPE",
                    "hint": "请先 gate_set_recipe(name)。"}
        missing = self.remaining
        if missing:
            if relaxed:
                return {"status": "OK", "recipe": self.recipe, "passed": sorted(self.passed),
                        "warnings": [{"id": "ceremony.recipe_gate", "level": "warning",
                                      "detail": f"配方 '{self.recipe}' 未过: {missing}（policy 已放宽）"}]}
            return {"status": "DENIED", "error": "GATE_NOT_PASSED",
                    "missing": missing, "passed": sorted(self.passed),
                    "hint": f"配方 '{self.recipe}' 还需通过: {missing}"}
        return {"status": "OK", "recipe": self.recipe, "passed": sorted(self.passed)}

    def reset(self) -> None:
        emit(op="gate", tool="gate_reset", outcome="ok", signal="ceremony.reset")
        self.recipe = None
        self.passed.clear()
        self.contexts.clear()
        self.workspace = {"primary": None, "roots": [], "pending": [], "set_at": None}
        self.writes.clear()
        self.deletes.clear()
        self.moves.clear()
        _clear_state_file()


# ═══ 状态持久化 — MCP server 进程重启恢复 ═══
# Claude Code 会回收空闲的 stdio MCP server 进程；重启后进程内存全空 → recipe=None → NO_RECIPE。
# 持久化 = 进程无关的最小修复；gate_set_recipe / gate_reset 显式覆盖。
# 并发: 原子 os.replace, 多实例 last-writer-wins（writes 审计不持久化, 会话内; 落盘文件即持久记录）。

STATE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "state.json")


def _save_state(s: SessionState) -> None:
    """原子写 .mcp/state.json。失败静默 — 持久化是增强, 不阻塞门禁流。"""
    try:
        tmp = STATE_FILE + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump({"recipe": s.recipe, "passed": sorted(s.passed),
                       "contexts": s.contexts, "workspace": s.workspace},
                      f, ensure_ascii=False)
        os.replace(tmp, STATE_FILE)
    except Exception:
        pass


def _clear_state_file() -> None:
    try:
        if os.path.isfile(STATE_FILE):
            os.remove(STATE_FILE)
    except Exception:
        pass


state = SessionState()

# 启动恢复: server 进程重启后从 state.json 恢复配方/门禁。
try:
    if os.path.isfile(STATE_FILE):
        with open(STATE_FILE, encoding="utf-8") as f:
            _data = json.load(f)
        state.recipe = _data.get("recipe")
        state.passed = set(_data.get("passed", []))
        state.contexts = _data.get("contexts", {})
        ws = _data.get("workspace")
        if isinstance(ws, dict):
            state.workspace = {"primary": ws.get("primary"), "roots": ws.get("roots", []),
                               "pending": ws.get("pending", []), "set_at": ws.get("set_at")}
except Exception:
    pass   # 损坏/不可读 → 冷启动, 等价于旧行为
