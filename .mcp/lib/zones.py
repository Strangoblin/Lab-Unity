"""分区模型 — 「这个路径属于哪个区、这个区允许怎么改」的唯一判定。

v3 之前只有一条边界：基础作用域 + 任务级工作目录。两者都回答
「允许写哪里」，于是**所有写入都是同一个问题**，只能一刀切：

  - 任务沙箱切到叶子目录 → 改 `Special/HLSL` 共享库、跨目录改 `SSCFeature` 全被拦
  - 收尾写记忆 → `.agents/memory` 不在作用域内，`WORKSPACE_NOT_ALLOWED`（实测 2 次硬撞）

v4 改成**按区分别定责**。路径属于哪个区，就只受那个区的规则约束：

| zone      | 路径                                                      | 规则 |
|-----------|-----------------------------------------------------------|------|
| asset     | `Assets/Mine/**`                                          | 本次会话沙箱内 + 内容规范/落点 |
| memory    | `.agents/memory/**`                                       | 随时可写，含既有记录（历史由 git 兜住） |
| system    | `.agents/{agents,knowledge,skills}`、`.mcp`、平台目录      | 可写，只记录 |
| scratch   | `tmp/**`、`.mcp/trash/**`                                 | 自由 |
| (denied)  | 其余（`Assets/` 非 Mine、`Library`、仓库外）               | 受 git 保护的按 system 处理，未受保护的拒绝 |

关键取舍：**asset 区的沙箱仍是硬边界**，因为那是用户下达的（「每次任务我指明小沙箱」）；
其余区的「限制」都被替换成「留痕」。拦得越少，留下的证据越多 —— 这是业务式管理的方向。

**git 保护 = 可随意编辑**（2026-10-28 用户裁决）：分区表之外的路径，只要 git 认为它已入库，
就按 system 区放行并记录。理由不是「agent 可靠」，而是**内容有版本库兜底** —— 改错一条
`git checkout` 就回来，而拦着它的代价是 agent 够不到项目入口文件（`AGENTS.md` / `.mcp.json` /
`.gitignore` 实测撞过）。未入库的文件（含新建）仍在拒绝侧：那里没有兜底。
规则作用于哪些区由 ``zones.json`` 的 ``git_protected.allow_zones`` 决定，改数据即可。

分区表是数据（``.mcp/zones.json``），不是本文件里的常量：换平台、加目录都是改数据。
"""

from __future__ import annotations

import json
import os
import subprocess

# 本模块在 .mcp/lib/，项目根要退两级；配置与状态也按这个锚点定位，
# 不再用 __file__ 的邻居 —— 否则目录一挪，判定核就找不到自己的配置文件。
_MCP_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROJECT_ROOT = os.path.abspath(os.path.join(_MCP_ROOT, ".."))

ZONES_FILE = os.path.join(_MCP_ROOT, "config", "zones.json")

# 读不到 zones.json 时的兜底 —— 与文件内容一致。兜底而不是报错：
# 判定核起不来会让资产区整体不可写，代价远大于「用了旧表」。
_FALLBACK_PREFIXES = [
    ("scratch", "tmp"),
    ("scratch", ".mcp/trash"),
    ("asset", "Assets/Mine"),
    ("memory", ".agents/memory"),
    ("system", ".agents/agents"),
    ("system", ".agents/knowledge"),
    ("system", ".agents/skills"),
    ("system", ".mcp"),
    ("system", "PLATFORM_CLAUDE_DIR"),
    ("system", "PLATFORM_CODEX_DIR"),
]
_FALLBACK_TOKENS = {"PLATFORM_CLAUDE_DIR": "." + "claude",
                    "PLATFORM_CODEX_DIR": "." + "codex"}
_FALLBACK_EXTS = {".shader", ".hlsl", ".cs", ".compute", ".md"}
# 分区表之外的路径，先问 git 是否有兜底；只有这些区才走这条放宽。
_FALLBACK_GIT_ZONES = ["denied", "asset"]
GIT_TIMEOUT = 20

_config: dict | None = None
_git_cache: dict[str, bool] = {}


def _load() -> dict:
    global _config
    if _config is not None:
        return _config
    data: dict = {}
    path = os.environ.get("MCP_ZONES_FILE") or ZONES_FILE
    try:
        with open(path, encoding="utf-8") as f:
            raw = json.load(f)
        if isinstance(raw, dict):
            data = raw
    except (OSError, ValueError):
        data = {}
    _config = data
    return data


def reload() -> dict:
    global _config
    _config = None
    _git_cache.clear()
    return _load()


def _tokens() -> dict:
    raw = _load().get("platform_dir_tokens") or {}
    out = dict(_FALLBACK_TOKENS)
    for key in list(out):
        if isinstance(raw.get(key), str) and raw[key]:
            out[key] = raw[key]
    return out


