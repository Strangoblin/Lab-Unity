#!/usr/bin/env python3
"""平台 hook — 在工具调用发生前做业务判定，调用后留证据。

两个平台共用本文件（两边的 hook 注册都只做薄包装，见 .mcp/hooks.sh），
因为两边的事件名与字段完全一致：`hook_event_name` / `tool_name` / `tool_input`。

支持的事件：
  PreToolUse   Write / Edit / NotebookEdit  → 分区 + 规范判定，可 deny
               Bash                          → 红线 + 资产区删除改走 trash
  PostToolUse  Write / Edit / NotebookEdit  → 记录写入（证据账本）
               Read                          → 记录真实读取（取代「申报已读」）

分区判定的兜底口径（v5）：分区表之外或沙箱之外的路径，先问 git `ls-files` 一次 ——
已入库就放行并记录，未入库才拒。**兜底是版本库，不是信任**：改错一条 `git checkout` 就回来。

**失败即拒绝**：判定核自身出错时，资产区一律 deny（宁可挡住一次正常写入，也不放过一次
越界或坏内容）；记忆区与暂存区放行 —— 那里的错误由 git 和测试兜住，卡死收尾没有收益。
settings 里同时配 `"onFailure": "block"`，保证本进程都没起来时也不静默放行。

用法（由平台调用，不手工执行）:
    echo '<hook json>' | python3 .mcp/lib/hooks.py PreToolUse
"""

from __future__ import annotations

import json
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import judgement                                        # noqa: E402
import session                                          # noqa: E402
import zones                                            # noqa: E402
from telemetry import record as emit                    # noqa: E402

WRITE_TOOLS = {"Write", "Edit", "NotebookEdit"}


# ═══ 输出契约 — 两个平台同形 ═══

def _emit_decision(decision: str, reason: str = "", context: str = "") -> None:
    payload: dict = {"hookSpecificOutput": {
        "hookEventName": current_event,
        "permissionDecision": decision,
        "permissionDecisionReason": reason,
    }}
    if context:
        payload["hookSpecificOutput"]["additionalContext"] = context
    print(json.dumps(payload, ensure_ascii=False))


def _deny(reason: str) -> None:
    _emit_decision("deny", reason)


def _allow(context: str = "") -> None:
    _emit_decision("allow", "", context)


# ═══ 工具输入 → (路径, 新内容, 旧内容) ═══

def _resolve_edit(file_path: str, old: str, new: str, replace_all: bool) -> tuple[str | None, str]:
    """把一次 Edit 应用成「结果内容」。读不到原文件时返回 None（交由调用方降级）。"""
    full = os.path.join(zones.PROJECT_ROOT, zones.normalize(file_path))
    try:
        with open(full, encoding="utf-8") as f:
            original = f.read()
    except OSError:
        return None, ""
    if replace_all:
        if old not in original:
            return None, original
        return original.replace(old, new), original
    if old not in original:
        return None, original
    return original.replace(old, new, 1), original


def _extract_write_input(tool_name: str, tool_input: dict) -> dict | None:
    """返回 {path, new_content, old_content}；形状不认识时返回 None。"""
    path = tool_input.get("file_path") or tool_input.get("path") or ""
    if not path:
        return None

    if tool_name == "Write":
        content = tool_input.get("content")
        if not isinstance(content, str):
            return None
        old = None
        full = os.path.join(zones.PROJECT_ROOT, zones.normalize(path))
        if os.path.isfile(full):
            try:
                with open(full, encoding="utf-8") as f:
                    old = f.read()
            except OSError:
                old = None
        return {"path": path, "new_content": content, "old_content": old}

    if tool_name == "Edit":
        old_str = tool_input.get("old_string")
        new_str = tool_input.get("new_string")
        if not isinstance(old_str, str) or not isinstance(new_str, str):
            return None
        result, original = _resolve_edit(path, old_str, new_str,
                                         bool(tool_input.get("replace_all")))
        if result is None:
            # 应用不了（原内容读不到 / old_string 不匹配）→ 交给工具自己报错，
            # 这里只做分区判定，不做内容判定。
            return {"path": path, "new_content": None, "old_content": None}
        return {"path": path, "new_content": result, "old_content": original}

    if tool_name == "NotebookEdit":
        # 笔记本是 JSON 结构，不能按行判规范；只做分区判定。
        return {"path": path, "new_content": None, "old_content": None}

    return None


