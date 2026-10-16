# 2026-09-01 — 门禁 NO_RECIPE 根因 + 状态持久化修复

## 现象

`write_gated` / `gate_pass` 偶发返回 `{"status": "DENIED", "error": "NO_RECIPE"}`。时间线规律：连续快速交互不丢，空闲 ~30min+ 后必丢。

## 根因（已读源码确认）

MCP server 进程重启 = 状态全清。`gate_center.py` 的 `state` 是模块级单例（recipe/passed/contexts/writes 全部进程内存），无任何持久化；Claude Code 会回收空闲的 stdio MCP server 进程，新进程 `state = SessionState()` 从头开始 → `recipe=None` → NO_RECIPE。排除：gate_reset 调用、TTL/超时逻辑（代码不存在）。

## 修复（2026-09-01）

`gate_center.py` 加状态持久化，进程无关：

- `STATE_FILE = .mcp/state.json`，`_save_state()` 原子写（tmp + os.replace），失败静默（持久化是增强，不阻塞门禁流）
- 保存时机：`set_recipe` OK / `pass_gate` OK → 落盘；`reset()` → 删除文件（显式清空）
- 模块底部启动加载：损坏/不可读 → 冷启动（等价旧行为）
- `writes` 审计**不**持久化（会话内；落盘文件本身即持久记录）
- 并发：原子 replace，多实例 last-writer-wins

## 验证

- `uv run python tests/test_recipes.py` 全绿，新增 `test_restart_recovery`（子进程 1 过链 → 子进程 2 模拟重启断言 recipe/passed 恢复 + can_write OK；gate_reset 删文件断言）
- 文档同步：mcp-gate-usage.md 状态模型条目更新
- `.gitignore` 新建：忽略 `.mcp/state.json(.tmp)`

## 遗留认知

- Claude Code 无 MCP server keep-alive 配置（~/.claude.json / .mcp.json 均无此键），服务端持久化是唯一稳妥解法
- 若再次遇到 NO_RECIPE：先 `gate_status()` 确认真空（区别于配方/门禁错误），再 `gate_set_recipe` 重走链

## 追加（同日）— 写-导入竞争：write_gated 原子写

同一竞争类的另一面：`write_gated` 原实现为「截断 + 流式写入」，Editor 文件 watcher 可能在写入中途读到混合态（实证：15:05 瞬时编译错误引用 line 98 的新符号——磁盘当时已是新内容）。已改为原子写：

- 同目录隐藏临时文件 `. <basename>.uetmp`（前导点 Unity 忽略不导入）+ `os.replace` → watcher 只看到完整新内容，mtime 只在 rename 瞬间变化一次
- 失败/中断时 except 清理残留临时文件
- 测试 `test_atomic_write`：内容完整 + 无 .uetmp 残留
- 教训：**任何写入工具对编辑器的写入必须原子**（临时文件 + rename），非原子写 = 瞬时混合态 + 误报
