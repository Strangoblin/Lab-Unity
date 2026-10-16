---
name: feature-screen-debug-retired
description: 2026-08-27 — feature-screen-debug 临时脚本/引用退役，屏幕调试固定为 DebugOutputFeature；SSL Volume 接入方法归档 volume-component.md
date: 2026-08-27
metadata:
  type: project
---

# feature-screen-debug 退役 + SSL Volume 方法归档（2026-08-27）

## 变更

- **删除** `references/feature-screen-debug.md` + `scripts/roslyn/feature-screen-debug.cs`：临时设置脚本已由固定 Feature `Assets/Mine/Scripts/Debug/DebugOutputFeature` 取代（Inspector 指定 shader + 勾选 debug，无需 json / Roslyn）
- **屏幕调试方法** 并入 `references/csharp-dev/script-structure.md`「屏幕调试方法」：Feature 侧 Debug 约定 / unityctl 调试步骤 / 失败定位顺序 / 固定 Feature 指针
- **SSL Volume 接入方法**（2026-08-27 解耦记录）归档到 `references/unity6-api/volume-component.md`「Feature 接入 Volume 完整流程」：组件定义（含枚举参数）、每帧读取 Volume 栈、关键字切换、Volume vs settings 差异；SSL.md 只留 SSL 特有恢复步骤 + 指针
- **同步引用点**：cli/roslyn.md（脚本表 + 引用）、skills/auto-manager/capabilities/script-decision.md（分支树 + 脚本库表）、.mcp/validation/script_library.py（KNOWN_SCRIPTS）、unity-developer.md 自描述 references 列表、memory/2026-08-25-feature-screen-debug.md（指针更新为 script-structure.md + DebugOutputFeature）
- 验证：`.mcp/tests/test_recipes.py` All tests passed；残留引用 grep 仅剩历史 memory 自身 slug

## 为什么

临时工具沉淀为固定实现后，脚手架文档退役；通用方法归入唯一权威 reference（同一事实只留一份），避免复制粘贴散落。

## 关联

[[2026-08-25-feature-screen-debug]]（决策记忆仍有效，指针已更新）
