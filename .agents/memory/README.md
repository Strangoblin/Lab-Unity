# 记忆库

| 领域 | 当前上下文与日期记录索引 |
|---|---|
| meta：体系决策与维护 | [meta memory](meta/memory.md) |
| unity：项目与渲染经验 | [unity memory](unity/MEMORY.md) |

先读需要的领域索引，再按问题取用相关记录；不全量加载历史。
当前索引可精炼更新，dated 记录、证据与基线保留原字节。旧位置与相对链接表示当时语境，按迁移记录回放，不作为当前路由。

## 写入规则

| 项 | 规则 |
|---|---|
| 通道 | 平台文件工具直接写入。`memory/` 在 `.mcp` 门禁作用域**之外**，`write_gated` 必然返回 `PATH_NOT_ALLOWED`；这不是配置故障，见[执行契约](../skills/auto-manager/capabilities/execution-contract.md) |
| 位置 | `<领域>/YYYY-MM-DD-<slug>.md`；领域为 `meta` / `unity` |
| 命名 | 日期取记录当天，slug 用主题短语；不用序号或 `latest` |
| 索引 | 同一会话内更新领域索引（[meta](meta/memory.md) / [unity](unity/MEMORY.md)）：索引写取用线索与结论摘要，正文只写一处 |
| 内容 | 区分已验证 / 推断 / 未验证；记录依据、限制与后续条件；不覆盖历史条目 |
| 归档 | 记录是结论的唯一副本：写入后随仓库提交。只存在于工作区的未跟踪文件不算归档，删除也不可恢复 |

角色只负责判断该不该记、记到哪个领域；流程与证据形式按各角色 AGENT 的"记录"段执行。
