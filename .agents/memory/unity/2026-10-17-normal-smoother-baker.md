# 2026-10-17 NormalSmoother 窗口收敛为标准 Baker

## 用户裁决

批量版窗口为兼容多种输入保留了选择列表、子级遍历、场景回写与覆盖开关，内容繁杂且不符合新项目标准。改为：**输入固定为单个 Mesh**，窗口按标准 Baker 框架重写。

## 变更

- 删除：多目标列表、`MeshFilter` / `SkinnedMeshRenderer` / `GameObject` / Prefab 收集、Include Children / Inactive、Apply Back To Scene、Overwrite 开关、输出文件夹浏览、批量进度循环。
- 新增/保留：`_sourceMesh`（`ObjectField` 限定 `typeof(Mesh)`）、固定资产路径 `_assetPath`（默认 `Assets/Mine/Generated/NormalSmoother/SmoothedNormal.asset`）、`[Bake][Load][Save]` 行动行、`BakeCore()`、`CanBake`（非空且可读）、`ShowBakeProgress = true`、`HasSaveableAsset`、`OnAssetChanged`、窗口资产所有权（Bake 产物销毁、Load/Save 资产不销毁）。
- 落盘沿用项目标准 `AssetFiles`：同名同类型 `CopySerialized` 原位更新保 GUID，覆盖前确认，只 `SaveAssetIfDirty` 目标资产；Load 校验目标确实是 Mesh。
- 预览改为直接渲染当前资产网格，去掉临时平滑网格缓存；关窗释放 `PreviewRenderUtility` 与材质。

## 验证（已验证）

- `unityctl asset refresh`：编译成功；`check_norm.py` 通过。
- 菜单 `Tools/Normal Smoother...` 打开 1 个窗口实例。
- 反射设置 `_sourceMesh` 后调用 `BakeCore()`：Cube 输出 24 顶点，`VertexColors` 模式下颜色与法线各 24 条。
- 反射调用嵌套 `AssetFiles.Save`：自动建目录并创建资产；再次保存触发覆盖对话框，点击 Overwrite 后返回同一资产实例、GUID 不变（`7f7be5e7171d211419165bcb14111387`）。测试资产在 `finally` 中删除，无残留。

## 未验证 / 注意

- 未人工点击 GUI（Bake / Load / Save 按钮、预览画面）。
- `AssetFiles.Save` 的覆盖分支会弹出模态对话框；自动化测试必须先 `unityctl dialog dismiss --button "Overwrite"`，否则脚本会阻塞等待。
- 预览只绘制子网格 0（沿用原实现）。