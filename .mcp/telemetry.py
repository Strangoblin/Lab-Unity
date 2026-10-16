"""摩擦账本 — 门禁的传感器（MCP v3）.

为什么需要它: v2 只记「产物」——写入有文件、删除/移动有审计日志。
但**框架该怎么演进**的证据不在产物里，在摩擦里: 哪条检查在误拦、哪个仪式从不产生决策价值、
哪类拒绝让 agent 反复重试。这些以前只以散文形式留在 dated memory，不可查、不可排序、
没有触发点，于是框架升级永远等人「想起来」——这就是滞后于 agent 能力的结构性原因。

本模块把每次判定落成一行 JSONL（``.mcp/friction.jsonl``），供
``validation/friction_report.py`` 聚合成提案。它只做一件事: 留痕。

三条设计约束:
  1. **永不抛异常** —— 记账失败不能影响门禁判定（尽力而为，失败静默）
  2. **不阻塞** —— 追加写，不做解析、不做网络
  3. **有界** —— 超过 ``MAX_BYTES`` 时保留最近 ``KEEP_LINES`` 行，防止无限增长

schema（稳定，报告与测试依赖）:
  ts      毫秒时间戳
  channel "mcp" | "cli"
  op      "write" | "delete" | "move" | "gate"
  tool    触发工具名（write_gated / move_gated.py / gate_pass …）
  outcome "ok" | "denied" | "warn" | "error"
  signal  聚合键，如 "deny.norm.region-added" / "ok.write" / "ceremony.recipe"
  check   检查项 id（可选，如 "norm.region-added"）
  level   有效级别（可选: block | warn | off）
  recipe  当前配方（可选）
  detail  人类可读摘要（可选）
"""

from __future__ import annotations

import json
import os
import time

_HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_FRICTION_FILE = os.path.join(_HERE, "friction.jsonl")


def friction_file() -> str:
    """账本路径。每次读环境变量 —— 测试可换到临时账本，不污染真账本。"""
    return os.environ.get("MCP_FRICTION_FILE") or DEFAULT_FRICTION_FILE

MAX_BYTES = 4 * 1024 * 1024
KEEP_LINES = 2000


def record(*, op: str, tool: str, outcome: str, signal: str, channel: str = "mcp",
           check: str = "", level: str = "", recipe: str = "", detail: str = "",
           path: str = "") -> None:
    """追加一条信号。永不抛异常。"""
    entry = {"ts": int(time.time() * 1000), "channel": channel, "op": op, "tool": tool,
             "outcome": outcome, "signal": signal}
    if check:
        entry["check"] = check
    if level:
        entry["level"] = level
    if recipe:
        entry["recipe"] = recipe
    if path:
        entry["path"] = path
    if detail:
        entry["detail"] = detail[:300]
    try:
        _trim_if_needed()
        with open(friction_file(), "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    except Exception:
        pass   # 记账是增强，永不阻断门禁


def _trim_if_needed() -> None:
    path = friction_file()
    try:
        if os.path.isfile(path) and os.path.getsize(path) > MAX_BYTES:
            with open(path, encoding="utf-8", errors="replace") as f:
                lines = f.readlines()[-KEEP_LINES:]
            tmp = path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                f.writelines(lines)
            os.replace(tmp, path)
    except Exception:
        pass


def read_all() -> list[dict]:
    """读全部条目；坏行跳过。供报告与测试使用。"""
    path = friction_file()
    if not os.path.isfile(path):
        return []
    out: list[dict] = []
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    item = json.loads(line)
                except ValueError:
                    continue
                if isinstance(item, dict):
                    out.append(item)
    except OSError:
        return []
    return out


def clear() -> None:
    path = friction_file()
    try:
        if os.path.isfile(path):
            os.remove(path)
    except OSError:
        pass
