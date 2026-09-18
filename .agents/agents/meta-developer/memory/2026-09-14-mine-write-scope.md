---
name: Unity 门禁扩展至 Assets/Mine
description: 统一共享 HLSL 与其他 Mine 资源的门禁可写范围
date: 2026-09-14
---

# Unity 门禁可写范围

- 用户授权将业务写入范围扩展为 `Assets/Mine/` 全部子目录。
- 修改 `.mcp/validation/project_paths.py`，保留 Roslyn 与 tmp 原有范围。
- 同时验证词法路径与真实路径的目录边界，拒绝相似前缀、路径穿越和软链越界。
- Claude/Codex 共用同一规则；平台层已有 `Assets/Mine/**` 门禁约束，无需复制配置。
- 补充现有路径契约测试及 `.mcp/README.md`；两个 MCP 测试入口和架构 strict 检查通过。
- 按 P1-P3 在现有实现与测试中修改，避免新增重复规则或独立配置源。