# ═══ Bash — 红线与删除重定向 ═══

_REDLINES = (
    (r"git\s+stash.*--all",
     "C1 安全红线: git stash --all 永久禁止。用 git stash push -- <paths> 指定精确路径。"),
    (r"rm\s+-rf.*Assets/Mine/",
     "C2 安全红线: 禁止 rm -rf Assets/Mine/。清理走 python3 .mcp/cli.py delete —— 会留底且可恢复。"),
    (r"\.mcp/(trash|friction|deletes|moves)\.jsonl?.*\brm\b|\brm\b.*\.mcp/(session|state)\.json",
     "证据账本不可删除 —— 它是「发生了什么」的唯一痕迹。要清账用测试 runner 的隔离环境。"),
)

# 只有**删除**必须走留底通道。移动 / 改名 / 重定向一律用原生命令：
# 2026-10-28 用户裁决「移动等其他操作也尽量让 agent 用原生工具自己跑，删除用垃圾桶倒也行」。
# `mv` 会顺带搬走 `.meta`，GUID 随文件走，本来就安全；拦着它只是逼一次 cli.py 往返。
_DELETE_BASH = r"\b(rm|unlink|rmdir|shred|truncate)\b"

# 删除的**目标**里出现资产区路径，才算动了资产 —— 命令里出现路径但目标是 tmp 的不算。
#
# 早先的写法是「命令里出现 Assets/Mine 且出现 rm」—— 这会把
# `rm -f tmp/x && python3 .mcp/cli.py delete Assets/Mine/Old.shader` 这类
# 正常清理一起拦下，而那条命令恰恰是推荐的写法。判定要看**参数**，不是整行。
_ASSET_TARGET = r"Assets/Mine\b"


def _bash_guard(command: str) -> str | None:
    """返回拒绝理由；None 表示放行。"""
    import re
    for pattern, reason in _REDLINES:
        if re.search(pattern, command):
            return reason
    # 逐个简单命令看：删除动词后面是否跟着资产区路径
    for segment in re.split(r"&&|\|\||;|\n", command):
        segment = segment.strip()
        if not segment or "cli.py" in segment:
            # 走 cli.py 的 delete/move 是留底通道，不该被这条规则挡住
            continue
        match = re.search(_DELETE_BASH, segment)
        if match and re.search(_ASSET_TARGET, segment[match.end():]):
            return ("资产区的**删除或清空**要走留底通道 —— 直接动手不留底，也没法 restore。"
                    "用 python3 .mcp/cli.py delete <路径...> --reason <原因>（进垃圾桶，可 restore）；"
                    "只改内容是 Write / Edit 的事，不必走 Bash。"
                    "移动 / 改名直接用 mv（.meta 一并搬，GUID 不变）。")
    return None


# ═══ PreToolUse ═══

