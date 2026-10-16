---
name: template-conventions-promoted
description: 2026-09-03 用户要求 meta 库更新「unity库、模版文件编写规则」—— 前几轮模板规则从 memory 提升为活动规范：layer-conventions Template 层刷新 + 新增 template-conventions.md 细则。
metadata:
  type: architecture
---

# 模板/库编写规则提升为活动规范 — 2026-09-03

## Context

模板体系四轮（taxonomy → standard 代码框架 → window 家族 → render/hlsl 家族）沉淀的规则只活在 dated memory 与 unity 侧族 README，meta 侧 layer-conventions 的「Template 层」表陈旧（类型缺 .hlsl、来源标注写"Unity 官方文件路径"、无家族结构/README-only-md/可编译承诺）。用户指令：在 meta 库更新 unity库、模版文件的编写规则。AskUserQuestion 裁定 = **仅 meta 侧文档**（Package A，不改 unity references/standard）。

## 落地

1. `references/layer-conventions.md`：层级总览 templates 行、Reference 层「完整代码」行（+ .hlsl）、**Template 层表刷新**（家族结构 / .hlsl 与 doc .md 类型 / 独特族只 README.md 为 md / 横幅 ⚠️ 可编译承诺 / YourXxx 占位符 / 实源重写 / 细则指针）、反模式 +2 行（悬空引用、独特族第二 .md）
2. **新增 `references/template-conventions.md`**（细则权威，≤80 行）：分类总则 + 家族树表（链 unity 侧族 README，不重述内容）+ 模板文件编写规则表 + HLSL 库规则摘要（拆库裁决 / guard 绑定 / 三档依赖 / 命名前缀 / MainLight 地雷）+ 验证清单（check_norm / ls / grep / 链解析 / memory）
3. `references/README.md` 索引加行
4. unity 侧不动（用户先裁定 Package A）；**当日后续用户批准跨侧同步** → 修正 `unity-developer/references/standard/shader/shader-structure.md`：RainDrop.hlsl 引用改真实同目录路径、③ 补拆库裁决（私有库随 shader 同目录 vs Special/HLSL 共享）、文件组织树删 `Special/Shaders/` 旧目录并加退役注、模板决策表链到 render/hlsl 族 README

## 原则应用

P1（MD 索引 + 链接不重述）——新细则只摘要，细则权威保留在族 README 与 rules/shader-development.md；P3 去重——并入既有 layer-conventions 不另开第二份层级约定。
