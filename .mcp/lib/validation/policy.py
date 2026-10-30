"""执行策略 — 把「管得有多严」从代码里拿出来，变成可调数据.

背景: v2/v3 把门禁按操作建模（write / delete / move），判定逻辑是代码里的常量。
问题是**严格度恒定** —— agent 变强了，闸门不会跟着松；实测出误拦，闸门也不会跟着调。

本模块让每个检查项的级别（block / warn / off）成为 ``.mcp/policy.json`` 里的数据：
  - 改策略 = 改数据，不动判定实现
  - 缺省条目 = 用检查自身的固有级别（零回归）
  - 任何读取失败都回落到固有级别 —— 策略是增强，不是依赖

v4 的变化只在键的空间，机制不变：入口仪式的键（ceremony.*）随门禁线一起退役，
出口验收的键（close.*）成为新的主旋钮。判定实现不需要知道键叫什么。
v5 退役 `memory.history-immutable` —— 记忆区改为完全可写，历史由 git 兜底。

level 语义:
  block — 违规即拒绝（写入被 deny / 出口不可通过）
  warn  — 放行，结论里携带提示
  off   — 不判定，仅记账（检查仍在账本里留痕，便于观察「本该拦的有没有出事」）

谁改 policy.json: meta-developer，经 report 提案 + 人工确认（见
``.agents/knowledge/meta/enforcement-policy.md``）。
"""

from __future__ import annotations

import json
import os

_HERE = os.path.dirname(os.path.abspath(__file__))
# .mcp/lib/validation/policy.py → .mcp/config/policy.json
DEFAULT_POLICY_FILE = os.path.join(
    os.path.dirname(os.path.dirname(_HERE)), "config", "policy.json")

LEVELS = ("off", "warn", "block")
_STRICTNESS = {"off": 0, "warn": 1, "block": 2}

# 固有级别 — 检查实现自报；policy.json 缺条目时用它。
# 键的名字就是「哪一环在管什么」，报告里直接可读。
INTRINSIC: dict[str, str] = {
    # 写入前：分区与沙箱（v5 起判「有没有兜底」，不判「是哪个目录」）
    "scope.zone": "block",
    "sandbox.asset": "block",
    # 写入前：内容与落点
    "norm.shader-decl": "block",
    "norm.cs-type-decl": "block",
    "norm.region-added": "block",
    "norm.divider-added": "warn",
    "placement.root.new": "block",
    "placement.root.existing": "warn",
    "placement.category": "warn",
    # 移动与 Bash
    "move.presafety": "block",
    "bash.redline": "block",
    # 写入后：出口验收
    "close.knowledge": "block",
    "close.norm": "block",
    "close.compile": "block",
    "close.verify": "warn",
    "close.memory": "warn",
}

# 沙箱默认配置（policy.json 的 sandbox 段覆盖）
_SANDBOX_DEFAULTS = {"applies_to": ["Assets/Mine/"]}

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


def sandbox_policy() -> dict:
    """沙箱策略: {applies_to}. 只描述「哪些前缀受沙箱约束」，具体目录由会话给出。"""
    raw = _load().get("sandbox") or {}
    out = dict(_SANDBOX_DEFAULTS)
    if isinstance(raw.get("applies_to"), list) and raw["applies_to"]:
        out["applies_to"] = [str(x) for x in raw["applies_to"]]
    return out


def block_ids() -> set[str]:
    return {cid for cid, info in snapshot().items() if info["level"] == "block"}


def warn_ids() -> set[str]:
    return {cid for cid, info in snapshot().items() if info["level"] == "warn"}