def zone_prefixes() -> list[tuple[str, str]]:
    """有序分区表，占位符已替换成真实目录名。"""
    tokens = _tokens()
    entries = _load().get("zones") or []
    out: list[tuple[str, str]] = []
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        zone, prefix = entry.get("zone"), entry.get("prefix")
        if not isinstance(zone, str) or not isinstance(prefix, str):
            continue
        out.append((zone, tokens.get(prefix, prefix)))
    if not out:
        return [(zone, tokens.get(prefix, prefix)) for zone, prefix in _FALLBACK_PREFIXES]
    return out


def managed_exts() -> set[str]:
    raw = _load().get("managed_extensions")
    if isinstance(raw, list) and raw:
        return {str(x).lower() for x in raw}
    return set(_FALLBACK_EXTS)


def git_allow_zones() -> list[str]:
    """哪些区在判定前还要问一句「git 有没有兜底」。默认只有 `denied`。"""
    raw = (_load().get("git_protected") or {}).get("allow_zones")
    if isinstance(raw, list) and raw:
        return [str(z) for z in raw]
    return list(_FALLBACK_GIT_ZONES)


def is_git_tracked(path: str) -> bool:
    """路径是否已在 git 索引里（`ls-files` 命中）。问一次缓存一次 —— hook 是热路径。

    只看「已入库」，不看有无未提交改动：有改动的文件同样被 git 兜住（改错能 checkout 回来），
    而且要求 clean 会让「刚写过的文件」在第二次写入时突然失去保护。判不了就说不受保护。
    """
    rel = normalize(path)
    if rel in _git_cache:
        return _git_cache[rel]
    try:
        proc = subprocess.run(("git", "ls-files", "-z", "--", rel),
                              cwd=PROJECT_ROOT, capture_output=True, text=True,
                              timeout=GIT_TIMEOUT)
        if proc.returncode != 0:
            raise OSError("git ls-files 失败")
        tracked = bool(proc.stdout.strip("\0"))
    except (OSError, subprocess.SubprocessError):
        tracked = False
    _git_cache[rel] = tracked
    return tracked


def normalize(path: str) -> str:
    """项目相对 POSIX 路径。绝对路径先相对化；`./` 前缀去掉。"""
    raw = str(path).replace("\\", "/")
    if os.path.isabs(raw):
        try:
            raw = os.path.relpath(raw, PROJECT_ROOT)
        except ValueError:
            return raw.replace("\\", "/")
        raw = raw.replace("\\", "/")
    return raw.lstrip("./") if raw.startswith("./") else raw


def zone_of(path: str) -> str:
    """分区表命中结果。返回 `asset` / `memory` / `system` / `scratch`，或 `denied`。

    **纯查表**，不问 git —— 目录级判断（如 `open --workspace` 只收 asset 区）要的是表面事实。
    要「这条路径实际能怎么改」用 `effective_zone`。
    """
    rel = normalize(path)
    for zone, prefix in zone_prefixes():
        if rel == prefix or rel.startswith(prefix + "/"):
            return zone
    return "denied"


def effective_zone(path: str) -> str:
    """实际归属：分区表之外的路径，已入库就算体系区（git 兜底）。

    只升 `denied`，不改 `asset`。资产区即使被 git 兜底也仍是 asset —— 出口验收按
    `zone == "asset"` 挑编译与验证义务，把已入库资产改判成 system 会让那些义务静默消失。
    """
    zone = zone_of(path)
    if zone == "denied" and _git_protected(path, zone):
        return "system"
    return zone


def is_managed(path: str) -> bool:
    """是否受内容规范管辖（只看扩展名）。"""
    return os.path.splitext(normalize(path))[1].lower() in managed_exts()


def under_any(path: str, roots: list[str]) -> bool:
    """路径是否落在任一根之下，按**分隔符边界**匹配（不是前缀字符串）。"""
    rel = normalize(path).rstrip("/")
    for root in roots or []:
        candidate = normalize(root).rstrip("/")
        if not candidate:
            continue
        if rel == candidate or rel.startswith(candidate + "/"):
            return True
    return False


# ═══ 判定 ═══

# 分区判定结论 → 策略里的检查项 id。
#
# 这张表存在的理由：账本要按**策略 id** 记账，否则摩擦报告会把「确实拦过」的检查
# 说成「从未命中」，进而提议退役一条正在工作的检查。信号名给人看，check id 给机器聚合。
_CHECK_IDS = {
    "ZONE_DENIED": "scope.zone",
    "SANDBOX_NOT_SET": "sandbox.asset",
    "SANDBOX_VIOLATION": "sandbox.asset",
}


