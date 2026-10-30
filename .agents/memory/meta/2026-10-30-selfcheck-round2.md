---
name: selfcheck-round2-unity-archive
description: 自检第二轮 —— unity 域 7 条 living 子系统记录退役，结论分入项目文档与 3 份新知识；并纠正 S5 里「history.md 满足 memory 义务」的错判。
date: 2026-10-30
metadata:
  type: project
---

# 自检第二轮（unity 域）与 S5 判据纠正

用户说「继续自检」后跑的第二轮。范围是首轮留下的 7 条 unity 过期记录；证据目录 86M 仍按约定另起一轮。

## 这一轮做了什么

7 条记录的共同点是**代码都还活着**，所以去向不是"删掉结论"，而是先确认每条的结论落在哪个活文档：

| 记录 | 现状 | 去向 |
|---|---|---|
| `pcss-integration` | `PostProcess/PCSS/` 现行 | 项目文档 `PCSS.md`（比记录详尽得多） |
| `poss-per-object-soft-shadow` | `PostProcess/POSS/` 现行 | 项目文档 `POSS.md`；`FindPass` 教训补进 [shader-structure.md](../../knowledge/unity/standard/shader/shader-structure.md) §5 |
| `fgd-lut-baker` | `Scripts/FGDLutBaker/` 现行 | 项目文档 `FGDLutBaker.md`；三条踩坑补进 [blitter-api.md](../../knowledge/unity/shader/postprocess/blitter-api.md) |
| `interaction-system` | `Scripts/InteractionManager/` 现行 | 架构归项目 `README.md`；七条踩坑抽成 [gpu-simulation.md](../../knowledge/unity/standard/compute/gpu-simulation.md) |
| `ssr-dda-fisheye-w-sign-flip` | **代码已不存在** | 通用结论留 [screen-space-marching.md](../../knowledge/unity/standard/shader/screen-space-marching.md) |
| `feature-screen-debug` | 流程现行 | 已内化在 `feature-script-structure.md` §4 与 `unity-render-debug` skill |
| `starrynight-angle-field` | `Render/VanGogh/` 现行 | 数学归 [angle-field.md](../../knowledge/unity/standard/shader/angle-field.md)；include 顺序归 [hlsl-includes.md](../../knowledge/unity/standard/shader/hlsl-includes.md) |

新增三份知识刻意放在 `standard/`（与功能无关的共性知识），不放 `shader/postprocess/`：
角场、屏幕空间步进、GPU 模拟都不属于某一条后处理链。

顺带修一处跨域断链：`meta/memory.md` 有一行指向 unity 域记录，记录退役就会断，已随本轮移除。

## 纠正：`history.md` 不满足 memory 义务

首轮结论写的是「写 `history.md` 满足 memory 记录、改索引满足第二部分，所以不需要 waiver」。
本轮 `close` 报了 `memory: MISS`，读 `close.py::check_memory` 才看清：

```python
records = [w for w in all_writes
           if w["path"].startswith(".agents/memory/")
           and re.match(_MEMORY_RECORD, ...)]
```

只认 `.agents/memory/<域>/<YYYY-MM-DD>-*.md`。`history.md` 不是 dated 记录，永远不匹配。
首轮之所以 OK，是因为那一轮**恰好**写了技能落地记录，不是 history 的功劳。

正确口径：自检收尾会稳定报一条 `memory: MISS`（warn，不阻塞）——这是有意的取舍，
清扫不该再造 dated 记录；需要干净输出用 `close --waive memory --reason ...`。
不要改 `close.py` 去凑通过。这条已回写进 SKILL.md 的 S5。

## 第三轮：证据区（同日追加）

用户「继续自检 证据」触发的 R3。结论与预期相反：**证据区几乎没有垃圾**。

32 个证据目录（unity 26 个 / meta 6 个，共 88M）逐条核对：

- 每一个都被仍然存在的 dated 记录引用，没有孤儿。
- 没有零字节文件，没有 `__pycache__` / `.npy` 残留；唯一垃圾是 1 个 `.DS_Store`。
- 75M 集中在两份大目录，而且各有**书面的保留契约**：
  - `2026-10-09-maskblur-darkspot`（35M）：10 份 GPU 原始回读是 `analyze_dense_reference.py` 重建 CPU 参考的必要输入；
  - `2026-09-21-diffusegi-ao-cleanup`（40M，含 27M 回退 tar）：README 明写「历史回退点例外，不新增 tar 包」。
- 两份 README 都把清理条件写成「随 dated memory 按精确清单人工确认」，即：**没有记录说不需要，就不能删**。

真正的体积在 `tmp/`（1.4G）：`kmtool/.venv` 1.1G 是当天新建的 ASR 工具（不动），`sessions` 268M。
会话区里有一条比「体积」更要紧的发现 —— **按大小或年龄扫会删掉登记过的唯一输入**：
`2026-10-12-maskrbr-mainlight-v2` 与 `2026-10-13-shared-mask-v1` 的 `contract.md` 登记着唯一未提交的
v1 输入，记录里写明「不能按可重建输出删除」。

实际执行（用户确认后）：

| 动作 | 体量 |
|---|---|
| 删 `.DS_Store` | 1 文件 |
| `ssc-opt-c1/output/*.rgba32f` 12 份原始回读 —— 记录声明「可由脚本重建」，同目录 CSV/PNG 保留 | 95M |
| 198 个无记录点名、无 `contract.md` 的老会话（<10-24） | 59M |
| 保留被点名的 29 个会话（含两份 contract） | 167M |

`tmp/sessions` 315 目录 268M → 117 目录 113M。删会话走 `cli.py delete` 分 10 批入桶并逐桶清空，
历史日志先行。

## 状态

记录 131（meta 53 / unity 78）→ 125（meta 54 / unity 71，含本条）；≥60 天零入链从 7 归 0；
垃圾桶 9 → 16 桶（新增 7 个本轮删除留底桶，均可 restore）。

验证：`verify.py --strict` PASS（新知识文档的链接全部可达）；`run_isolated` 4/4 PASS。

## 相关

- 技能与首轮：[structure-selfcheck-skill](2026-10-30-structure-selfcheck-skill.md)
- 历史时间线：[unity/history.md](../unity/history.md)
