---
name: experiment-mode
description: Experiment Mode 加入 unity-developer — WebSearch Plan + 自主迭代 + 3 大循环退出
date: 2026-08-03
metadata:
  type: project
---

# Experiment Mode 加入

## 触发原因

用户发现现有 Research（人工观测 shader 效果）和 Production（有模板一条龙）无法覆盖第三种需求：
1. 不需要人工监督（或无正向效果）
2. 库内无模板/方案，需从网络搜索
3. 无断点，持续迭代直到目标效果

## 设计方案

### 模式入口
- 本体：`skills/auto-manager/modes/experiment.md`
- 宪法（选择逻辑+对比表）：`agents/unity-developer.md`
- 路由速查：`SKILL.md` + `AutoMode.md`

### 工作流
- E1: WebSearch → Plan → 用户确认（技术路线+方案来源+模板）
- E2: 按 Plan 迭代：编译(≤3) → 结构化验证 → .backup/ 备份
- E3: 3 次失败 → WebSearch + 修改 Plan → 3 大循环失败→保留现场退出
- E4: 正常退出 → 产物路径报告 + 提示清理 .backup/

### 关键决策
- 去掉自动 memory 写入（测试发现 agent 会把 memory 写到错误位置 .memory/）
- .backup/ 和 .memory/ 都在项目文件夹内
- 失败保留现场，不自动回滚

## 与其他模式的区别

| | Research | Production | Experiment |
|--|---------|-----------|-----------|
| 方案来源 | 人工试 | 库内模板 | WebSearch |
| 观测 | 每步暂停 | 仅异常 | Plan确认+自主 |
| 循环 | 无限 | 一次 | ≤3大循环 |

## 测试验证

首次在 POSM 逐物体阴影开发中使用 Experiment Mode，工作流正常：
- E1 Plan: WebSearch 找到 GavinKG/PerObjectShadowSRP 等 3 个参考
- E2 迭代: 7 个文件产物，编译通过
- 发现 memory 写入不稳定 → 已移除此步骤
