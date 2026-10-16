---
name: references-relocation
description: 2026-08-24 — references 归 agent：顶层 .claude/references/ 迁入 unity-developer + rules 瘦身 + 合并重复
date: 2026-08-24
metadata:
  type: project
---

# references 归 agent 结构审计与调整

## 背景

用户提问：rules 和 references 是否有重合？表面内容是否应分类到具体 agent（skills 除外）？

## 审计发现（4 处重复）

1. **rules ↔ references 三对重复**（同事实两形态）：
   - rules/compute-shader.md ↔ agents/unity-developer/references/urp-shader-lib/compute-shader.md（逐条同内容）
   - rules/shader-development.md 必须项 ↔ shader-structure.md:220-223
   - rules/csharp-renderpass.md ↔ script-structure.md + unity6-api/render-graph.md
2. **顶层 ↔ agent 完全重复**：references/unity6-api/postprocess-differences.md（306 行新）↔ agents/unity-developer/references/legacy-memory/unity6-shader-differences.md（175 行旧）——同一文档双份，内化迁移未删旧副本
3. **README "待补"槽位全部已补**：顶层 README 标注 blit-fullscreen/hlsl-includes/compute-shader/render-graph 等"待补"，实际 agent 层早已存在——迁移时未对照 agent 层已有文件

## 归属结论（分三档）

| 内容 | 归属 | 原因 |
|------|------|------|
| skills | 顶层 | Skill 发现机制 |
| rules | 顶层 | `paths:` frontmatter 按文件路径全局生效，与 agent 无关；移入 agent 后失去全局路径门控 |
| references | 归 agent | 顶层 4 目录全部是 Unity 域内容；agent references/ 激活时自动加载 = 归属即加载 |

## 执行（2026-08-24）

1. 删 legacy-memory/unity6-shader-differences.md（新版章节结构完整覆盖旧版，git rm）
2. 顶层 references/ 全部迁入 agents/unity-developer/references/：shader-structure.md → urp-shader-lib/、script-structure.md → csharp-dev/（新建）、postprocess-differences.md → unity6-api/、2 个 doc-template → templates/；README 三份合并（agent 版加新文件行）
3. rules 瘦身：compute-shader.md / csharp-renderpass.md 头部加"完整知识见 references"链接；shader-development.md 链接更新 + ddx/ddy 详述压缩为铁律 + memory 链接
4. 同步引用点：C7 宪法、knowledge.md 全文件重写、CLAUDE.md 快速参考 + 架构说明、AutoMode.md、meta memory.md 体系状态、unity-developer.md 自描述、script-structure.md 内部路径

## 验证

- grep 活动文档无残留 `.claude/references/` 引用（历史 memory 保留为快照）
- MCP 测试 7 用例全绿——g_knowledge 按**文件名**匹配（shader-structure.md/script-structure.md），路径无关设计使迁移零影响

## 经验

- **README 的"待补"槽位是迁移未同步的信号**：内化迁移把新副本放顶层，没对照 agent 层已有文件，导致槽位与实际存在错位——迁移/内化时必须先清单对照目标层
- **rules 不能按"内容归属"移动，按"生效机制"移动**：paths 门控 = 全局生效，归 agent 后只在 agent 激活时生效——归属分析要问"这个文件由谁加载"，不是"内容属于谁"
- 结构规范类文档内部写有自身路径（script-structure.md 描述自己的目录结构）——移动后也要同步

## 根因修复（同日补录）

用户指出根因：**meta-developer 有 P1-P3 设计原则但无执行规则**——P3 是"事后去重"，缺"事前归属判定"和"过程中链路同步"，导致内化迁移时默认放顶层 `.claude/references/`（Harness 默认位置）而非按 agent 归属。

修复：新建 `.claude/rules/meta-architecture.md`（`paths: [".claude/**", ".mcp/**"]` 路径门控自动注入）三条规则：
1. **文件分类**：按"谁加载"归属——skills/rules 归顶层（机制），references/templates/memory 归 agent（内容）
2. **重复审查**：写入前 grep 全库对照（含 agent 层）；迁移前 ls 目标层清单；README"待补"槽位迁移后立即清理
3. **链路保障**：改路径/结构后 grep 全库同步引用点；.claude↔.mcp 改一侧同步另一侧；结构变更跑 .mcp 测试；memory 声称的强制配置实测验证

同步：meta-developer.md 设计原则段加规则引用行；CLAUDE.md 架构说明补规则分层（Unity 域 paths→Assets/Mine/ + meta 域 paths→.claude/**.mcp/** 共存顶层 rules/）。

相关：[[knowledge-internalization]]、[[ecs-decoupling-refactor]]、[[mcp-gate-audit]]