def check_id_for_error(error: str) -> str:
    """把一次分区拒绝映射到策略检查项。未知错误回落 scope.zone（最接近的兜底）。"""
    return _CHECK_IDS.get(error, "scope.zone")


def _git_protected(rel: str, zone: str) -> bool:
    """这条路径能否靠 git 兜底，从而越过分区规则。"""
    return zone in git_allow_zones() and is_git_tracked(rel)


def evaluate_write(path: str, workspace: list[str]) -> dict:
    """写入前的分区判定。

    Returns:
        {"zone": str, "status": "OK" | "DENIED", "error": str, "hint": str, "record": bool,
         "git_protected": bool}
        `record` 表示该写入是否需要额外留痕（asset / memory / system 都要）。
        `git_protected` 表示「分区规则本来会拒，但已入库有 git 兜底」——只影响提示，不影响区归属。
    """
    rel = normalize(path)
    zone = zone_of(rel)

    if zone == "denied":
        if _git_protected(rel, zone):
            return {
                "zone": "system", "status": "OK", "record": True, "git_protected": True,
                "hint": f"{rel} 不在分区表内，但已入库（git 有兜底），按体系区放行并记录。"
                        f"改错用 git checkout 取回；新增文件仍会被拒 —— 新建没有兜底。",
            }
        return {
            "zone": zone, "status": "DENIED", "error": "ZONE_DENIED", "record": True,
            "hint": f"{rel} 不属于任何可写分区，且不在 git 里（没有兜底）。可写区："
                    f"Assets/Mine（本次沙箱内）、.agents/memory、.agents/{{agents,knowledge,skills}}、"
                    f"tmp；已入库的仓库文件也可写。仓库外与生成目录需要人工决定。",
        }

    if zone == "scratch":
        return {"zone": zone, "status": "OK", "record": False}

    if zone == "memory":
        # 记忆沙箱：随时可写，既有记录也允许原地改（2026-10-28 放宽，历史由 git 兜底）。
        return {"zone": zone, "status": "OK", "record": True}

    if zone == "system":
        return {"zone": zone, "status": "OK", "record": True}

    # ── asset：默认受本次会话沙箱约束（用户下达）；已入库的文件由 git 兜底 ──
    if workspace and under_any(rel, workspace):
        return {"zone": zone, "status": "OK", "record": True}
    if _git_protected(rel, zone):
        return {
            "zone": zone, "status": "OK", "record": True, "git_protected": True,
            "hint": f"{rel} 不在本次沙箱内，但已入库（git 有兜底），放行并记录。"
                    f"要新建文件或改未入库资产，仍需把它加进沙箱。",
        }
    if not workspace:
        return {
            "zone": zone, "status": "DENIED", "error": "SANDBOX_NOT_SET", "record": True,
            "hint": f"本次任务尚未声明沙箱，{rel} 在资产区内且未入库。先运行："
                    f"python3 .mcp/cli.py open --task \"<任务>\" --workspace <用户指定的目录>",
        }
    return {
        "zone": zone, "status": "DENIED", "error": "SANDBOX_VIOLATION", "record": True,
        "hint": f"{rel} 不在本次沙箱内，且未入库（新建或未跟踪，没有 git 兜底）。已声明: {workspace}。"
                f"跨目录共享库在同一次 open 里一并给出；确需另一个目录，请用户在对话框里确认后 "
                f"cli.py expand --workspace <目录> --reason \"<用户确认的原话>\"。",
    }


def evaluate_delete(path: str, workspace: list[str]) -> dict:
    """删除前的分区判定 —— 与写入同一条边界，但语义是「这次删除成不成立」。

    asset 区默认仍须沙箱内；已入库的文件由 git + trash 双重兜底，放行。
    memory / system 允许，由 trash 留底。
    """
    rel = normalize(path)
    zone = zone_of(rel)
    if zone in ("scratch", "memory", "system"):
        return {"zone": zone, "status": "OK"}
    if _git_protected(rel, zone):
        return {"zone": zone, "status": "OK", "git_protected": True}
    if zone == "denied":
        return {"zone": zone, "status": "DENIED", "error": "ZONE_DENIED",
                "hint": f"{rel} 不属于任何可删分区，且不在 git 里。"}
    if not workspace:
        return {"zone": zone, "status": "DENIED", "error": "SANDBOX_NOT_SET",
                "hint": "尚未声明沙箱。先 python3 .mcp/cli.py open ..."}
    if not under_any(rel, workspace):
        return {"zone": zone, "status": "DENIED", "error": "SANDBOX_VIOLATION",
                "hint": f"{rel} 不在本次沙箱内且未入库。已声明: {workspace}。"}
    return {"zone": zone, "status": "OK"}
