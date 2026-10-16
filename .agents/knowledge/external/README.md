# 外部来源索引

本目录只登记可确认的外部来源，用于来源追踪，不新增知识正文，也不复制技能内容。体系内部事实见 [knowledge/meta](../meta/README.md)。

| 外部来源 | 任务领域 | canonical 实体入口 |
|---|---|---|
| [Claude Code 官方结构](https://code.claude.com/docs/en/claude-directory) | Claude `.claude` 目录规范 | [../meta/claude-code-structure.md](../meta/claude-code-structure.md) |
| [Hermes Agent Docs](https://hermes-agent.nousresearch.com/docs/developer-guide/creating-skills) | Skill 目录规范 | [../meta/hermes-agent-skills.md](../meta/hermes-agent-skills.md) |
| unityctl-plugins 目录来源 | unityctl 插件创建 | [../../skills/unityctl-plugins/SKILL.md](../../skills/unityctl-plugins/SKILL.md) |

第三方技能保留在原 canonical 目录，本表只记录来源与入口。

## 已评估移除（2026-10-15）

| 来源 | 移除原因 |
|---|---|
| skillstore.io / sickn33-unity-developer | frontmatter `name: unity-developer` 与本项目角色同名；正文为通用能力清单，无本项目事实；引用不存在的 `resources/implementation-playbook.md`；覆盖面（联机、主机、音视频、变现）远超本项目范围 |
| skillstore.io / dwsy-project-planner | 全文无 Unity / 游戏 / 渲染相关内容，模板面向 Web / 微服务 / 数据管道；两个 42KB 脚本未被 SKILL.md 引用；含已废弃的 `.claude/skills/` 安装指引与互相矛盾的作者元数据 |

按需可从上游重新获取；`git checkout -- <path>` 可还原本次移除。
