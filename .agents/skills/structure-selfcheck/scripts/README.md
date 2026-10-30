# structure-selfcheck scripts

本目录放真正执行的代码；命令与工作流在 `references/`，**代码不粘进 Markdown**。

脚本路径一律经**项目根**解析（自 `__file__` 向上四级），不写死用户机器路径。

| 脚本 | 用途 | 用法 |
|------|------|------|
| [inventory.py](inventory.py) | 只读盘点：记忆年龄 / 活文档入链 / 索引覆盖 / 证据目录 / 垃圾桶分类 / 孤儿。**不做判断**，判断留给 agent | `python3 .agents/skills/structure-selfcheck/scripts/inventory.py [--json] [--check]` |

## 选项

| 选项 | 含义 |
|---|---|
| `--json` | 机器可读全量输出 |
| `--check` | 退役候选 ≥ 20 或垃圾桶 ≥ 15 桶时退 1，用于到期提醒 |
| `--max-age N` | 记忆年龄阈值，默认 60 天 |
| `--stale-limit N` / `--bucket-limit N` | `--check` 的两个阈值 |

只读是硬性质：脚本不删除、不改写、不移动任何文件。唯一的写操作是 stdout。

判据见 [../references/memory-archive.md](../references/memory-archive.md) 与
[../references/trash-triage.md](../references/trash-triage.md)。
