#!/usr/bin/env bash
# PreToolUse hook — guard dangerous Bash commands
# Reads JSON from stdin: { "tool_name": "Bash", "tool_input": { "command": "..." } }
# Exit 2 = block, Exit 0 = allow
# ⚠️ 本文件与另一平台的 .{platform}/hooks/guard-bash.sh 必须**逐字一致**（本注释也相同）。
#    改一侧必须同步另一侧，否则两平台红线静默分叉；由 .codex/tests/06_architecture/verify.py
#    的 platform_hook_drift 检查守着。规则见 .agents/knowledge/rules/meta-architecture.md。

INPUT=$(cat)
CMD=$(echo "$INPUT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('tool_input',{}).get('command',''))" 2>/dev/null)

# ═══ Hard blocks (exit 2) ═══

# C1: git stash --all permanently forbidden
if echo "$CMD" | grep -q "git stash.*--all"; then
    echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"C1 安全红线: git stash --all 永久禁止。使用 git stash push -- <paths> 指定精确路径。"}}'
    exit 2
fi

# C2: No destructive ops on Assets/Mine/ (rm -rf only, cp/mv allowed)
if echo "$CMD" | grep -qE "rm -rf.*Assets/Mine/"; then
    echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"C2 安全红线: 禁止删除 Assets/Mine/ 下功能代码。清理仅限 tmp/ 和 Screenshots/。"}}'
    exit 2
fi

# C3: 工作目录扩展的批准权在人 —— agent 不得自行 approve/reject
#     （gate_request_workspace 只是登记；批准走 CLI，且 CLI 命令对 agent 封闭）
if echo "$CMD" | grep -qE "workspace[[:space:]]+(approve|reject)|(approve|reject)_workspace"; then
    echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"C3 权限边界: 工作目录扩展须由用户批准。agent 只能 gate_request_workspace 登记请求，批准命令由用户在本机执行。"}}'
    exit 2
fi

exit 0
