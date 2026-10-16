---
name: unity-gate-mcp-server
description: Unity Gate MCP Server — 配方驱动门禁系统，将 Harness 门禁从软指令升级为 MCP 工具层硬校验
date: 2026-08-07
metadata:
  type: project
---

# Unity Gate MCP Server

## 动机

.claude 门禁体系（G0-G3）依赖 agent 读取 markdown 自觉执行。Claude Code hooks 无状态，无法追踪"agent 是否通过了 G0"。MCP server 维护会话状态，在工具调用层硬拒绝未通过门禁的操作。

## 架构

三层解耦 — 对标 Traefik Triple Gate 可组合安全管道：

```
Gate Center (gate_center.py) → 注册表 + 配方表 + 状态追踪（不知道门禁逻辑）
  └── Recipes (配方)          → 门禁 ID 有序列表（不关心门禁实现）
        └── Gates (gates/*.py) → check(ctx) → pass | fail（只关心自身逻辑）
```

## 配方

| 配方 | 门禁链 |
|------|--------|
| Production | g_entry → g_mode → g_script → g_file |
| Research | g_entry → g_mode |
| Experiment | g_entry → g_mode → g_web_search → g_plan → g_script → g_file |

## 技术栈

- Python 3.11 (uv 管理) + mcp SDK (MCPServer)
- 7 个 MCP 工具：gate_set_recipe, gate_pass, gate_status, gate_list, gate_reset, script_list, write_gated
- 6 个独立门禁模块：g_entry, g_mode, g_script, g_file, g_web_search, g_plan
- .mcp.json 注册，Claude Code 自动加载
- settings.json deny Write/Edit(Assets/Mine/**) → 强制走 write_gated

## 项目位置

/Users/xiaokangji/Unity/Lab/.mcp/
/Users/xiaokangji/Unity/Lab/.mcp.json
