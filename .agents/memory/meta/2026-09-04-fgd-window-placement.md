---
name: fgd-window-placement
description: FGDLutBakerWindow moved from the global Editor folder into its feature-owned Editor directory.
date: 2026-09-04
---

# FGD LUT Baker Window 归位（2026-09-04）

## 变更

- `FGDLutBakerWindow.cs` 与 `.meta` 从全局 `Assets/Editor/` 迁入 `Assets/Mine/Scripts/FGDLutBaker/Editor/`。
- 保留文件 GUID `720d800ce80954c1b9886d4ddfc9b043`；新增功能 `Editor` 目录 GUID `82a4fc129e6e42d79936522edf17e99f`。
- 同步更新功能文档与 window / baker 模板 README，移除活动文档中的遗留路径。

## 验证

- 迁移前后文件 SHA-256 一致，代码内容未修改。
- `check_norm.py` 通过，文件 GUID 在 Assets 中唯一。
- Git stash `3903b579b93a6f680beeecb342d8ea1655874606` 保留迁移前当前修改。
