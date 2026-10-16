---
name: policy-demotion-executed
description: 首批策略降级执行（recipe_gate / knowledge_high_priority → warn）与两条被驳回的提案；E5 首次实跑抓到两个报告缺陷
date: 2026-10-09
---

# 2026-10-09 — 首批策略降级执行（E5 首次实跑）

用户批准（"策略降级可修改"）。**只改 `policy.json` 数据，未动判定代码** —— 这正是 v3 抽象是否成立的检验。

## 执行

| 检查 | 变更 | 依据 |
|---|---|---|
| `ceremony.recipe_gate` | block → **warn** | friction_report `demote` 提案（仪式比 1.79 > 0.30）；agent 已能自主读知识 |
| `ceremony.knowledge_high_priority` | block → **warn** | 历史 10 次 `G15_MISSING_HIGH_PRIORITY` 多为「读了但没按名字申报」的形式性误拦 |
| `ceremony.knowledge_resolvable` | **保持 block** | 历史 5 次 `G15_UNRESOLVED_FILE` 是真拦截（编造文件名）——「漏读」与「幻觉」是两种失败模式 |
| `norm.cs-type-decl` / `norm.shader-decl` | **保持 block** | 零命中 = agent 没写坏过，**不是冗余**；成本一次正则 |

含义（必须对用户讲清）：`ceremony.recipe_gate` 是 `can_write` 的唯一闸，降为 warn 后**整条配方链变为建议** ——
未过链也能写，只是携带 `ceremony.recipe_gate` warning 且 `recipe` 记 `null`。硬闸未变：
作用域 / 工作目录 / 结构规范 / 落点（新文件）/ 删除与移动安全 / 编造文件名。

记录落在 `policy.json` 的 `_history`（date / by / change / evidence / kept_block / observation）。

## 驳回两条提案（判断依据要留痕）

报告按「零命中 → 退役候选」机械产出了 `norm.*` 与 `placement.root.new` 的退役建议。
但它们拦的是**产物不可用**（缺 `Shader "..."` 的 .shader 就是废文件），零命中是健康而非冗余。
据此给这类检查加 `retirable: false`。**报告的启发式要经人复核，这一轮就是实例。**

## E5 首次实跑抓出两个报告缺陷（测试发现，已修）

1. 仪式比提案对**已放宽**的项重复提降级 —— 加 `level == "block"` 前置（升级由事故驱动，不等样本）
2. `retirable: false` 且已放宽的项被误标 `safety-healthy`（"零命中 = 没事"）—— 应标"已放宽，观察中"

## 测试结构的必要调整（重要）

策略一变，一批断言行就失效 —— 因为它们断言的是**当前 policy 取值**，而不是**机制**。改为：

- 套件级钉住一份 **strict 策略**（`MCP_POLICY_FILE` → tempdir），所有「该拦就拦」的断言在它下面跑；
  这样下次调 policy 不用改断言
- 新增 `test_shipped_policy`：**已发布策略的取值本身就是被决定过的姿态**，单独守（防漂移）
- 新增 `test_shipped_relaxed_write`：端到端验已发布策略的真实行为（未过链可写+留 warning / 防幻觉硬拦 / 缺项 RELAXED）
- `policy_env` 上下文管理器统一切换并**还原**（此前各用例 `pop`，会让后续用例退回已发布策略 —— 一类隐蔽的测试耦合）

顺带修掉一个真 bug：`friction_report` 用顶层 `from policy import ...`，与 judgement/gate_center 的
`validation.policy` 形成**两个模块实例**（各自一份缓存，`reload()` 只清一个）。改为统一走 `validation.*`。
这条属于 meta-architecture「链路保障」的实例：同名模块的两种导入路径 = 隐性的第二权威源。

## 同步清单

`.mcp/policy.json`（数据 + `_history`）、`.mcp/validation/friction_report.py`（两个缺陷 + 导入统一）、
`.mcp/tests/test_recipes.py`（strict 钉桩 / `policy_env` / 两个新用例 / 提案断言在 shipped 策略下重算 agg）、
`.agents/agents/unity-developer/references/mcp-gate-usage.md`（§当前档位）、
`.agents/skills/auto-manager/SKILL.md` + `modes/production.md`（档位列 + "跳过门禁"措辞改为按档位）、
`.mcp/README.md`、`references/enforcement-policy.md`（§5.7 已执行记录）。

## 验证

`.mcp/tests/test_recipes.py` 全绿；`test_knowledge_paths.py` PASS；`06_architecture/verify.py` PASS。

## 观察窗口与回退

- 窗口：一个完整 Unity 任务周期。观察 `friction.jsonl` 里 `warn.ceremony.recipe_gate` 的频率，
  以及是否出现「跳读知识 → 产物质量问题」。
- 回退：把两项改回 `block` 即恢复 v2 严格度 —— **一行数据，不动代码**。质量下滑时按控制律不等样本立即升回。
