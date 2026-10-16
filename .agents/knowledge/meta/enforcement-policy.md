# 执行策略参考 — 框架如何随 agent 能力持续更新

> 本文回答一个结构性问题：**为什么 `.agents` / `.mcp` 框架的升级总是晚于 agent 能力升级，以及如何让它不再晚。**
> 配套实现：`.mcp/policy.json`（策略数据）、`.mcp/telemetry.py`（传感器）、`.mcp/validation/friction_report.py`（提案）。

## 1. 为什么会滞后（结构性原因，不是疏忽）

| # | 机制 | 后果 |
|---|------|------|
| 1 | **约束的价值随被约束者变强而衰减，成本不衰减** | agent 越可靠，闸门的边际收益越低，但每次操作照付；没有任何机制注意到这个衰减 |
| 2 | **升级是事件驱动（人「注意到」），能力增长是连续的** | 没有触发点 → 滞后无上界。`write` 缺 CLI 通道 2026-09-21 就判定为「下一步」，到 10-08 仍未做——17 天已知缺口的滞后 |
| 3 | **摩擦以散文存在于 dated memory** | 不可查、不可排序、不可聚合，无法驱动待办 |
| 4 | **门禁只增不减** | 没有退役判据。`g_mode/g_script/g_file/g_web_search/g_plan` 全部退役却仍注册；`g_file.py` 是死代码，仅因测试钉住集合而保留 |
| 5 | **框架的验收是快照，不是趋势** | 无法回答「这条闸门最近还起过作用吗」 |
| 6 | **执行绑定在传输边缘**（MCP 工具表按会话冻结） | 框架**可达性**本身脆弱：冻结即断操作，被迫补平行通道，通道再各自分叉。**v3 已修**（三条操作都有 CLI，判定核唯一）——此条留作机制警示 |

判据：**说出来的流程**与**被执行的流程**已分离过一次。`production.md` 写着 `[G1] 模式 / [G2] 脚本 / [G3] 放置`，MCP 实际只执行 `g_entry + g_knowledge`（其余退役为注解）。说一套、执行另一套 = 滞后已经发生。

## 2. 模型：门禁是控制器，不是过滤器

```
   sensor                    aggregator                 actuator
friction.jsonl  ──────►  friction_report  ──────►  policy.json
（每次判定留痕）           （排序 + 提案）            （check → block/warn/off）
     ▲                                                     │
     └──────────────── judgement.py 按级别判定 ◄────────────┘
```

- **sensor**：`telemetry.record` 在判定核内调用，通道不许自己记账（口径统一）
- **aggregator**：`friction_report` 产出事实 + 提案，**不自动改策略**
- **actuator**：`policy.json` 是数据；改它 = 调框架，不必改判定代码
- **判定核**：`validation/judgement.py`，策略只在核内应用

## 3. 能力契约（每条门禁必须能说出自己的假设）

新增或保留一条检查时，必须能填出这一行，否则它就是无根据的限制：

| 字段 | 含义 |
|------|------|
| **假设** | 它假定 agent 会做错什么？（如「会忘记给 `.shader` 写 `Shader "..."`」） |
| **证据** | 最近一次它真正拦住问题是什么时候？（`friction.jsonl` 的 `deny.<check>`） |
| **成本** | 每次操作多付什么？（工具调用 / token / 往返） |
| **退役判据** | 什么条件下降级或删除？（默认：N 次真实操作内零命中，且非安全类） |

## 4. 控制律（默认档位与调节方向）

| 检查类别 | 默认 | 调节方向 | 依据 |
|---|---|---|---|
| **安全类**（`scope.path` / `deletion.*` / `move.presafety`） | `block` | **不退役**；只在证据充分时降为 `warn`，不得 `off` | 错了不可逆 |
| **权限类**（`scope.workspace`） | `block` | **不退役**；由用户下达的边界，不因 agent 可靠而放开 | 越界 = 违背用户意图 |
| **正确性类**（`norm.shader-decl` / `norm.cs-type-decl`） | `block` | 零命中且样本足 → 复核降级 | 错了产物不可用，误拦少 |
| **仪式类**（`ceremony.recipe_gate` / `ceremony.knowledge_*`） | `block` | 仪式比 > 0.30 且样本足 → 降 `warn` 观察 | agent 已能自读知识时，声明是纯税 |
| **风格类**（`norm.divider-added` / `placement.*`） | `warn` | 一般不升 `block` | 风格不值得阻断 |

