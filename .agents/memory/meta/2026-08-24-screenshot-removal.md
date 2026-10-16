---
name: screenshot-removal
description: 2026-08-24 — 移除 .claude 体系全部截图环节，验证统一为结构化 + 人工观测
date: 2026-08-24
type: feedback
---

# 截图环节移除（2026-08-24）

用户指令：进入 meta 模式，将所有截图环节去掉。

## 根因

截图（`unityctl screenshot capture`）在本项目是低效验证手段：
- Read PNG 返回 [Unsupported Image]（分辨率不匹配时），AI 无法可靠读取
- 消耗 context、跨迭代不可 diff、像素细节不可靠
- 结构化验证（snapshot / logs / script eval）覆盖了 90%+ 的验证需求

## 决策：验证二分法

| 验证目标 | 手段 |
|---------|------|
| 结构化（层级/组件/值/日志/UI 坐标） | `snapshot` / `logs` / `script eval` |
| 视觉质量（颜色、透明度、动画流畅度） | **人工在 Editor Game 视图观测** |

## 改动清单

- **删除**：`capabilities/screenshot.md`（git rm）
- **清理引用**（9 个文件）：
  - `AutoMode.md` — capabilities 索引行删除
  - `capabilities/cleanup.md` — 截图缓存清理行 + [L2] 扫描步骤（L1-L5 → L1-L4）+ 备份/stash 范围去 `Screenshots/` + 报告文案去"Y 张截图"
  - `agents/unity-developer.md` — C2 宪法去 `Screenshots/`、模式对比表"非必要不截图"→"人工观测视觉"、验证表视觉行删除、退出条件"截图无法判定"→"人工在 Editor 观测"
  - `cli/unityctl.md` — 工作流注释 + 最佳实践 90 条改写
  - `cli/roslyn.md` — 截图食谱条目删除
  - `modes/experiment.md` — E2 注释、Rationalizations、Verification 三处去截图表述
  - `skills/unity-editor/SKILL.md` — Verifying Changes 节改"人工在 Editor 观测"，视觉行从工具表删除
  - `skills/unityctl-plugins/SKILL.md` — smoke 示例 `screenshot capture` → `snapshot`
- **保留边界**（报告中声明）：
  - `cli/unityctl.md:79-88` 命令清单（Screenshots & Recording 节）— 工具事实文档，unityctl 仍支持该命令
  - `skills/unity-editor/SKILL.md` frontmatter + `dirtybitgames-unity-editor/README.md` — 激活条件/第三方来源描述，非执行环节

## 经验

- "去掉 X 环节" 的执行口径：**环节类引用（工作流步骤/验证手段/清理目标）全清；工具事实文档（命令清单/激活描述）保留**。删后者会让文档失真且破坏 skill 激活。

相关：[[knowledge-internalization]]
