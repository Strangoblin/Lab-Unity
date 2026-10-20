---
name: gate-path-windows-regression
description: 修复 validate_path 在 Windows 上的分隔符比较回归 —— Assets/Mine 全域被误判 PATH_NOT_ALLOWED（35a2fd6 于 2026-09-18 引入；Codex 2026-09-26 报告）。
date: 2026-10-05
---

# 门禁路径比较 Windows 回归修复

## 症状与影响

- `write_gated` / `delete_gated` / `move_gated`（MCP 与 Bash CLI 两通道）对 `Assets/Mine/**` 一律返回
  `PATH_NOT_ALLOWED`，而提示里列出的合法目录第一个就是 `Assets/Mine/`。
- Codex 在「设计 TerrainManager 地形生成框架」会话（2026-09-26 起）三度记录该异常，改用补丁直写绕过；
  同日合并会话 `test_write_gated_placement_wired` 期望 `NORM_VIOLATION`、实际 `PATH_NOT_ALLOWED`，
  亦同因未修（见 [[local-remote-merge]]）。
- 该通道是 Assets/Mine 写入的唯一合法门禁路径（原生 Write/Edit 被 settings deny 且连带 Bash），
  等于对本机全域封死。

## 根因（35a2fd6, 2026-09-18 引入）

- `validate_path` 的前缀 `os.path.join(PROJECT_ROOT, "Assets/Mine")` 在 Windows 上是**混合分隔符**
  字符串（`...\Lab\Assets/Mine`），而 `os.path.commonpath` 返回全反斜杠形；两者 `==` 恒为 False。
  旧写法 `full.startswith(os.path.normpath(prefix))` 无此问题——重写为 commonpath 语义时丢了归一化。
- `tmp/` 与 roslyn 前缀恰好不含内部正斜杠，侥幸通过：这是「端到端正向用例全落在 tmp/」盲区的来源。

## 修复

- `project_paths.py` 新增 `_canonical()`（abspath + normpath + normcase），比较两侧先归一；
  `commonpath` 跨盘 `ValueError` 由崩溃改为按拒绝处理。
- `test_recipes.py` 新增 `test_validate_path_scope()`（相对/绝对/混合分隔符/盘符大小写正向 +
  前缀扩展、越界反向）与 `test_assets_mine_prefix_gated_ops()`（三通道端到端写 → 移 → 删；
  探针用 `.` 前缀文件名，Unity 忽略导入，零残留）。
- `test_knowledge_paths.py` 首个断言由字符串子串改为 `Path` 相等：Windows 上正斜杠子串恒失败，
  掩蔽了该文件内本可抓住本次回归的 Assets/Mine 作用域用例；2026-09-21 memory 声称其 PASS 与实际不符。

## 验证

- `.mcp/tests/test_recipes.py` 全绿（含新用例）；`.mcp/tests/test_knowledge_paths.py` PASS；
  `.codex/tests/06_architecture/verify.py --strict` PASS；端到端探针实测三通道放行且无残留。
- 运行环境：Windows 下用 `PYTHONUTF8=1 uv run --directory .mcp python tests/...`；
  若 shell 另设 `PYTHONIOENCODING=utf-8`，`text=True` 子进程读取线程会按 GBK 解码 UTF-8 输出而崩溃
  （测试外壳脆弱点，本次未改，仅记录）。

## 遗留（待裁决）

- `.codex/config.toml` 向 Codex 暴露 write/delete/move 三工具，与 `.codex/AGENTS.md:45,59`
  「只有 Claude 侧持有」的声明矛盾；本次修复后 Codex 在机制上已可完整使用三通道。
  与 memory 索引「Codex 是否可用 Bash 删除通道」同属政策裁决，未擅动。
