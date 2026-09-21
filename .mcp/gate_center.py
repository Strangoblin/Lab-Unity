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


# ═══ 注册表 — 门禁元信息（Gate 模块路径）═══

GATE_REGISTRY: dict[str, dict] = {
    "g_entry":     {"name": "框架入口", "module": "gates.g_entry",
                    "requires": [], "context_keys": ["agent"]},
    "g_knowledge": {"name": "知识加载证据校验", "module": "gates.g_knowledge",
                    "requires": ["g_entry"], "context_keys": ["loaded_files", "status"]},
    # ── 退役门禁（保留注册供 gate_list 展示；不在任何配方，调用即 GATE_NOT_IN_RECIPE）──
    "g_mode":      {"name": "模式确认（退役→gate_set_recipe）", "module": "gates.g_mode",
                    "requires": ["g_entry"], "context_keys": ["mode", "reason"], "retired": True},
    "g_script":    {"name": "脚本决策（退役→write_gated 注解）", "module": "gates.g_script",
                    "requires": ["g_knowledge"], "context_keys": ["decision"], "retired": True},
    "g_file":      {"name": "文件放置（退役→write_gated 注解）", "module": "gates.g_file",
                    "requires": ["g_knowledge"], "context_keys": ["file_type", "category", "effect"], "retired": True},
    "g_web_search":{"name": "联网搜索（退役）", "module": "gates.g_web_search",
                    "requires": ["g_entry"], "context_keys": ["query", "summary"], "retired": True},
    "g_plan":      {"name": "方案设计（退役）", "module": "gates.g_plan",
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


# ═══ SessionState ═══

@dataclass
class SessionState:
    recipe: Optional[str] = None
    passed: set[str] = field(default_factory=set)
    contexts: dict = field(default_factory=dict)
    writes: list[dict] = field(default_factory=list)   # 写入审计日志（注解随写记录）
    deletes: list[dict] = field(default_factory=list)  # 删除审计日志（write_gated 的对称面）

    @property
    def remaining(self) -> list[str]:
        if not self.recipe:
            return []
        return [g for g in RECIPES.get(self.recipe, []) if g not in self.passed]

    def set_recipe(self, name: str) -> dict:
        if name not in RECIPES:
            return {"status": "DENIED", "error": "INVALID_RECIPE",
                    "hint": f"Recipe 必须为 {list(RECIPES.keys())} 之一。"}
        self.recipe = name
        self.passed.clear()
        self.contexts.clear()
        _save_state(self)
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

        if result.get("status") == "OK":
            self.passed.add(gate_id)
            self.contexts[gate_id] = ctx
            result["gate_name"] = GATE_REGISTRY[gate_id]["name"]
            result["remaining"] = self.remaining
            _save_state(self)
        return result

    def can_write(self) -> dict:
        if not self.recipe:
            return {"status": "DENIED", "error": "NO_RECIPE",
                    "hint": "请先 gate_set_recipe(name)。"}
        missing = self.remaining
        if missing:
            return {"status": "DENIED", "error": "GATE_NOT_PASSED",
                    "missing": missing, "passed": sorted(self.passed),
                    "hint": f"配方 '{self.recipe}' 还需通过: {missing}"}
        return {"status": "OK", "recipe": self.recipe, "passed": sorted(self.passed)}

    def reset(self) -> None:
        self.recipe = None
        self.passed.clear()
        self.contexts.clear()
        self.writes.clear()
        self.deletes.clear()
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
                       "contexts": s.contexts}, f, ensure_ascii=False)
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
except Exception:
    pass   # 损坏/不可读 → 冷启动, 等价于旧行为
