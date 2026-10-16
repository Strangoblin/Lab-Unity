---
name: mcp-v3-self-observing
description: MCP v3 — 判定核唯一化 + 策略数据化（policy.json）+ 摩擦账本自观测；回答「框架为什么总晚于 agent 能力」
date: 2026-10-08
---

# 2026-10-08 — MCP v3：判定核 / 策略数据 / 摩擦账本

## 起因（用户提问）

> "随着 agent 智能化，现有 mcp 无法最大化发挥 agent 能力……目前的 `.agents`/`.mcp` 框架升级
> 始终晚于 agent 能力升级，如何保证框架持续更新？"

两问合一：**机制问题**（门禁绑死传输 + 恒严）与**元问题**（框架升级没有触发点）。

## 证据（不是推断，取自 Codex 会话库 `~/.codex/thread_history_1.sqlite`）

| 指标 | 数值 | 含义 |
|---|---|---|
| MCP 调用总数 | 902 | 全库（含导入的 Claude 会话） |
| 真实操作 | 542（write 536 / delete 4 / move 2） | |
| 仪式调用 | 342（g_pass 183 / set_recipe 89 / status 35 / reset 28 / list 7） | **~0.6 次仪式 / 次真实操作** |
| DENIED | 44 / 1334 响应 | G15_MISSING_HIGH_PRIORITY 10、G15_UNRESOLVED 5、PATH_NOT_ALLOWED 8 |
| 两侧工具分叉 | delete/move 各补 CLI；**write 无 CLI** | 2026-09-21 记「下一步」，10-08 仍未做 = 17 天已知缺口 |
| 说 vs 做 | `production.md` 写 `[G1]模式/[G2]脚本/[G3]放置` 三道门；MCP 实际只执行 `g_entry+g_knowledge` | 文档与执行已分离 = 滞后已发生 |

结构结论（写进 `references/enforcement-policy.md`）：约束的价值随被约束者变强而衰减，成本不衰减；
升级是事件驱动（等人注意到），能力增长是连续的；摩擦只以散文存在于 dated memory，不可查不可排序；
门禁只增不减（5 个退役门禁仍注册，`g_file.py` 是死代码）；验收是快照不是趋势；执行绑死在 MCP
工具表（按会话冻结）导致可达性本身脆弱。

## 决策：不删仪式、不放松安全闸，改三件事

1. **判定核唯一化** — `validation/judgement.py` 是 write/delete/move 的唯一实现；策略应用与信号落账都在核内。
   `server.py` 与 CLI 都退化为薄壳。这直接消掉「write 无 CLI」的结构原因：通道不再是判定的宿主。
2. **策略数据化** — `.mcp/policy.json` + `policy.py`：每条检查 `level`（block/warn/off）与 `retirable`。
   **缺省 = 固有级别 = v2 行为（零回归）**；策略只能放宽不能收紧（收紧是检查实现的事）。
   `ceremony.recipe_gate` 是「agent 自主性档位」的主旋钮：降为 warn → 未过链也能写但留 warning。
3. **自观测** — `telemetry.py` → `.mcp/friction.jsonl`（每次判定一行）；`friction_report.py` 聚合出
   仪式比 / 拒绝 Top / 从未命中的检查，产出 `demote` / `retire-or-dedemote` / `safety-healthy` / `fix-plumbing`
   提案。**提案不自动生效** —— 改 policy 须 meta 复核 + 人工确认（写进 `rules/meta-architecture.md`）。

元问题的答案（`references/enforcement-policy.md`）：**门禁是控制器，不是过滤器** ——
sensor（账本）→ aggregator（报告）→ actuator（policy 数据），加**能力契约**（每条检查须填假设/证据/成本/退役判据）
与 **E5 会话收尾程序**。触发不靠人想起：收尾跑报告，`review` 级提案即待办。

## 落地清单

代码：`validation/judgement.py`（新，判定核）、`validation/policy.py`（新）、`policy.json`（新）、
`telemetry.py`（新）、`validation/friction_report.py`（新）、`validation/gate_cli.py`（新，统一 CLI）、
`validation/{write,delete,move}_gated.py`（改为薄壳，删除各自的独立实现）、`server.py`（三工具改薄壳，版本 0.7.0）、
`gate_center.py`（仪式记账 + policy 降级）、`tests/test_recipes.py`（+4 组 v3 用例）、`.gitignore`（friction.jsonl）。

文档：`references/enforcement-policy.md`（新，91 行，与同级 reference 85–123 行一致）、meta `AGENT.md`（E5 + 清单 + 工具表）、
`references/README.md`（索引）、`rules/meta-architecture.md`（框架演进改哪一层）、`.mcp/README.md`（v3 三节）、
`mcp-gate-usage.md`（CLI 对等 + policy）、`auto-manager/SKILL.md`（CLI 兜底 + 不自行改级别）、
`.claude/CLAUDE.md`、`.codex/AGENTS.md` + `INTERFACE.md`、`interfaces/project-structure.md`。

## 验证

- `.mcp/tests/test_recipes.py` 全绿（含 `test_policy_levels` / `test_ceremony_relax` / `test_write_cli_channel` / `test_friction_report`）
- `test_knowledge_paths.py` PASS；`.codex/tests/06_architecture/verify.py` PASS（default + strict，`agents_broken_markdown_links: 0`）
- 行为等价性：全部 v2 用例在缺省 policy 下逐条通过（判定核是抽取而非重写）
- 摩擦报告实跑：本会话样本即产出「仪式比 0.69 → 复核 recipe_gate 降级」，且对安全类零命中标注 `safety-healthy` 而非提议退役

## 偏离与未做

- `enforcement-policy.md` 91 行，超 P1 的 80 行上限；同级 reference 已有 85/88/105/105/123 行，按现状记账。
- **首条策略改动未执行**：`ceremony.recipe_gate` 降为 `warn`（以及 `ceremony.knowledge_high_priority` 降级）
  属行为变更，需人工确认 —— 本轮只交付机制，默认档位保持 v2。观察窗口建议：一个完整 Unity 任务周期。
- `g_file.py` + `project_paths.py` 咨询层仍为死代码（测试钉住退役集合），未清理 —— 属另一轮。
- `telemetry` 账本无按会话切分字段（只有 ts/channel），跨会话趋势可查、单会话回溯较弱；待有需求再加 `session` 字段。
