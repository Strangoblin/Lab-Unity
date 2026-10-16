---
name: interior-map-window-placement
description: InteriorMap Baker EditorWindow moved into its feature-owned Editor directory and template references synchronized.
date: 2026-09-04
---

# InteriorMap Baker Window 归位（2026-09-04）

## 变更

- `InteriorMapBakerWindow.cs` 与 `.meta` 从全局 `Assets/Editor/` 迁入 `Assets/Mine/Scripts/InteriorMapBaker/Editor/`。
- 保留原 GUID `3252e5149276147ec9b205e27442c2d7`，类内容未修改。
- 同步更新 window / baker 模板 README 中的项目范例路径，消除活动文档旧引用。

## 原因

EditorWindow 壳属于共享 window 家族，但具体实现仍应归属消费它的功能目录；`<Tool>/Editor/` 同时保持 Unity Editor-only 编译边界和功能内聚。

## 验证

- 源/目标迁移前 SHA-256 一致，迁移后 GUID 唯一。
- `check_norm.py` 通过，活动文档中不再引用旧路径。
