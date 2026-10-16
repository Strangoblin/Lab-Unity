# Experiment Mode

适用无库内方案、需要搜索外部实现与有限自主迭代的任务。逐步人工调参用 Research；已有方案的修复/实现用 Production。
共用授权和门禁以 [执行契约](../capabilities/execution-contract.md) 为准。

## 流程

承接开发入口已完成的模式选择、recipe 与工作区登记，不重新初始化；已有知识与设计结论直接复用，变化时更新。

1. E1 按 [WebSearch → Plan](../capabilities/web-search.md) 找来源、验证版本/依赖，在形成阶段方案时按 [可持续开发判断](../capabilities/sustainable-development.md) 标出基线、职责与待验证假设（已有结论复用，无依据的近期变化标为未知），并给出验收标准；在已有授权内完成可审阅材料后，由用户确认 Plan。当前会话已有同一 Plan 授权则直接沿用。
2. E2 按 [知识](../capabilities/knowledge.md) 取用库内约束并同步 G1.5；需要同类功能先做 [复用](../../unity-feature-reuse/SKILL.md)。依据 [脚本决策](../capabilities/script-decision.md) / [落点](../capabilities/file-placement.md) 门禁实现。
3. 已连接 Editor 时执行 [编译](../capabilities/compile.md) 与必要的 [运行/渲染验证](../capabilities/runtime.md)。Play 仅用于运行时行为；非视觉目标优先 snapshot/logs/查询，视觉目标交人工观测。
4. 按 [Git 基准](../capabilities/backup.md) 保留可比较来源与必要验证输入，再依证据进入下一迭代。
5. E3 阶段前 2 次失败可修复；第 3 次联网确认疑点并修改 Plan，必要方向变化重新确认。最多 3 大循环；连续 2 个大循环同阶段失败先说明方案疑点。
6. E4 目标达成按 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 收尾。耗尽循环保留现场、来源与失败总结，不自动回滚。

## 边界

Plan / WebSearch 是流程要求，其旧 MCP gate 已退役。所有模式共用 g_entry / g_knowledge 配方链；不要调用 g_plan / g_web_search。
[Editor 不可用](../../unity-editor/references/availability.md) 时完成可独立代码工作，明确未编译/运行；恢复上限见 [recovery](../capabilities/recovery.md)。
编译通过不能替代目标达成证据，视觉判断也不能替代可查询的行为验证。
