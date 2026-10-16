# 共享技能索引

`.agents/skills/` 是技能及其执行资源的唯一编辑位置，每个技能由 `SKILL.md` 提供能力入口。

Unity 任务首次分派到 [autoagent](auto-manager/SKILL.md)，再由其选择开发流程或专门能力。下表是能力清单，不在角色 / 总索引中另建并行流程；明确点名的专门能力与已分派任务沿用当前分支。

| Skill / 目录 | 工作范围 | 状态 / 来源 |
|---|---|---|
| [agent-maintenance](agent-maintenance/SKILL.md) | 体系编辑、索引与引用核验、平台同步、迁移 | 本项目维护 |
| [autoagent](auto-manager/SKILL.md) / `auto-manager` | Unity 统一任务分流、开发模式与验证编排 | 本项目维护 |
| [unity-editor](unity-editor/SKILL.md) | Editor 连接、CLI、编译、场景、运行、日志与测试 | 本项目维护 |
| [unity-feature-reuse](unity-feature-reuse/SKILL.md) | 同类实现比较、公共流程与函数复用 | 本项目维护 |
| [unity-render-debug](unity-render-debug/SKILL.md) | 中间输出、Game View 观察、完整相机 PNG | 本项目维护 |
| [unity-render-benchmark](unity-render-benchmark/SKILL.md) | 渲染同步墙钟 A/B 采集与结果判读 | 本项目维护 |
| [codex-bridge](codex-bridge/SKILL.md) | Claude / Codex 接口与共享入口同步 | 本项目维护 |
| [codex-opencode-go](codex-opencode-go/SKILL.md) | 本项目的 OpenCode Go 委派边界（通用手册见全局权威） | 本项目维护 |
| [unityctl-plugins](unityctl-plugins/SKILL.md) | unityctl 插件创建与实现 | 外部：unityctl-plugins 目录 |

外部来源仅标注技能出处，不构成第三套知识；来源追踪见 [knowledge/external/README.md](../knowledge/external/README.md)。

## 历史兼容

`codex-orchestrate` 项目版本已退役，接入配置与双边契约分别由对应技能持有。
