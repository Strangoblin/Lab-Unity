---
paths:
  - ".agents/**"
  - ".claude/**"
  - ".mcp/**"
---
# .claude 体系架构规则（meta 域）

> 本规则在编辑 `.agents/`、`.claude/` 或 `.mcp/` 下任何文件时自动注入。meta-developer 的一切操作必须遵守；其他 agent 触碰体系文件同样生效。

## 文件分类 — 归属判定（新增/迁移前必查）

新增或移动任何共享体系内容，先按"**谁加载它**"判定归属，不默认放顶层。

| 内容 | 归属 | 机制原因 |
|------|------|---------|
| skills/ | 顶层 `.agents/skills/`（Claude 兼容层按需保留） | Skill 发现机制只认此目录 |
| rules/ | 顶层 `.agents/rules/`（Claude 规则入口按需兼容） | `paths:` frontmatter 按文件路径全局生效 |
| references/ | `.agents/agents/<name>/references/` | agent 激活时自动加载 = 归属即加载 |
| templates/ | `.agents/agents/<name>/templates/` | 与 reference 配对，同属一个 agent |
| memory/ | `.agents/agents/<name>/memory/` | 按 agent 领域归属 |
| CLAUDE.md / settings.json | 顶层 | 项目级共享配置 |

判定口诀：**skills/rules 按"机制"归顶层，references/templates/memory 按"内容"归 agent**。内容归属拿不准时问"这个文件由谁加载"，不是"内容属于谁"。

## Unity MCP 边界

- `meta-developer` 以及 `.agents/**`、`.mcp/**`、`.claude/**`、`.codex/**` 体系维护绕过 `unity-gate`，不选择 recipe、不调用 `write_gated` / `delete_gated`。
- Meta 直接编辑体系文件，并以交叉引用检查、`.mcp/tests/` 与 `.codex/tests/06_architecture/verify.py` 验收。
- `Assets/Mine/**` 仍属于 Unity 业务门禁范围；切换到 `unity-developer` 后执行正常 MCP 链。

## 重复审查 — 事前清单对照（写入前必查）

- 新增 reference/rule/template 前：**先 grep 全库**（含所有 agent 层）同名/同主题，已有则合并或链接，不另起新文件
- 迁移/内化前：**先 ls 目标层已有文件清单**，对照后再放入——迁移必须核对目标层，不是只搬源文件
- README 索引的"待补"槽位必须与实际文件一致：迁移完成后立即删除已补槽位（槽位残留 = 未对照的信号）
- 同一事实只留一份权威来源，其他位置用链接指向；链接要写成相对路径，移动后同步

## 链路保障 — 改一侧必须同步另一侧

- 修改任何路径/结构后：**grep 全库找引用点**（文档内自引用、@-路径、capability/mode 引用、CLAUDE.md、自描述清单、门禁配置）逐个更新
- `.agents`（共享大脑）↔ `.claude` / `.codex`（适配层）↔ `.mcp`（骨架）并行层：改一侧必须同步另一侧（GATE_REGISTRY、RECIPES、工具签名、测试用例）
- 结构变更后必须验证：grep 残留引用零输出 + 跑 `.mcp/tests/` 测试套件
- memory 声称的强制配置需**实测验证**（settings deny、paths 门控等——memory 可能声称存在但实际从未配置）
- 历史 memory 文件是当时快照，引用旧路径**不改**；只更新活动文档

---

## 溯源

- 2026-08-24 references-relocation：顶层 `.claude/references/` 与 agent 层重复 4 处，根因 = 内化迁移无归属规则
- 2026-08-24 mcp-gate-audit：G1.5 新增只改大脑没同步骨架，根因 = 无链路同步规则