def handle_pre(payload: dict) -> int:
    tool_name = payload.get("tool_name", "")
    tool_input = payload.get("tool_input") or {}
    data = session.load()
    workspace = data.get("workspace", [])

    if tool_name == "Bash":
        reason = _bash_guard(str(tool_input.get("command", "")))
        if reason:
            emit(op="bash", tool="hook", outcome="denied", signal="deny.bash.redline",
                 check="bash.redline", detail=reason)
            _deny(reason)
        else:
            _allow()
        return 0

    if tool_name not in WRITE_TOOLS:
        _allow()
        return 0

    extracted = _extract_write_input(tool_name, tool_input)
    if extracted is None:
        # 形状不认识：只做分区判定，内容判定交给工具自身与后续编译。
        path = tool_input.get("file_path") or tool_input.get("path") or ""
        zone = zones.effective_zone(path) if path else "denied"
        if zone == "asset" and not workspace:
            _deny("本次任务尚未声明沙箱。先运行 "
                  "python3 .mcp/cli.py open --task \"<任务>\" --workspace <用户指定的目录>")
            return 0
        emit(op="write", tool="hook", outcome="degraded", signal="degraded.shape",
             path=str(path), detail=f"未识别的工具输入形状: {tool_name}")
        _allow("hook 未能解析本次写入形状，只做了分区判定。")
        return 0

    if extracted["new_content"] is None:
        result = zones.evaluate_write(extracted["path"], workspace)
        if result["status"] != "OK":
            # check 落策略 id —— 摩擦报告按 check 聚合，只记信号名会让它以为这条检查从未命中
            emit(op="write", tool="hook", outcome="denied", signal=f"deny.{result['error']}",
                 check=zones.check_id_for_error(result["error"]),
                 path=extracted["path"], detail=result.get("hint", ""))
            _deny(result.get("hint", "分区拒绝"))
        else:
            session.record_write(extracted["path"], result["zone"], f"hook:{tool_name}")
            _allow(result.get("hint") or "")
        return 0

    result = judgement.decide_write(extracted["path"], extracted["new_content"],
                                    extracted["old_content"], workspace)

    if result["status"] != "OK":
        violations = result.get("violations") or [{}]
        emit(op="write", tool="hook", outcome="denied",
             signal=f"deny.{result.get('error', 'write')}",
             # 分区拒绝没有 violations，check 要回落到分区检查项
             check=(violations[0].get("check")
                    or zones.check_id_for_error(result.get("error", ""))),
             path=extracted["path"], detail=result.get("hint", ""))
        _deny(result.get("hint") or "写入被拒绝")
        return 0

    judgement.record_write(extracted["path"], result.get("zone", ""), f"hook:{tool_name}",
                           result)
    emit(op="write", tool="hook", outcome="ok", signal="ok.write",
         path=extracted["path"], recipe="", detail="")

    context = ""
    if result.get("warnings"):
        items = "；".join(f"{w.get('id')}: {w.get('detail', '')}" for w in result["warnings"][:3])
        context = f"规范提醒（不阻断）: {items}"
    _allow(context)
    return 0


# ═══ PostToolUse ═══

def handle_post(payload: dict) -> int:
    tool_name = payload.get("tool_name", "")
    tool_input = payload.get("tool_input") or {}
    path = tool_input.get("file_path") or tool_input.get("path") or ""

    if tool_name == "Read" and path:
        session.record_read(path, source="hook:Read")
        emit(op="read", tool="hook", outcome="ok", signal="ok.read", path=str(path))
        return 0

    if tool_name in WRITE_TOOLS and path:
        # PreToolUse 已记过（含判定结论）；PostToolUse 只补一条「确实落盘了」，
        # 以便区分「被允许但工具失败」与「真的写成了」。
        response = payload.get("tool_response") or {}
        failed = bool(payload.get("error")) or (
            isinstance(response, dict) and response.get("is_error"))
        if not failed:
            session.record_write(path, zones.effective_zone(path), f"hook:{tool_name}:applied")
    return 0


# ═══ 入口 ═══

current_event = "PreToolUse"


def main(argv: list[str]) -> int:
    global current_event
    if not argv:
        print("usage: hooks.py <PreToolUse|PostToolUse>  (reads hook JSON on stdin)",
              file=sys.stderr)
        return 2
    current_event = argv[0]

    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
    except ValueError:
        print("hook input 不是合法 JSON", file=sys.stderr)
        return 2

    try:
        if current_event == "PreToolUse":
            return handle_pre(payload)
        if current_event == "PostToolUse":
            return handle_post(payload)
        return 0
    except Exception as e:                      # noqa: BLE001 — 兜底必须覆盖一切
        tool_name = payload.get("tool_name", "")
        tool_input = payload.get("tool_input") or {}
        path = str(tool_input.get("file_path") or tool_input.get("path") or "")
        try:
            # 这张表只用来决定「失败时往哪边倒」，问 git 失败就落到 denied（保守侧）。
            zone = zones.effective_zone(path) if path else "denied"
        except Exception:                       # noqa: BLE001 — 兜底里的兜底
            zone = "denied"

        emit(op="hook", tool="hooks.py", outcome="error", signal="error.hook",
             path=path, detail=f"{type(e).__name__}: {e}")

        # 资产区出错 → 拒绝（可逆：报错改一条消息即可重走）。
        # 记忆区 / 暂存区出错 → 放行，否则一次判定核的 bug 会卡死收尾。
        if current_event == "PreToolUse" and zone in ("asset", "denied"):
            _deny(f"hook 判定失败（{type(e).__name__}），资产区按失败即拒处理。"
                  f"详情: {e}")
            return 0
        _allow()
        return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
