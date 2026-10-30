# 执行策略参考 — 框架如何随 agent 能力持续更新

> 本文回答一个结构性问题：**为什么 `.agents` / `.mcp` 框架的升级总是晚于 agent 能力升级，以及如何让它不再晚。**
> 配套实现：`.mcp/config/policy.json`（策略数据）、`.mcp/telemetry.py`（传感器）、`.mcp/validation/friction_report.py`（提案）。

## 1. 为什么会滞后（结构性原因，不是疏忽）

| # | 机制 | 后果 |
|---|------|------|
| 1 | **约束的价值随被约束者变强而衰减，成本不衰减** | agent 越可靠，闸门的边际收益越低，但每次操作照付；没有任何机制注意到这个衰减 |
| 2 | **升级是事件驱动（人「注意到」），能力增长是连续的** | 没有触发点 → 滞后无上界。`write` 缺 CLI 通道 2026-09-21 就判定为「下一步」，到 10-08 仍未做——17 天已知缺口的滞后 |
| 3 | **摩擦以散文存在于 dated memory** | 不可查、不可排序、不可聚合，无法驱动待办 |
| 4 | **门禁只增不减** | 没有退役判据。`g_mode/g_script/g_file/g_web_search/g_plan` 全部退役却仍注册；`g_file.py` 是死代码，仅因测试钉住集合而保留。**v4 已整条拆除**（2026-10-28） |
| 5 | **框架的验收是快照，不是趋势** | 无法回答「这条闸门最近还起过作用吗」 |
| 6 | **执行绑定在传输边缘**（MCP 工具表按会话冻结） | 框架**可达性**本身脆弱：冻结即断操作，被迫补平行通道，通道再各自分叉。v4 取消 MCP 服务后此条结构性消失 |

判据：**说出来的流程**与**被执行的流程**已分离过一次。`production.md` 写着 `[G1] 模式 / [G2] 脚本 / [G3] 放置`，MCP 实际只执行 `g_entry + g_knowledge`（其余退役为注解）。说一套、执行另一套 = 滞后已经发生。
**同一条判据在 v4 的反向读法**：文档里写的每个节点都必须有证据或工具承接；没有承接的节点，就该删掉而不是留着装点。

## 2. 模型：判定核是控制器，不是过滤器

```
   sensor                    aggregator                 actuator
friction.jsonl  ──────►  friction_report  ──────►  policy.json
（每次判定留痕）           （排序 + 提案）            （check → block/warn/off）
     ▲                                                     │
     └──────────── judgement.py / close.py 按级别判定 ◄────┘
```

- **sensor**：`telemetry.record` 在判定核内调用，通道不许自己记账（口径统一）
- **aggregator**：`friction_report` 产出事实 + 提案，**不自动改策略**
- **actuator**：`policy.json` 是数据；改它 = 调框架，不必改判定代码
- **判定核**：`judgement.py`（入口，分区 + 规范）与 `close.py`（出口，义务核对）；策略只在这两处应用

## 3. 能力契约（每条检查必须能说出自己的假设）

新增或保留一条检查时，必须能填出这一行，否则它就是无根据的限制：

| 字段 | 含义 |
|------|------|
| **假设** | 它假定 agent 会做错什么？（如「会忘记给 `.shader` 写 `Shader "..."`」） |
| **证据** | 最近一次它真正拦住问题是什么时候？（`friction.jsonl` 的 `check` 字段） |
| **成本** | 每次操作多付什么？（工具调用 / token / 往返） |
| **退役判据** | 什么条件下降级或删除？（默认：N 次真实操作内零命中，且非安全类） |

**账本按 `check` 记账，不按信号名**：`deny.SANDBOX_VIOLATION` 是给人看的，`sandbox.asset` 才是聚合键。
混用会让报告把**确实拦过**的检查说成「从未命中」，进而提议退役一条正在工作的检查。

## 4. 控制律（默认档位与调节方向）

| 检查类别 | 检查 | 默认 | 调节方向 | 依据 |
|---|---|---|---|---|
| **权限类** | `sandbox.asset`、`scope.zone` | `block` | **不退役**；但这三条判的是「**有没有兜底**」，不是「是哪个目录」—— 已入库的路径靠 git 放行，见 §5.5 | 无兜底的写入 = 不可逆 |
| **安全类** | `bash.redline`、`move.presafety` | `block` | **不退役**；只在证据充分时降为 `warn`，不得 `off` | 错了不可逆 |
| **正确性类** | `norm.shader-decl`、`norm.cs-type-decl` | `block` | 零命中且样本足 → 复核降级 | 错了产物不可用，误拦少 |
| **风格类** | `norm.divider-added`、`placement.*` | `warn`（`placement.root.new` 为 block） | 一般不升 `block` | 风格不值得阻断 |
| **出口类** | `close.knowledge`、`close.norm`、`close.compile` | `block` | 由证据可得性决定；环境拿不到证据就先降 `warn` | 交付不合格的代价在交付之后 |
| **出口类（软）** | `close.verify`、`close.memory` | `warn` | 视项目验证习惯 | 证据形式因任务而异 |

**兜底原则**（v5）：一条权限检查该不该拦，问的是「拦错了能不能回来」。
能（git 有版本、trash 有副本）→ 放行并留痕；不能（新建文件、未入库、证据账本）→ 拒。
2026-10-28 之前判的是目录归属，于是「已入库但不在沙箱里的文件」被一律拒绝 —— 那是**用信任换兜底**，
代价实测出现过两次（`AGENTS.md` 撞 `ZONE_DENIED`、改写记忆撞 `MEMORY_HISTORY_IMMUTABLE`）。

