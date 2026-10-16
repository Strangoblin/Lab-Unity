---
name: editor-window-family-ruling
description: 窗口壳归属裁定——不属于 baker 独特模板,新开 script/window/ 家族承载。
metadata:
  type: architecture
---

# EditorWindow 归属裁定 — 2026-09-03(更正)

## 更正

早前本文件声称 Added `templates/script/baker/editor-baker-window.cs` 为 Baker 家族成员——**该文件从未落盘**,记录与实际不符(此即悬空引用根因:baker/README 链向不存在的文件)。

## 裁定

用户裁定:EditorWindow 壳**不属于 baker 独特模板**,它是跨 baker/generator 的共享形态 → **新开 `templates/script/window/` 家族**承载。

- 模板:`templates/script/window/editor-window.cs`(通用壳,类名 `YourToolWindow`)+ `window/README.md`(消费差异表)
- baker/generator 的 README「族内结构」只保留各自服务层,窗口壳改链 `../window/README.md`
- 窗口壳标准化结构(本裁定沿用,无变化):行动行统一 Height(30)、Bake/Load/Save vs Generate/Save 差异、`_ownsTexture` 所有权、AssetDatabase 资产永不销毁、Save 前覆盖确认、归位 `Assets/Mine/Scripts/<Tool>/Editor/`

## Source implementations

- `Assets/Editor/FGDLutBakerWindow.cs`(精简形态)
- `Assets/Editor/InteriorMapBakerWindow.cs`(完整 Bake/Load/Save)
- `Assets/Mine/Scripts/NoiseGenerator/Editor/`、`CurveGenerator/Editor/`(Generator 标准形)

## Usage

Check `templates/script/README.md` family table (Window 行) while writing a tool window; copy `window/editor-window.cs` into `<Tool>/Editor/`, rename class, pick the consumption form per `window/README.md`.
