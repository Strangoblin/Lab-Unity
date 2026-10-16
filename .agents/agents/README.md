# Agent 清单

各平台入口先读本清单，再直接取用匹配角色和需要的库；不按目录逐层预加载。

| 任务 | Agent | 记忆库 | 知识库 | 技能入口 |
|---|---|---|---|---|
| 体系、索引、角色 / 技能、平台接口 | [meta-developer](meta-developer/AGENT.md) | [meta memory](../memory/meta/memory.md) | [meta knowledge](../knowledge/meta/README.md) | [agent-maintenance](../skills/agent-maintenance/SKILL.md) |
| Unity 渲染、Shader / Compute / RenderGraph、C# | [unity-developer](unity-developer/AGENT.md) | [unity memory](../memory/unity/MEMORY.md) | [unity knowledge](../knowledge/unity/README.md) | [autoagent](../skills/auto-manager/SKILL.md) |
| Editor 操作、渲染取像、耗时对照、同类复用 | 使用 unity-developer | 只读相关上下文 | 只读所需事实 | 先到 [autoagent](../skills/auto-manager/SKILL.md)，由其分流开发流程或专门操作 |

外部是来源，不是第三个角色：[外部来源清单](../knowledge/external/README.md)。只读咨询可只取知识；Unity 任务实施与操作统一由 autoagent 首次分流，体系接入按 meta 领域取用对应技能。角色只负责索引、取用与记录，具体操作归 skill。