**棘轮原则**：降级要凭账本证据（`friction_report`），升级可以在事故后立即做（不必等样本）。
**降级不静默**：被放行的检查仍留 warning + 记账，`off` 也要留痕——否则下次无法判断该不该恢复。
**只能放宽，不能收紧**：`policy.level_of` 取 `min(intrinsic, policy)`，策略无法把一条 `warn` 检查改成 `block`。

## 5. 框架演进入口

E5 的报告、提案处理、策略确认、隔离验证与记录步骤由 [agent-maintenance](../../skills/agent-maintenance/references/maintenance.md) 持有。这里仅保留控制模型、能力契约、权威边界和历史事实，不维护第二套执行流程。

## 5.5 谁给边界 — 确认留在对话里

沙箱的**来源**始终是用户：agent 不自行选定、不擅自放大。但「确认」与「执行」不该分成两条通道。

- `cli.py open --workspace <目录>` 由 agent 执行，目录必须是用户指定的。
- 用户中途指出新地址 → `cli.py expand --workspace <目录> --reason "<用户原话>"`，由 agent 执行并留痕。
  它**不重置证据起点**（重新 `open` 才会），所以扩充不能拿来做豁免。
- 判定核不问「谁来批」，只问「有没有兜底」：已入库的路径 git 能取回，放行并记录；未入库才拒。
- 每次 `open` / `expand` 都落 `friction.jsonl` 与 `session.json` 的 `expansions`，沙箱变化可追。

**通则**：凡是「agent 提出、人批准」的动作，批准应当发生在**人本来就看得见的地方**（对话框），
并在同一动作里留痕；不要另造一条 agent 够不到的通道 —— v3 的「登记 ↔ 人工 CLI 批准」实测一次没用过，
确认被搬离对话之后，实际发生的是「没人确认」。同理，通行判据要挑**有兜底、可回退**的事实（git 已入库、
trash 有副本），而不是「agent 是否可靠」这种说不清的东西。

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

**2026-10-28 整线拆除**（1261 条摩擦记录，人工批准）：

| 观察 | 结论 |
|---|---|
| 仪式开销比 0.093 | 仪式已不痛；继续加减仪式没有收益 —— 问题不在仪式 |
| 5 次真实误拦全在 `Assets/Mine` 内部 | 沙箱切到叶子目录，跨目录改共享库被拦 —— **检查放错了位置** |
| 收尾写记忆 2 次 `WORKSPACE_NOT_ALLOWED` | 记忆库在工作目录边界之外 —— 结构性必然撞墙 |
| CLI 679 / MCP 309 | 写入通道倒挂：内容要先落文件再 `--from` |

于是改模型而不是调档位：**入口少管，出口多验；能留痕就不拦；能留底就不拒。**
`ceremony.*`、`scope.path`、`scope.workspace`、`deletion.recoverability`、`deletion.guid-referenced` 全部退役；
`scope.zone` / `sandbox.asset` / `memory.history-immutable` / `bash.redline` / `close.*` 新增。策略版本 3 → 4。

**2026-10-28 同日修正**（用户裁决，v4 → v5）：v4 的权限三件里有两件把「目录归属」当成了「能不能改」，实测误拦两次 ——
改 `AGENTS.md` 撞 `ZONE_DENIED`（项目入口文档恰在模型够不到处）、改刚写的 dated 记忆撞 `MEMORY_HISTORY_IMMUTABLE`
（绕路新建的副本比原地改更难维护）。改法是把判据换成**有没有兜底**：

| 变更 | 内容 |
|---|---|
| 退役 | `memory.history-immutable` —— 记忆区完全可写，历史由 git 兜底 |
| 收窄 | `sandbox.asset` / `scope.zone` 只判「未入库 / 无兜底」；已入库路径放行并按体系区记录 |
| 恢复 | `cli.py expand` 扩充通道 —— 确认留在对话框，命令只留痕 |
| 放开 | 资产区的移动 / 改名用原生命令；只有删除与清空仍进垃圾桶 |
| 削去 | `bash.redline` 的「自行批准沙箱」一条 —— 它封的是一条已被对话取代的命令 |

策略版本 4 → 5，`zones.json` 版本 4 → 5（新增 `git_protected.allow_zones`）。

## 6. 与操作模型的关系

v2 按**操作**建模（write 管内容 / delete 管消失 / move 管换位置）。v3 不动这个模型，只加**策略数据化**与**自观测**。
v4 保留分区与规范判定，但换了两个前提：

- **入口从「申报」改成「观测」**：读过什么由 PostToolUse 看真实路径，不再有可编造的申报字段。
- **出口从「无闸」改成「证据验收」**：`close` 按 profile 的义务逐项核对，交付合格与否由证据回答。

判定实现仍集中：`judgement.py` 管写入（分区 + 规范），`close.py` 管出口（义务 + 证据）。
两条通道（平台 hook、`cli.py`）共用同一实现，不再各立口径。

## 溯源

- 2026-10-08 建立。证据来源：Codex 会话库 `~/.codex/thread_history_1.sqlite`（902 次 MCP 调用 = 542 真实操作 + 342 仪式调用 + 44 DENIED；`gate_reset` 28 次；MCP 工具表冻结导致 delete/move 各补一条 CLI，write 长期缺失）。
- 决策：不删仪式、不放松安全闸，而是把「松紧」变成数据 + 让框架消费自己的信号。
- 2026-10-28 v4：摩擦账本（`friction.jsonl`，1261 条）显示真正的成本是**误置的强制**而非检查本身，于是把入口的仪式换成出口的证据，把删除的审批换成可恢复的留底。
