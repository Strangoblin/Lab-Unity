---
name: structure-selfcheck-skill
description: 结构自检技能落地 —— 记忆按三把锁归档、垃圾桶按四类分流、逐日历史日志替代 dated 记录；inventory.py 只读盘点。
date: 2026-10-30
---

# 结构自检技能（开发层周期动作）

用户要求：整体结构需要自检流程，整理记忆、清理垃圾，算开发层的工作；过久记忆把有用信息并入
skill 后清除，最多留历史日志简述当天干了什么；垃圾桶里清点可复用 script 并入 skill，其余垃圾
直接删。

## 形态

`.agents/skills/structure-selfcheck/` —— `SKILL.md`（五阶段）+ `references/`（两条判据）+
`scripts/inventory.py`（只读盘点）+ `assets/history.template.md`。路由接在
[agents 清单](../../agents/README.md) 的 meta-developer 行与
[meta-developer/AGENT.md](../../agents/meta-developer/AGENT.md) 取用段。

与既有能力的分工是这次设计的重点：`verify.py` 管断链与归属（自检只调用），
`agent-maintenance` 管有人下指令的编辑迁移，`cleanup.md` 管 `Assets/` 存量；**自检是唯一一个
没人下令也会到期跑的清扫**，只动 `.agents/memory/**` 与 `.mcp/trash/**`。

## 判据

| 对象 | 规则 |
|---|---|
| 记忆·孤儿 | `index_mentions == 0` → 立即分流（补索引或并走再删），不等过期 |
| 记忆·过期 | 三把锁：≥60 天 + 零活文档入链 + 有归宿或已无价值 |
| 垃圾桶 | 生成物 / 退役子系统默认清空；`Assets/**` 永不动；<7 天默认保留 |
| 复活 | 四问全 yes：是脚本、会再出现、无等价物、改完路径能跑 |

历史日志 `memory/<域>/history.md` **逐日一行**，列：日期 / 当天做了什么 / 归档记录 / 去向。

## 三个实现决定

- **`inventory.py` 只读**。它只把事实算成可复现的表；判断留给 agent。手写 shell 数出来的数
  不能当删除依据 —— 第一版手工统计把 unity 记录全拿去比 meta 索引，报出 8 条孤儿，实际只有 3 条。
- **`close` 不需要 waiver**。memory 义务要求「memory 区有写入」+「索引被更新」；写 `history.md`
  满足前者、改索引满足后者。所以每次清扫不会再造一条 dated 记录 —— 那正是要治的病。
- **`trash --empty` 前先写日志**。它是整条流程唯一不可逆动作，和「trash 是唯一痕迹」的红线冲突；
  留痕顺序把冲突降成「丢内容、留审计」。

## 盘点脚本踩到的真问题

- 旧桶 `20261028-125504-a0cbb8` 的 manifest 里 `children` 被写成一串键名
  （`["path","kind","size",...]`）。脚本对损坏 manifest 保持沉默并标记 `[manifest 损坏]`，
  不因为读不出子项就当成空桶 —— 那会让一次 restore 变成一次彻底丢失。
- 域索引与磁盘的真实差异：`meta/2026-10-16-write-channel-rule.md` 与 `-rules.md` 是同日两条并存，
  前者未入索引；`unity/2026-10-21-ssc-quality-tiers.md` 未入索引。三条孤儿都进第一轮清单。

## 验证

- `python3 .codex/tests/06_architecture/verify.py --strict` → PASS（补齐 4 处技能内断链后由 4 降到 0）
- `python3 .mcp/tests/run_isolated.py --task-dir tmp/sessions/2026-10-30-selfcheck-build` → 4/4 PASS，生产账本指纹未变
- `inventory.py --check` → 退 1（退役候选 22 ≥ 20、垃圾桶 21 桶 ≥ 15），阈值按预期触发

## 首轮盘点基线（2026-10-30）

记忆 146 条（meta 67 / unity 79）；≥60 天且零入链 22 条；垃圾桶 21 桶
（生成物 11、退役子系统 3、其它 1、业务资产 6）；孤儿 3 条；索引指向缺失文件 0。

## 首轮实跑（2026-10-30）

- 归档 16 条记忆：14 条 v3 体系史、1 条写入通道重复稿、1 条 2025-06 安全经验；分三批进 trash，
  逐条核对过现行结论的落点，另把两条未落地的经验补进 `cleanup.md` 与 `maintenance.md`。
- 清空 15 个旧桶（876K → 156K）；保留 6 个 Fabric 业务资产桶与 3 个本次删除留底桶。
- 补索引：`ssc-quality-tiers` 补行；两个域索引加 `history.md` 指针。
- **抓到一处真耦合**：`test_zones.py` 把夹具钉死在 `2026-08-03-experiment-mode.md`，归档即失败。
  改为「取任意一条 dated 记录」；否则每次归档都在给测试埋雷。
- 结果：孤儿 0、meta 退役候选 0、`verify.py --strict` PASS、隔离 4/4。

## 相关

- 记忆写入规则：[memory/README.md](../README.md)
- 存量整理边界：[cleanup.md](../../skills/auto-manager/capabilities/cleanup.md)
- 判定核与垃圾桶：[.mcp/README.md](../../../.mcp/README.md)
