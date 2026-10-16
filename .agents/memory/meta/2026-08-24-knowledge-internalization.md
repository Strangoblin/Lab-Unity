---
name: knowledge-internalization
description: 知识库内化 — Assets/MarkDowns/ 迁入 .claude/references/ + [G1.5] 知识加载硬门禁
date: 2026-08-24
metadata:
  type: project
---

# 知识库内化 + [G1.5] 知识加载门禁

## 动机

1. 用户指出知识库（Assets/MarkDowns/）是给 AI 看的，不应留在 Unity 资源区 → 内化到 .claude/
2. 路由缺陷：knowledge 预加载（[P1]）是软步骤，无硬门禁。实测只读了高优先级 4 份中的 1 份就进入了 P2，无任何阻断。根因：门禁序列只有 [G2] 脚本决策 / [G3] 文件放置，知识加载没有门禁输出格式。

## 内化结构（2026-08-24）

```
.claude/references/
  urp-shader-lib/shader-structure.md   ← 原 ShaderStructure.md（422 行）
  csharp-dev/script-structure.md       ← 原 ScriptStructure.md（282 行）
  unity6-api/postprocess-differences.md ← 原 Unity 6 全屏后处理差异.md（314 行，去 memory frontmatter）
  templates/shader-doc-template.md     ← 原 ShaderDocTemplate.md（78 行）
  templates/script-doc-template.md     ← 原 ScriptDocTemplate.md（149 行）
  */README.md                          ← 每子目录索引（含"待补"槽位）
```

原 MarkDowns 中相对路径链接（../Mine/...）全部修正为项目根相对。原目录已 git rm（可回退）。

## 门禁修复

- SKILL.md 新增 [G1.5] 知识加载校验（各模式必经，生产模式强制）
- production.md 流程图 + 门禁契约表登记 [G1.5]，PARTIAL 阻断 P2
- capabilities/knowledge.md 新增判定标准：COMPLETE = 高优先级（2 份结构规范）全读；PARTIAL = 有未读 → 阻断

## 知识库分层（内化后）

| 层 | 路径 | 加载方式 |
|---|------|---------|
| 硬规则 | rules/*.md | 自动注入（保持精简 ≤80 行，不塞 400 行规范） |
| 知识库 | references/*/ | 按需读取（可详尽） |
| 模板 | references/templates/ | 按需复制 |

## P1-P3 应用

- P1: references 每目录 README 索引 ≤80 行；结构规范全文不进 rules/（rules 自动注入，会爆 context）
- P3: 原"references vs MarkDowns 双层次"对比表已删除（两层合并，无重复）；grep 确认 .claude 内无残留 MarkDowns 活跃引用（仅剩来源标注）