**棘轮原则**：降级要凭账本证据（`friction_report`），升级可以在事故后立即做（不必等样本）。
**降级不静默**：被放行的检查仍留 warning + 记账，`off` 也要留痕——否则下次无法判断该不该恢复。

## 5. 框架演进入口

E5 的报告、提案处理、策略确认、隔离验证与记录步骤由 [agent-maintenance](../../skills/agent-maintenance/references/maintenance.md) 持有。这里仅保留控制模型、能力契约、权威边界和历史事实，不维护第二套执行流程。

## 5.5 批准权在人 — 机制的牙齿

「人工确认」若只靠约定，等于没有。工作目录扩展（`gate_request_workspace` → 人工批准）把它做成**分权**：

- agent 侧工具**只登记不授权**（`NEEDS_APPROVAL`），批准入口不是 MCP 工具；
- 批准命令 `gate_cli.py workspace approve/reject` 被**平台 hook 封闭**（Claude/Codex 的 `guard-bash.sh` C3），
  agent 的 Bash 调用直接 exit 2 拒绝；用户在本机终端执行不受影响；
- 请求 / 批准 / 拒绝全部落 `friction.jsonl` —— 自批会留痕。

**通则**：凡是「agent 提出、人批准」的动作，批准入口应当是 agent 够不到的通道 + 审计，而不是写在 skill 里的一句要求。

## 5.7 已执行记录（E5 实跑）

**2026-10-09 首批调级**（人工批准，改 `policy.json` 数据，未动判定代码）：

| 检查 | 变更 | 依据 | 结果 |
|---|---|---|---|
| `ceremony.recipe_gate` | block → **warn** | friction_report `demote` 提案（仪式比 1.79 > 0.30）；agent 已能自主读知识 | 未过链可写 + warning + 记账；观察一个任务周期 |
| `ceremony.knowledge_high_priority` | block → **warn** | 历史 10 次 `G15_MISSING_HIGH_PRIORITY` 多为「读了但没按名字申报」的形式性误拦 | 漏申报 → `RELAXED` 标记 + 放行 |
| `ceremony.knowledge_resolvable` | **保持 block** | 历史 5 次 `G15_UNRESOLVED_FILE` 是真拦截（编造文件名） | 防幻觉不随仪式降级 |
| `norm.cs-type-decl` / `norm.shader-decl` | **保持 block** | 零命中 = agent 没写坏过，**不是检查冗余**；成本是一次正则 | 标 `retirable: false`，报告不再提议退役 |

两条被驳回的提案（记录判断依据）：报告按「零命中 → 退役候选」机械产出了 `norm.*` / `placement.root.new`
的退役建议，但它们拦的是**产物不可用**，零命中是健康而非冗余 —— 据此给这类检查加 `retirable: false`。

**这次实跑还抓出两个报告缺陷**（测试发现，已修）：仪式比提案对**已放宽**的项重复提降级；
`retirable: false` 且已放宽的项被误标为 `safety-healthy`（"零命中是好事"）而非"观察中"。

## 6. 与门禁操作模型的关系

v2 按**操作**建模（write 管内容 / delete 管消失 / move 管换位置）。v3 不动这个模型，
只在上面加**策略数据化**（级别可调）与**自观测**（判定留痕）。判定实现集中在
`validation/judgement.py` —— 三条操作、两条传输（MCP 工具 / `gate_cli.py`）共用同一判定与记账。

## 溯源

- 2026-10-08 建立。证据来源：Codex 会话库 `~/.codex/thread_history_1.sqlite`（902 次 MCP 调用 = 542 真实操作 + 342 仪式调用 + 44 DENIED；`gate_reset` 28 次；MCP 工具表冻结导致 delete/move 各补一条 CLI，write 长期缺失）。
- 决策：不删仪式、不放松安全闸，而是把「松紧」变成数据 + 让框架消费自己的信号。
