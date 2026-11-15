---
name: structure-selfcheck
description: 体系周期自检 —— 盘点记忆寿命与垃圾桶存量，把仍有用的结论吸收进 knowledge / skill，退役过期记录、清空垃圾桶，并留下逐日历史日志。适用于「自检」「整理记忆」「清垃圾桶」这类到期清扫；不处理 Unity 业务资产，不替代 agent-maintenance 的日常编辑。
user-invocable: true
argument-hint: "[记忆 | 垃圾桶 | 全部]"
---

# Structure Selfcheck

体系里有两样东西只增不减：记忆每次收尾加一条，垃圾桶每次删除加一桶。没有到期清扫，
检索面会慢慢变成档案柜 —— 索引越写越长，真正要知道的东西反而埋得更深。这个技能是那把扫帚。

它是**开发层（meta 领域）的周期动作**：不需要有人下指令才跑。归 meta-developer 取用。

## 边界

| 不做 | 归属 |
|---|---|
| 断链、软链、角色归属、hook 漂移 | 本技能不做断言；`verify.py` 已于 2026-11-01 退役，用 `rg` 核对活动文档链接，必要时手工检查 `git status` 与软链 |
| agent / skill / rule 的日常编辑与迁移 | [agent-maintenance](../agent-maintenance/SKILL.md) |
| `Assets/` 业务资产存量整理 | [cleanup](../agent-workflow/capabilities/cleanup.md) |
| 判定误拦与策略提案 | `cli.py report`（E5） |

本技能**只动两处**：`.agents/memory/**` 与 `.mcp/trash/**`。

## 五阶段

```
S0 基线   open --profile meta；inventory.py --check 与 rg 断链核验确认起点
S1 盘点   inventory.py（只读）→ 记忆寿命 / 入链 / 索引覆盖 / 垃圾桶存量 / 孤儿
S2 分流   出「精确清单」：路径 | 动作 | 理由 | 现状 | 去向
          ⛔ 一次确认，覆盖整批（清单未获确认前不动任何东西）
S3 吸收   把还要用的结论写进 knowledge / skill，再 grep 核对确实写进去了
S4 清除   记忆 → cli.py delete；垃圾桶 → 先写历史日志，再 trash --empty
S5 留痕   history.md 逐日一行 + 索引改行；inventory.py；run_isolated；close
```

### S0 基线

```bash
python3 .mcp/bin/cli.py open --profile meta --task "体系自检"
python3 .agents/skills/structure-selfcheck/scripts/inventory.py --check
```

起点不绿就先修，不要在一个断链的体系上做清扫 —— 那样分不清是你删断的，还是本来就断的。

### S1 盘点

```bash
python3 .agents/skills/structure-selfcheck/scripts/inventory.py          # 人读
python3 .agents/skills/structure-selfcheck/scripts/inventory.py --json   # 机器
python3 .agents/skills/structure-selfcheck/scripts/inventory.py --check  # 阈值提醒
```

脚本只读数、不做判断。它的价值在于**同一份事实每次算出来一样** —— 手写 shell 数出来的
「大概」不能作为删东西的依据（第一版手写统计就把 unity 记录全拿去比 meta 索引，报出 8 条孤儿，
其中 5 条其实有索引）。

### S2 分流清单

| 列 | 内容 |
|---|---|
| 路径 | 精确到文件 |
| 动作 | 吸收 / 归档 / 保留 / 清空桶 |
| 理由 | 命中哪把锁，或哪条例外 |
| 现状 | 年龄、入链数、索引状态、证据目录体量 |
| 去向 | 迁移目标（可点的路径），或「无」 |

确认一次，覆盖整批 —— 与 [cleanup](../agent-workflow/capabilities/cleanup.md) 同一模式。
清单被否就重出清单，**不要改判据去凑通过**。

### S3 吸收（不可省略）

按 [去向优先级](references/memory-archive.md) 写进唯一归属，然后逐条 grep 核对。
一条结论如果在活文档里找不到，它就没有被吸收，那条记录就不能删。

### S4 清除

```bash
python3 .mcp/bin/cli.py delete .agents/memory/meta/<记录>.md --reason "自检：已并入 <去向>"
python3 .mcp/bin/cli.py trash --empty <桶 id>
```

### S5 留痕与验证

```bash
python3 .agents/skills/structure-selfcheck/scripts/inventory.py
python3 .mcp/tests/run_isolated.py --task-dir tmp/sessions/<date>-selfcheck
python3 .mcp/bin/cli.py close
```

`close` 的 memory 义务是三段式：asset / system 区有实质改动 → 必须新增
`.agents/memory/<域>/<YYYY-MM-DD>-*.md` **dated 记录** → 并更新域索引。

`history.md` **不算** dated 记录（路径不匹配 `_MEMORY_RECORD`），所以自检收尾会稳定报一条
`memory: MISS`，**级别 warn、不阻塞**。这是有意的取舍：清扫本身不该再造一条 dated 记录
（那正是要治的病），留痕由 `history.md` 与 `deletes.jsonl` 承担。

- 不要为了消掉这条 warn 去改 `.mcp/lib/close.py` 的判据。
- 需要干净输出时用 `close --waive memory --reason "自检：留痕在 history.md"`，豁免会落进账本。
- 唯一要补 dated 记录的情况：自检**改变了判据、技能或脚本本身**（如 2026-10-30 技能落地那条）。

退役还有第二个哨兵：**测试可能钉在具体某条记录上**。`test_zones.py` 的夹具曾写死
`2026-08-03-experiment-mode.md`，归档那条记录就让三套变两套绿。夹具要取「任意一条 dated 记录」，
不能点名 —— 否则每次归档都在给自己埋雷。`run_isolated` 是这一关的哨兵，S5 不能省。

## 硬约束

1. **S3 必须早于 S4。** 先删后补是整条流程里唯一会丢东西的顺序。
2. 记忆删除一律走 `cli.py delete`，不用 `rm` —— 内容进 trash，可 `restore` 回来。
3. `trash --empty` 是唯一不可逆动作：**先写历史日志行，再清空**。
4. 不动 `Assets/**` 的桶（业务资产，只有用户能定）；不动证据目录（另起一轮）。
5. 年龄、入链、体积都只是**调查线索**，不是删除理由。理由必须落在「结论已并入哪里」。

## 判据

| 对象 | 入口 |
|---|---|
| 记忆：三把锁、去向、历史日志、索引改写 | [memory-archive](references/memory-archive.md) |
| 垃圾桶：桶分类、复活四问、清空留痕 | [trash-triage](references/trash-triage.md) |
| 历史日志行格式 | [history 模板](assets/history.template.md) |
| 盘点脚本 | [scripts](scripts/README.md) |
