"""会话状态与证据账本 — 业务式管理的两个基础面。

v3 把「会话状态」定义成**门禁通过情况**（recipe / passed / workspace），于是每个任务
都先付一遍仪式。v4 只记三件对业务有用的事：

  task_open  — 这次做什么、沙箱在哪、用哪个 profile（一次调用）
  task_expand— 用户指出新目录后把沙箱扩一格（对话里确认，命令只负责记录）
  证据账本   — 真实发生了什么：读过哪些文件、写过哪些文件、删过哪些（可由记录重建）
  task_close — 交付是否合格（按 profile 的完成义务校验）

**为什么不记「读过了什么，请申报」**：PostToolUse 能直接看到 Read 的 `tool_input.file_path`，
申报是让人复述工具已经知道的事实。观测取代申报之后，编造文件名这类失败模式自动消失
（没读过就是没读过，没有可编造的字段）。

落盘位置都在 `.mcp/` 下、都在 `.gitignore` 里：它们是会话痕迹，不是仓库内容。
"""

from __future__ import annotations

import json
import os
import time
import uuid

_MCP_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROJECT_ROOT = os.path.dirname(_MCP_ROOT)
_STATE_DIR = os.path.join(_MCP_ROOT, "state")

SESSION_FILE = os.path.join(_STATE_DIR, "session.json")
WRITES_FILE = os.path.join(_STATE_DIR, "writes.jsonl")
READS_FILE = os.path.join(_STATE_DIR, "reads.jsonl")

MAX_LEDGER_LINES = 5000


def _now_ms() -> int:
    return int(time.time() * 1000)


def _env_path(name: str, default: str) -> str:
    """测试可换到临时文件，不污染真账本。"""
    return os.environ.get(name) or default


def session_file() -> str:
    return _env_path("MCP_SESSION_FILE", SESSION_FILE)


def _append_jsonl(path: str, entry: dict) -> None:
    """追加一行。永不抛异常 —— 记账失败不能影响业务操作。"""
    try:
        if os.path.isfile(path) and os.path.getsize(path) > 4 * 1024 * 1024:
            with open(path, encoding="utf-8", errors="replace") as f:
                lines = f.readlines()[-MAX_LEDGER_LINES:]
            tmp = path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                f.writelines(lines)
            os.replace(tmp, path)
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    except Exception:
        pass


def _read_jsonl(path: str) -> list[dict]:
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
        pass
    return out


# ═══ 会话 ═══

def load() -> dict:
    """读会话；不存在或损坏返回空会话（等价于「没开过任务」）。"""
    empty = {"task": None, "profile": None, "workspace": [], "opened_at": None,
             "waivers": [], "expansions": []}
    try:
        with open(session_file(), encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return empty
    if not isinstance(data, dict):
        return empty
    out = dict(empty)
    out.update({k: data[k] for k in out if k in data})
    if not isinstance(out.get("workspace"), list):
        out["workspace"] = []
    if not isinstance(out.get("waivers"), list):
        out["waivers"] = []
    if not isinstance(out.get("expansions"), list):
        out["expansions"] = []
    return out


def save(data: dict) -> None:
    path = session_file()
    try:
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        os.replace(tmp, path)
    except OSError:
        pass


def open_task(task: str, workspace: list[str], profile: str) -> dict:
    data = load()
    data.update({"task": task, "profile": profile, "workspace": list(workspace),
                 "opened_at": _now_ms(), "waivers": []})
    save(data)
    return data


def expand_task(workspace: list[str], reason: str) -> dict:
    """把目录加进当前任务的沙箱。**不重置证据起点** —— 它与 open 的语义不同。

    为什么允许 agent 执行这一步：沙箱的来源是用户在对话框里指的地址，确认发生在对话中，
    不在某个 agent 够不到的通道里（v3 的「登记 ↔ 人工 CLI 批准」实际没被用过一次）。
    这条命令因此只管**记录**：把用户确认的原话与目录落进 session 与摩擦账本，
    事后能回答「这块地是谁给的、什么时候给的」。
    """
    data = load()
    current = [str(d) for d in data.get("workspace", [])]
    added = [d for d in workspace if d not in current]
    data["workspace"] = current + added
    data.setdefault("expansions", []).append(
        {"dirs": added, "reason": reason, "at": _now_ms()})
    save(data)
    return {"workspace": data["workspace"], "added": added}


def close_task() -> None:
    """收尾不清空 workspace —— 结论性证据（编译日志、截图）常在这之后才引用。
    新任务 open 时整体覆盖，语义比「关掉再开」干净。"""
    data = load()
    data["closed_at"] = _now_ms()
    save(data)


def waived(check: str) -> dict | None:
    for item in load().get("waivers", []):
        if item.get("check") == check:
            return item
    return None


def add_waiver(check: str, reason: str) -> None:
    data = load()
    data.setdefault("waivers", []).append(
        {"check": check, "reason": reason, "at": _now_ms()})
    save(data)


# ═══ 证据账本 ═══

def record_write(path: str, zone: str, source: str, extra: dict | None = None) -> None:
    entry = {"ts": _now_ms(), "path": path, "zone": zone, "source": source}
    if extra:
        entry.update(extra)
    _append_jsonl(_env_path("MCP_WRITES_FILE", WRITES_FILE), entry)


def record_read(path: str, source: str) -> None:
    """只记资产与知识两类路径；读 tmp/Library 不构成知识证据。

    用 `effective_zone` 而不是 `zone_of`：已入库的仓库文件（`AGENTS.md` 之类）按体系区算，
    读它是货真价实的知识证据 —— 用查表结果会把这些读取全丢掉。
    """
    from zones import effective_zone
    zone = effective_zone(path)
    if zone not in ("asset", "memory", "system"):
        return
    _append_jsonl(_env_path("MCP_READS_FILE", READS_FILE),
                  {"ts": _now_ms(), "path": path, "zone": zone, "source": source})


def writes_since(since_ms: int | None) -> list[dict]:
    items = _read_jsonl(_env_path("MCP_WRITES_FILE", WRITES_FILE))
    return [i for i in items if since_ms is None or i.get("ts", 0) >= since_ms]


def reads_since(since_ms: int | None) -> list[dict]:
    items = _read_jsonl(_env_path("MCP_READS_FILE", READS_FILE))
    return [i for i in items if since_ms is None or i.get("ts", 0) >= since_ms]


def new_trash_id() -> str:
    return time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
