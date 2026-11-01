# Unity — 已归档记忆

> 被结构自检退役的记录在此逐日留一句。正文已删，完整内容在 git 与 `.mcp/trash/`；
> 「谁在何时删了什么」见 `.mcp/state/deletes.jsonl`。

| 日期 | 当天做了什么 | 归档记录 | 去向 |
|------|-------------|---------|------|
| 2026-10-30 | 自检 R3（证据区）：32 个证据目录逐条核对，无孤儿、无零字节，仅删 `.DS_Store`；`tmp/sessions/2026-10-20-ssc-opt-c1/output/` 的 12 份 `.rgba32f` 原始回读（95M）按记录「可由脚本重建」删除，同目录 CSV/PNG 与 evidence 保留；另清 198 个无记录点名、无 `contract.md` 的老会话（59M），被点名的 29 个（含两份 contract）保留 | `2026-10-20-ssc-performance` 的运行输出、198 个会话目录 | 原始浮点可重跑重建；长期项在 [evidence/2026-10-20-ssc-performance](evidence/2026-10-20-ssc-performance)；会话结论已在 memory，删除留底见 `deletes.jsonl` |
| 2026-10-30 | 自检 R2：unity 域 7 条 living 子系统记录退役（PCSS、POSS、FGD LUT、交互系统、SSR DDA、屏幕调试、StarryNight 角场） | `pcss-integration` / `poss-per-object-soft-shadow` / `fgd-lut-baker` / `interaction-system` / `ssr-dda-fisheye-w-sign-flip` / `feature-screen-debug` / `starrynight-angle-field` | PCSS、POSS、FGD、交互归各自项目文档（`Assets/Mine/...`，见 MEMORY「关键架构决策」）；角场、屏幕空间步进归 [standard/shader](../../knowledge/unity/standard/shader/README.md)，GPU 模拟归 [standard/compute](../../knowledge/unity/standard/compute/README.md) |
