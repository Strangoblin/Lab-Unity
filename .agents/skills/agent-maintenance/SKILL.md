---
name: agent-maintenance
description: 执行 agent / skill / rule / reference / 平台适配维护、索引和引用核验、迁移及模板 API 验证。适用于 .agents/.claude/.codex/.mcp 体系任务；Unity 业务实施使用对应开发技能。
---

# Agent Maintenance

承载体系维护的具体操作、脚本与验证；角色通过 [职责契约](../../knowledge/meta/architecture.md) 索引本能力并记录结果。

| 任务 | 按需入口 |
|---|---|
| 角色 / 技能 / 规则 / 适配维护、迁移与引用检查 | [维护流程](references/maintenance.md) |
| 模板 API 核验 | [验证能力与控制组](references/template-validation.md) |
| 脚本用途 / 入口 | [scripts](scripts/README.md) |
| 新增角色定义 | [角色模板](assets/agent-template.md) |

体系内容直接维护，不调用 Unity 业务门禁；需要业务改动时取用 unity-developer 对应技能，保持任务授权范围。

存量删除 / 移动须根 AGENTS.md 规定的精确清单确认；不自行调整门禁策略级别或放宽平台权限。历史记录不改写；共享规范遵循 [meta-architecture](../../knowledge/rules/meta-architecture.md)，项目资源通过 [输入契约](../../knowledge/meta/interfaces/project-resources.md) 取用。

交付实际改动、引用 / 工具验证、兼容状态与待办，由角色回写 memory。任务产物按 [生命周期](../../knowledge/meta/interfaces/task-artifacts.md) 归属。
