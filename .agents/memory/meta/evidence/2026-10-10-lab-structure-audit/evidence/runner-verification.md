# 测试运行器验证（2026-10-10）

所有命令均从仓库根运行。旧文件未删除/移动；只清理本轮 runner 创建并拥有的 fixture。

## 结果

| 检查 | 结果 |
|---|---|
| 隔离 runner 回归 | 9/9，通过 |
| MCP `test_recipes.py` | 30 个测试函数与注册表 self-check，通过 |
| MCP `test_knowledge_paths.py` | 5/5，通过 |
| `run_all.sh` 语法 | 通过 |
| runner 副本：成功/失败/未知 suite、3 轮异结果并发、临时目录清理 | 10/10，通过 |
| 真实 `.mcp` state / deletes / moves / friction 前后 SHA-256 | 4/4 相同 |

隔离 MCP 项目创建于本任务 `fixtures/`，独立 Git 根和 TMPDIR；成功后由 `finally` 清除，日志/报告保留。新增回归覆盖 fixture 落到 `fixtures/` 及该目录的 symlink 逃逸拒绝。

## 命令与证据

```sh
.mcp/.venv/bin/python -B .mcp/tests/test_isolated_runner.py
.mcp/.venv/bin/python -B .mcp/tests/run_isolated.py --task-dir tmp/sessions/2026-10-10-lab-structure-audit
bash -n .codex/tests/run_all.sh
git diff --check -- .mcp/tests/run_isolated.py .mcp/tests/test_isolated_runner.py .codex/tests/run_all.sh
```

- MCP 报告：[report.json](../output/mcp-isolated-a93d002a970b/report.json)
- MCP 30 个测试的日志：[test_recipes.log](mcp-isolated-a93d002a970b/test_recipes.log)
- MCP 知识路径日志：[test_knowledge_paths.log](mcp-isolated-a93d002a970b/test_knowledge_paths.log)
- Bash runner 副本控制结果：[runner-verification-run-all-stubs.json](runner-verification-run-all-stubs.json)

控制测试只在 task fixture 中复制 `run_all.sh`，用退出 0 / 7 的 stub suite 验证汇总退出码和并发独立性；共享的临时父目录最终无 `codex-tests.*` 残留。未运行会写真实 `Assets` 的 Codex suite，因此本次结果不代表 Codex CLI、Unity 编译或 GPU 验证。
