---
name: skill-content-audit
description: 技能层内容审计：codex-opencode-go 三份漂移归并为单一全局权威；移除 3 个冗余/失配目录，skills 索引 12→9。
date: 2026-10-15
---

# 技能层内容审计与收敛

用户要求检查各 skill 是否存在重复与过期内容，点名 `codex-bridge` 与 `codex-opencode-go`。

## 审计结论

`codex-bridge` 已在上轮精简为 13 行纯路由，无残留；真正的问题是 `codex-opencode-go` 存在**三份互不相同**的副本：

| 位置 | 行数 | 状态 |
|---|---|---|
| 项目 `.agents/skills/codex-opencode-go` | 327 | 最全，但缺「历史桶」节 |
| `~/.claude/skills/codex-opencode-go` | 255 | 近似超集，缺 opencode CLI / `ocw` |
| `~/.agents/skills/codex-opencode-go` | 139 | 最旧，缺 catalog / 传输 / opencode CLI |

三份同 `name`，运行时技能列表出现两次，模型可能读到任一版本。

项目文件内的自相矛盾（已修）：

- 配置模板推荐 `disable_response_storage = true`，同文件排障表又说 0.159.2 已忽略该 key、「删掉即可」。
- `--auto` 段要求「opencode 产出 → Claude review → 合入，见 `.codex/INTERFACE.md` §3」，而该文件已改为三节结构、两侧对等通道，引用悬空。
- 「与 CC-Switch 的关系」两条并列标注 `Codex → OpenCode Go`，首条实为 Claude Code 路径。
- 整节「历史方案（opencode-go-proxy）」为死内容。

## 执行

- **单一权威落在项目内**（用户要求 git 留档）：`.agents/skills/codex-opencode-go/SKILL.md`（360 行）= 三份并集 + 上述修正 + 「历史桶」节 + `Unity/Lab` 委派边界；`~/.claude/skills/codex-opencode-go/`（255 行）与 `~/.agents/skills/codex-opencode-go/`（139 行）两个全局副本均已删除，并删除 `~/.agents/skills/codex-opencode-go/` 目录本身。
- **跨项目可达性改用明文路由**：`~/.Codex/AGENTS.md` 与 `~/.claude/CLAUDE.md` 各加一行指向该路径的说明（同项目内「入口 → 明文指引 → 实体」的既定做法），不恢复副本、不建软链。
- **移除 3 个目录**：项目 `dirtybitgames-unity-editor`（8+7 行两个文件描述同一退役事实）；`sickn33-unity-developer`（`name: unity-developer` 与本项目角色同名、无项目事实、引用不存在的 `resources/implementation-playbook.md`、覆盖面含联机/主机/音视频/变现，远超本项目范围）；`dwsy-project-planner`（全文无 Unity / 游戏 / 渲染内容，模板面向 Web / 微服务 / 数据管道，两个 42KB 脚本未被 SKILL.md 引用，含废弃 `.claude/skills/` 安装指引与互相矛盾的作者元数据）。
- 移除原因记入 `.agents/knowledge/external/README.md` 的「已评估移除」表；索引同步更新。三者均被 git 跟踪，`git checkout -- <path>` 可还原。
- `~/.claude/CLAUDE.md` 增加「全局技能」明文路由节——删除副本后 Claude 侧不再有自动发现入口，改用显式路径；`codex-opencode-go` 从全局技能表移出，只保留指向项目正文的路径。

## 验证与边界

- `.codex/tests/06_architecture/verify.py --strict` 全绿（断链、软链、所有权、平台漂移均 0）。
- `.agents/{skills,knowledge,agents}` 全库相对链接 0 断链。
- `.mcp/tests/run_isolated.py` 四套契约隔离通过，生产 state / 审计指纹不变。
- 未触碰 Unity 资产、Editor、`.mcp` 判定代码、policy 与 hooks。
- 边界：正文放项目内后随仓库 git 留档；代价是其他项目要按明文路径跨仓读取，不再是「全局技能根自动发现」。
- 遗留：`~/.claude/skills/smart-task` 与 `~/.agents/skills/smart-task` 内容相同仍双份（两者都是全局副本，项目内没有）；`unityctl-plugins` 未与当前 CLI 实测比对。

产物与证据见 `tmp/sessions/2026-10-15-skill-content-audit/`。
