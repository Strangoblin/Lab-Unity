---
name: 空间滤波预算与半径指导
description: 将 Blur、SNN、Kuwahara 和 Resolve 的质量预算与感受野半径拆分为统一规范和模板
date: 2026-09-21
---

# 空间滤波预算与半径指导

## 变更

- 新增 `references/shader/postprocess/spatial-filter-budget.md`，作为空间滤波参数语义的唯一权威源。
- 新增 `templates/shader/postprocess/spatial-filter-budget.hlsl`，提供 Low/Medium/High 固定 9/16/25 tap 与独立 radius offset 骨架。
- 修正 `fullscreen-structure.md` 将采样半径误归循环成本参数的旧表述，并修正关键字示例正反标记。
- 在参数面板规范、C# 面板模板、Volume 示例、后处理索引和 HLSL 家族索引中建立交叉引用。

## 裁决

- Quality 属于 Performance，决定采样数、分布模板、算法近似和可选工作分辨率。
- Radius 属于 Artistic，决定输入纹理空间中的感受野，只缩放固定 offset。
- 禁止由 radius 推导循环边界、tap 数或 RT 分辨率。
- 若算法只能使用离散半径，则半径并入质量档，不再暴露第二个连续来源。

## P1-P3

- P1：说明集中在 41 行 reference；可复制代码独立为 HLSL template。
- P2：未向 skill 嵌入实现代码。
- P3：检索现有 Blur/SNN/Kuwahara/quality/radius 内容后合并旧规范，没有新增算法重复文档。

## 验证

- 新增及修改的活动 Markdown 均不超过 80 行（历史 `fullscreen-structure.md` 除外）。
- 新增相对链接均解析成功。
- `check_api_refs.py` 对后处理模板目录全绿。
- Codex Phase 7 架构验证通过，broken links 与 drift 均为 0。
