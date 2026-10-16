# Shader 菜单改名历史证据

来源：2026-09-17 的 84 项改名计划；背景见 meta-developer `2026-09-17-shader-menu-naming.md`。
`mapping.json` 保留全部菜单名映射、逐文件 before/after 的 SHA256、Git blob 与变更 commit。
9 个 before 和 4 个 after 未出现在 Git 历史，正文保存在 `snapshots/`，逐字校验通过。
其余 155 个正文从现有 Git blob 实际回读校验，故不重复收纳完整正文。

恢复某个历史正文时，先查看 mapping：有 Git blob 用 `git cat-file blob <oid>`；
无 Git blob 则读取对应 snapshot。恢复只是读取历史文本，写回 Assets 需另行任务与门禁。
这些版本仅描述改名时状态，既不是当前功能权威源，也不声称能编译或运行。
2026-10-10 审计时 60 项与 after 相同、14 项后续变更、10 项已不存在；没有保留 before 的现存项。
全部映射与 168 个正文均在归档准备中校验；旧 8.94 MB 原计划只能在最终归档复核后按批准清单删除。
