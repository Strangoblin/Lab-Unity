"""判定核 — hook 与 CLI 共用的唯一实现。

v3 的教训：判定写在传输层旁边，两条通道迟早分叉（写入长期没有 CLI 就是分叉的结果）。
v4 的通道从「MCP 工具 / Bash CLI」变成「平台 hook / Bash CLI」，这条教训照旧成立 ——
所以判定仍然只有一份：

    platform hook (PreToolUse: Write|Edit)  ─┐
                                             ├─→ decide_write ─→ zones + norms
    CLI (cli.py write / trash)              ─┘

分工：
  zones.py   这个路径属于哪个区、这个区允许怎么改
  norms.py   内容是否符合结构规范（只判新增行）
  本模块     把两者合成一次判定，并把结论落进证据账本
"""

from __future__ import annotations

import os

import session
import zones
from validation import policy
from validation.norms import NORM_RULES, _existing_lines, check_content
from validation.project_paths import CATEGORY_BASE, MANAGED_ROOTS


# ═══ 规范检查的 policy 应用 ═══

def _norm_check_id(violation: dict) -> str:
    return f"norm.{violation.get('id', 'unknown')}"


def _placement_check_id(violation: dict, is_new: bool) -> str:
    if violation.get("id") == "placement-root":
        return "placement.root.new" if is_new else "placement.root.existing"
    return "placement.category"


def _effective(violation: dict, check_id: str) -> str:
    """违规的有效级别（block / warn / off）。"""
    intrinsic = "block" if violation.get("level") == "error" else "warn"
    return policy.level_of(check_id, intrinsic)


# ═══ 落点检查（从 v3 project_paths 收进来，规则不变）═══

def check_placement(path: str, is_new: bool, category: str = "") -> list[dict]:
    """受管文件的落点。新文件是全新决定（error），就地编辑是历史决定（warning）。"""
    rel = zones.normalize(path)
    if not zones.is_managed(rel) or not rel.startswith("Assets/Mine/"):
        return []

    violations: list[dict] = []
    level = "error" if is_new else "warning"
    rest = rel[len("Assets/Mine/"):]
    segments = rest.split("/")
    if len(segments) < 2 or segments[0] not in MANAGED_ROOTS:
        violations.append({
            "id": "placement-root", "level": level, "source": "placement",
            "detail": f"{os.path.splitext(rel)[1]} 应落在 {'/'.join(MANAGED_ROOTS)} 之一的子目录下，"
                      f"当前是 Assets/Mine/{rest}。",
            "path": path,
        })
    if category and category in CATEGORY_BASE:
        base = CATEGORY_BASE[category]
        if not rel.startswith(base + "/") and rel != base:
            violations.append({
                "id": "placement-category", "level": "warning", "source": "placement",
                "detail": f"category='{category}' 对应 {base}/，而路径是 {path}。",
                "path": path,
            })
    return violations


# 这些扩展名由 Unity 引入的换行/注释约定决定，不做行首空白归一。
_SKIP_WHITESPACE_NORMALIZATION = {".hlsl", ".shader", ".compute"}


def _normalize_lines(text: str, path: str) -> set[str]:
    """现有文件的行集合。`.cs` 用去首尾空白的归一形，避免缩进变化被当成「新增行」。

    这是 v3 的一个真问题：已存在的 .cs 只改缩进，`region-added` 会把整段判成新增，
    于是纯格式化被拦。`.shader/.hlsl` 不归一 —— 那些文件的缩进在行内有语义
    （多行宏），归一后反而漏判。
    """
    lines = set(text.splitlines())
    if os.path.splitext(path)[1].lower() in _SKIP_WHITESPACE_NORMALIZATION:
        return lines
    return lines | {ln.strip() for ln in lines}


# ═══ 写入判定 ═══

def decide_write(path: str, new_content: str, old_content: str | None,
                 workspace: list[str]) -> dict:
    """完整写入判定。old_content 为 None 表示新文件（或读不到原内容）。

    Returns:
        {"status": "OK"|"DENIED", "zone", "violations": [...],
         "warnings": [...], "blocking": [...], "is_new": bool}
    """
    zone_result = zones.evaluate_write(path, workspace)
    base = {"zone": zone_result.get("zone"), "path": zones.normalize(path)}
    if zone_result["status"] != "OK":
        return {**base, "status": "DENIED", "error": zone_result["error"],
                "hint": zone_result["hint"], "violations": [], "warnings": []}

    is_new = old_content is None
    violations: list[dict] = []

    if zones.is_managed(path):
        if old_content is None:
            existing = set()
        else:
            existing = _normalize_lines(old_content, path)
        norm = check_content(path, new_content, existing=existing)
        violations += [v for v in norm["errors"] + norm["warnings"]]
    violations += check_placement(path, is_new=is_new)

    blocking, warnings, muted = [], [], []
    for violation in violations:
        check_id = (_placement_check_id(violation, is_new)
                    if violation.get("source") == "placement"
                    else _norm_check_id(violation))
        level = _effective(violation, check_id)
        item = {**violation, "check": check_id}
        if level == "block":
            blocking.append(item)
        elif level == "warn":
            warnings.append(item)
        else:
            muted.append(item)

    if blocking:
        first = blocking[0]
        return {**base, "status": "DENIED", "error": "NORM_VIOLATION",
                "violations": blocking + warnings,
                "hint": f"{first.get('detail', '')} 修正后重试。"}

    out = {**base, "status": "OK", "is_new": is_new, "warnings": warnings}
    if muted:
        out["muted"] = muted
    return out


def record_write(path: str, zone: str, source: str, result: dict) -> None:
    """把写入结论落进证据账本。仅被判定的写入才记 —— 读操作不产生条目。"""
    extra = {}
    if result.get("violations"):
        extra["violations"] = [
            {"id": v.get("id"), "check": v.get("check"), "detail": v.get("detail", "")}
            for v in result["violations"]]
    elif result.get("warnings"):
        extra["violations"] = [
            {"id": v.get("id"), "check": v.get("check"), "detail": v.get("detail", ""),
             "level": "warning"} for v in result["warnings"]
            if v.get("check", "").startswith(("norm.", "placement."))]
    session.record_write(path, zone, source, extra)


# ═══ 删除判定 ═══

def decide_delete(paths: list[str], workspace: list[str]) -> dict:
    """删除判定：分区边界 + 形状（软链/作用域根）。内容安全交给 trash。"""
    denied = []
    for p in paths:
        if not zones.normalize(p):
            denied.append({"path": p, "error": "EMPTY_PATH", "hint": "路径为空。"})
            continue
        result = zones.evaluate_delete(p, workspace)
        if result["status"] != "OK":
            denied.append({"path": p, **result})
    if denied:
        return {"status": "DENIED", "error": denied[0].get("error", "ZONE_DENIED"),
                "blocked": denied,
                "hint": denied[0].get("hint", "路径不在可删分区或本次沙箱内。")}
    return {"status": "OK"}
