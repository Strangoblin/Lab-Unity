# 模式选择与边界

`[G1] Mode Selection — Mode: <Research | Production | Experiment> | Reason: <why>` 是操作流程的结构化决策。
选择目标最匹配的模式；用户已指定执行方式时沿用其授权。范围混合且无法判断主目标时再澄清，不因关键词机械询问。

| 模式 | 主目标 | 入口 |
|---|---|---|
| Research | Shader/渲染调参、逐步看效果、假设验证 | [research.md](../modes/research.md) |
| Production | 库内方案支持的实现、修复、重构、文件整理 | [production.md](../modes/production.md) |
| Experiment | 无库内方案、必须搜索方案、自主迭代 | [experiment.md](../modes/experiment.md) |

涉及同类功能时先按 [复用工作流](../../unity-feature-reuse/SKILL.md) 查契约，再判定“无库内方案”。

| 维度 | Research | Production | Experiment |
|---|---|---|---|
| 来源 | 人工逐步试 | 库内 reference/template | WebSearch → Plan → 用户确认 |
| 基准 | 不额外备份 | 重大改动记录 Git 基准 | Git 迭代基准 + 必要验证输入 |
| 知识 | 按任务取用 | 门禁必读 + 相关领域 | 联网来源 + 库内约束 |
| 编译恢复 | 报错即停 | 最多 3 次 | 最多 3 次；第 3 次重审方案 |
| Play | 不自动 | 按运行行为验证需求 | 仅必须验证运行时行为时 |
| 验证 | 编译 + 人工视觉观测 | snapshot/logs/查询 + 所需渲染 | 结构化证据 + 人工视觉观测 |
| 人工介入 | 每步观测 | 异常或需要决策时 | Plan 确认；失败或授权边界时 |
| 循环 | 人工驱动持续试验 | 单次流程 | 最多 3 大循环 |
| 完成 | 人工确认 | 所需编译/运行通过 | 目标达成；耗尽循环保留现场 |

Editor 连接/不可用行为见 [availability.md](../../unity-editor/references/availability.md)，退出与错误恢复见 [recovery.md](recovery.md)。
