#!/bin/bash
# Codex 链路测试集运行器（直连方案，2026-08-17 起无本地代理）
# 用法: ./run_all.sh [测试编号]  (省略则跑全部)

cd "$(dirname "$0")" || exit 1

# 每次运行拥有独立退出码目录，并在正常退出或中断时清掉自己的临时文件。
RUN_TMP=$(mktemp -d "${TMPDIR:-/tmp}/codex-tests.XXXXXXXX") || exit 1
trap 'rm -rf -- "$RUN_TMP"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

TESTS=(01_smoke 02_readonly 03_write 04_batch 05_spec 06_architecture)
if [ -n "$1" ]; then TESTS=("$1"); fi

PASS=0; FAIL=0
for t in "${TESTS[@]}"; do
  if [ ! -d "$t" ]; then echo "❌ 未知测试: $t"; exit 1; fi
  echo ""
  echo "════════════ $t ════════════"
  # 进入测试目录作为工作区（workspace-write 只影响这里，保护项目源码）
  ( cd "$t" && bash ./run.sh >/dev/null 2>&1; echo $? > "$RUN_TMP/exit-code" ) &
  TEST_PID=$!
  wait "$TEST_PID"
  RESULT=$(cat "$RUN_TMP/exit-code" 2>/dev/null) || RESULT=1
  if [ "$RESULT" == "0" ]; then
    echo "✅ $t PASS"
    PASS=$((PASS+1))
  else
    echo "❌ $t FAIL (see $t/run.log)"
    FAIL=$((FAIL+1))
  fi
done

echo ""
echo "════════════ 结果: $PASS PASS / $FAIL FAIL ════════════"
[ "$FAIL" == "0" ]
