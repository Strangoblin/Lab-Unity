# agent-maintenance scripts

本目录是 agent-maintenance skill 所属的可执行脚本。与 `references/` 分工：references 放命令与工作流，本目录放真正执行的代码。**代码不粘进 Markdown。**

脚本路径一律经**项目根**解析（`PROJECT_ROOT`，自 `__file__` 向上推算），不写死用户机器路径。

| 脚本 | 用途 | 用法 |
|------|------|------|
| [check_api_refs.py](check_api_refs.py) | 模板 API 核验：写下的 API 存在且没过时（词法 + 过时 API，`--compile` 可选真编译）。**不属门禁链** —— 模板写入不经 `write_gated` | `python3 .agents/skills/agent-maintenance/scripts/check_api_refs.py <dir>` |

判据、边界与控制组要求见 [../references/template-validation.md](../references/template-validation.md)。
