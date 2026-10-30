# Meta — 已归档记忆

> 被结构自检退役的记录在此逐日留一句。正文已删，完整内容在 git 与 `.mcp/trash/`；
> 「谁在何时删了什么」见 `.mcp/state/deletes.jsonl`。

| 日期 | 当天做了什么 | 归档记录 | 去向 |
|------|-------------|---------|------|
| 2026-07-28 | `.claude` 7 层闭环架构初始化；同日补全参考库（urp-shader-lib / unity6-api / platform / templates / meta） | architecture-init, references-bootstrap | [architecture](../../knowledge/meta/architecture.md) |
| 2026-08-03 | Experiment Mode 加入：WebSearch → Plan → 自主迭代，取消自动 memory 写入 | experiment-mode | [modes/experiment.md](../../skills/auto-manager/modes/experiment.md) |
| 2026-08-07 | `.claude` 按 Mode / Capability / Routing 三层解耦；同日起 Unity Gate MCP Server 以配方驱动门禁 | ecs-decoupling, mcp-server | [harness-patterns](../../knowledge/meta/harness-patterns.md)；门禁已于 v4 拆除 |
| 2026-08-24 | 知识库从 `Assets/MarkDowns/` 内化进体系；references 归 agent、rules 瘦身；移除截图环节改人工观测 | knowledge-internalization, mcp-gate-audit, references-relocation, screenshot-removal | [architecture](../../knowledge/meta/architecture.md)、[enforcement-policy](../../knowledge/meta/enforcement-policy.md) |
| 2026-08-25 | 门禁收敛为后果验证（v2）并按模式分层；Codex 改为直接读权威源 | gate-consequence-verification, gate-recipe-layering, codex-direct-read, no-screenshot-validation | [enforcement-policy](../../knowledge/meta/enforcement-policy.md)、[codex-bridge](../../skills/codex-bridge/SKILL.md) |
| 2026-08-27 | feature-screen-debug 临时脚本退役，屏幕调试固定为 DebugOutputFeature | feature-screen-debug-retired | [debug-resource-layout](../../knowledge/meta/debug-resource-layout.md) |
| 2026-10-16 | 写入通道按域拆分：体系库走平台工具、不经 Unity 门禁；修正「唯一操作通道」表述 | write-channel-rule | [architecture](../../knowledge/meta/architecture.md)、[memory/README](../README.md) |
| 2026-10-30 | 垃圾桶清空 15 桶：v3/v4 退役模块 3 桶、隔离测试中间产物 11 桶、旧 guard-bash 1 桶 | trash:125504/125519/142021/141553/143210/143405/152610×4/152654/184748×4 | 内容不留，`deletes.jsonl` 可查 |
