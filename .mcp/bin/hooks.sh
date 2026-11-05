#!/usr/bin/env bash
# PreToolUse / PostToolUse 薄传输 —— 判定本身全在 .mcp/lib/hooks.py。
#
# 为什么还要这一层：判定核起不来时（python3 缺失、语法错误、被信号杀掉），
# 平台默认**放行**。资产区的写入不该因为判定核故障而静默通过，所以这里把
# 「拿不到判定」翻译成 deny。设置里的 onFailure: block 只覆盖超时与崩溃，
# 覆盖不到「进程活着但吐了非 JSON」，这一层把两种情况一并兜住。
#
# 单文件放 .mcp/bin/ 而不是两个平台各一份：一份就不存在漂移（v3 的两份
# guard-bash.sh 靠逐字一致维持等价，那是补丁）。
#
# 退出码: 0 = 已给出判定（JSON 在 stdout）；2 = 输入不可解析（平台按失败处理）
set -u

EVENT="${1:-PreToolUse}"
# Windows 的 `python3` 常是应用商店占位符（能 command -v 找到但跑不起来），
# 逐候选实测能否启动解释器；都不行时 PreToolUse 按故障处理。
PYTHON=""
for _c in python3 python; do
    if command -v "$_c" >/dev/null 2>&1 && "$_c" -c 'import sys' >/dev/null 2>&1; then
        PYTHON="$_c"
        break
    fi
done
if [ -z "$PYTHON" ]; then
    [ "$EVENT" = "PreToolUse" ] && exit 2
    exit 0
fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PAYLOAD="$(cat)"

OUT=$(printf '%s' "$PAYLOAD" | "$PYTHON" "$HERE/../lib/hooks.py" "$EVENT" 2>/dev/null)
STATUS=$?

# 判定核正常返回且吐出了 JSON → 原样交给平台
if [ "$STATUS" -eq 0 ] && case "$OUT" in \{*) true ;; *) false ;; esac; then
    printf '%s\n' "$OUT"
    exit 0
fi

# 读操作与收尾观测：判定核故障不该拦下读取，也不该拦下已经落盘的写入。
if [ "$EVENT" != "PreToolUse" ]; then
    exit 0
fi

# PreToolUse：把路径问出来，只有资产区与界外才 deny。
PATH_HINT=$(printf '%s' "$PAYLOAD" | "$PYTHON" -c '
import json, sys
try:
    d = json.load(sys.stdin)
except Exception:
    sys.exit(0)
ti = d.get("tool_input") or {}
# Windows 下平台给的是反斜杠路径，而下面的分区模式按 POSIX 分隔符写。
print((ti.get("file_path") or ti.get("path") or "").replace(chr(92), "/"))
' 2>/dev/null)

case "$PATH_HINT" in
    *Assets/Mine/*|*Assets/*|/Library/*|/Temp/*)
        printf '%s\n' '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"写入判定核未能给出结论（python3 / lib/hooks.py 故障）。资产区按失败即拒处理 —— 修好判定核再重试，不要绕过。"}}'
        exit 0
        ;;
esac

# 非资产区（记忆库、体系库、tmp）：判定核故障时不卡死收尾。
exit 0
