"""执行策略 — 把「门禁有多严」从代码里拿出来，变成可调数据.

背景（v3）: v2 把门禁按操作建模（write / delete / move），判定逻辑是代码里的常量。
问题是**严格度恒定**——agent 变强了，闸门不会跟着松；实测出误拦，闸门也不会跟着调。
于是框架只能靠人「注意到」才升级，天然滞后于 agent 能力。

本模块让每个检查项的级别（block / warn / off）成为 ``.mcp/policy.json`` 里的数据：
  - 改策略 = 改数据，不动判定实现
  - 缺省条目 = 用检查自身的固有级别（等价于 v2 行为，零回归）
  - 任何读取失败都回落到固有级别 —— 策略是增强，不是依赖

level 语义:
  block — 违规即 DENIED（阻断）
  warn  — 放行，响应携带 warnings
  off   — 不判定，仅记账（检查仍在信号里留痕，便于观察「本该拦的有没有出事」）

谁改 policy.json: meta-developer，经 friction_report 提案 + 人工确认（见
``.agents/knowledge/meta/enforcement-policy.md``）。
"""

from __future__ import annotations

import json
import os

_HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_POLICY_FILE = os.path.join(os.path.dirname(_HERE), "policy.json")

LEVELS = ("off", "warn", "block")
_STRICTNESS = {"off": 0, "warn": 1, "block": 2}

# 固有级别 — 检查实现自报；policy.json 缺条目时用它（= v2 行为）
INTRINSIC: dict[str, str] = {
    "scope.path": "block",
    "norm.shader-decl": "block",
    "norm.cs-type-decl": "block",
    "norm.region-added": "block",
    "norm.divider-added": "warn",
    "placement.root.new": "block",
    "placement.root.existing": "warn",
    "placement.category": "warn",
    "deletion.recoverability": "block",
    "deletion.guid-referenced": "block",
    "move.presafety": "block",
    "ceremony.recipe_gate": "block",
    "ceremony.knowledge_high_priority": "block",
    "ceremony.knowledge_resolvable": "block",
    "scope.workspace": "block",
}

# 工作目录默认配置（policy.json 的 workspace 段覆盖）
_WORKSPACE_DEFAULTS = {"required": False, "applies_to": ["Assets/Mine/"]}

_cache: dict | None = None


def _load() -> dict:
    """读取 policy.json；损坏/缺失 → 空覆盖（全部回落固有级别）。"""
    global _cache
    if _cache is not None:
        return _cache
    data: dict = {}
    path = os.environ.get("MCP_POLICY_FILE") or DEFAULT_POLICY_FILE
    try:
        with open(path, encoding="utf-8") as f:
            raw = json.load(f)
        if isinstance(raw, dict) and isinstance(raw.get("checks"), dict):
            data = raw
    except (OSError, ValueError):
        data = {}
    _cache = data
    return data


def reload() -> dict:
    """丢弃缓存并重读（测试与报告用）。"""
    global _cache
    _cache = None
    return _load()


def level_of(check_id: str, intrinsic: str | None = None) -> str:
    """该检查项的有效级别。policy 无条目 → 固有级别。"""
    base = intrinsic or INTRINSIC.get(check_id, "block")
    override = (_load().get("checks") or {}).get(check_id) or {}
    level = override.get("level")
    if level not in LEVELS:
        return base
    # 策略只能放宽，不能收紧到比固有更严 —— 收紧是给检查实现的事，不是数据的事
    return level if _STRICTNESS[level] <= _STRICTNESS.get(base, 2) else base


def snapshot() -> dict:
    """当前策略视图（报告/测试用）: check_id → {level, source, why}."""
    override = (_load().get("checks") or {})
    out: dict[str, dict] = {}
    for check_id, intrinsic in INTRINSIC.items():
        entry = override.get(check_id) or {}
        level = level_of(check_id, intrinsic)
        out[check_id] = {
            "level": level,
            "intrinsic": intrinsic,
            "source": "policy" if entry.get("level") in LEVELS and entry.get("level") != intrinsic
                      else "intrinsic",
            "why": entry.get("why", ""),
            "retirable": entry.get("retirable", True),
        }
    return out


def workspace_policy() -> dict:
    """工作目录策略: {required, applies_to}. 缺省 required=False（= v2 行为）。"""
    raw = _load().get("workspace") or {}
    out = dict(_WORKSPACE_DEFAULTS)
    if isinstance(raw.get("required"), bool):
        out["required"] = raw["required"]
    if isinstance(raw.get("applies_to"), list) and raw["applies_to"]:
        out["applies_to"] = [str(x) for x in raw["applies_to"]]
    return out


def block_ids() -> set[str]:
    return {cid for cid, info in snapshot().items() if info["level"] == "block"}


def warn_ids() -> set[str]:
    return {cid for cid, info in snapshot().items() if info["level"] == "warn"}
