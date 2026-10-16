---
name: gate-recipe-layering
description: 2026-08-25 — 按模式分层门禁链（Quick 不再零门禁、g_knowledge 每链必含、dormant 语义、g_script NONE）
date: 2026-08-25
metadata:
  type: project
---

# 按模式分层的门禁链收敛

## 背景 — 三面矛盾

8-24 audit 后门禁体系处于「文档允许 + 权限禁止 + 工具不可达」三面矛盾：
- CLAUDE.md 文档化 Quick 通道（原生直写）
- settings.json deny 堵死 Assets/Mine/ 原生写入
- 替代通道 MCP write_gated 从未被批准连接（`~/.claude.json` 中 unity-gate 零痕迹）

## 用户决策（2026-08-25 确认，7 条）

1. 所有 Assets/Mine/ 写入都走门禁（write_gated）——**g_knowledge 是每链必含门禁**
2. 只有 Production 走完整 5 门禁链
3. 非生产模式统一轻链：Research/Experiment/Debug = `[g_entry, g_knowledge, g_file]`
4. Quick 保留为最短合规链 `[g_entry, g_knowledge]`（不再零门禁）
5. Experiment 的 g_web_search/g_plan 模块保留注册但退出配方（dormant），E1 流程继续文档驱动
6. deny 保留；MCP 必须接线（用户批准连接）
7. （隐含）g_script 支持 NONE 动作——纯 shader/C# 改动显式通过 G2

## Before / After 配方表

| 配方 | Before | After |
|------|--------|-------|
| Production | g_entry → g_mode → g_knowledge → g_script → g_file | 不变（完整链） |
| Research | g_entry → g_mode | g_entry → g_knowledge → g_file |
| Experiment | g_entry → g_mode → g_web_search → g_plan → g_script → g_file | g_entry → g_knowledge → g_file（web_search/plan dormant） |
| Debug | g_entry → g_mode → g_script | g_entry → g_knowledge → g_file |
| Minimal | g_entry | g_entry → g_knowledge |
| Quick | []（零门禁绿色通道） | g_entry → g_knowledge（最短合规链） |

## requires 变更

| 门禁 | Before | After |
|------|--------|-------|
| g_knowledge | [g_mode] | **[g_entry]**（轻链无 g_mode） |
| g_script | [g_mode] | **[g_knowledge]** |
| g_file | [g_mode] | **[g_knowledge]** |
| g_web_search | [g_mode] | **[g_entry]**（dormant 对内部自洽） |
| g_plan | [g_web_search] | 不变 |

自洽性已验证：每链中每门禁的 requires ⊆ 链中排它之前的门禁集（测试自检断言防回归）。

## NONE 动作

- `validate_decision` 新增 `NONE` 分支（宽容实现，允许 "NONE 附带说明"）→ `{"status": "OK", "action": "NONE", "mode": "none"}`
- 纯 shader 改动走 Production 链必须显式通过 g_script；`script-decision.md` 文档早已预留 NONE 语义
- SKILL.md 链感知映射中 G2 标注「无脚本任务 Decision="NONE"」

## dormant 语义

- g_web_search / g_plan 保留在 GATE_REGISTRY（注册），但不在任何配方中
- 调用返回 `GATE_NOT_IN_RECIPE`（不再是 INVALID_GATE）
- E1 WebSearch→Plan 继续文档驱动，无 MCP 同步
- 测试自检断言「dormant 不在任何配方」防回归

## 接线状态（2026-08-25 修复）

**根因（MCP 从未成功启动过，8-24 建好后从未连上）**：`server.py` 用 mcp **1.x** 的 `server.run(read_stream, write_stream)`，但 pyproject 锁的是 `mcp[cli]>=2.0.0`。2.x 的 `MCPServer` 无两个流参数的 run()——真实异常是 `TypeError: MCPServer.run() takes from 1 to 2 positional arguments but 3 were given`，被 `except Exception` 吞成 "unhandled errors in a TaskGroup" 泛化消息。**修复**：改用 2.x 内置入口 `await server.run_stdio_async()`（内部 stdio_server + create_initialization_options，capabilities 自动含 tools），删掉手写的 stdio 管道。握手验证：initialize 应答 unity-gate/0.4.0 + 7 工具全列出 + 进程存活。

接线：`enabledMcpjsonServers: ["unity-gate"]` 放在 **settings.local.json 顶层是合法字段**（schema 有），无需转 mcpServers（schema 无此字段）。待用户重启 Claude Code → `/mcp` 确认 connected → `gate_list` 可调 → 走链 write_gated。

诊断手法备忘：mcp SDK 崩溃常被主 except 吞成 TaskGroup 泛化消息——用「直接调 server.run + catch ExceptionGroup 打印子异常 traceback」破案。

相关：[[mcp-gate-audit]]（8-24，历史快照不改）
