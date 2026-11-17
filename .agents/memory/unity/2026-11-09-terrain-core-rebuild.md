# TerrainManager 最小核心重建（2026-11-09）

`GrassManager` 与 `InteractionManager` 原先各自持有一套地形真相（`ProceduralGrassTerrain` / `FlatGrassTerrain`），旧 `TerrainManager` 又是与 Voronoi/城市内容耦合的半废案。本轮把地形拆成独立的两层，先只做数据层与渲染层，网格用单块默认网格。

## 当前契约

- **数据层** `Data/TerrainGrid`：高度存在**顶点**，尺寸 `(WidthCells+1) × (HeightCells+1)`；地表层存在**格**。每格沿 `(0,0) → (1,1)` 对角线切两个三角形：`u >= v` 用 `(00,11,10)`，否则 `(00,01,11)`。`TrySample(worldX, worldZ, out TerrainSample)` 与 `TerrainMeshBuilder` 必须共用这套剖分，否则草/交互会落在错误的面上。
- **发布** `TerrainSnapshot`：`Grid` + 递增 `Version`；消费者以版本变化作为重建信号。`TerrainManager` 事务式生成，新资源全部成功后才替换旧快照，`OnDisable` / `OnDestroy` 释放。
- **提供者** `ITerrainProvider`：`Flat` 与 `Noise` 两种内置实现；只写数据，不创建场景对象。
- **渲染层** `TerrainSurfaceRenderer`：单块 Mesh、颜色图尺寸 = 格数、材质始终创建实例（不修改传入的 `_surfaceMaterial`）；Mesh / Material / Texture 均由渲染层释放，场景对象挂 `HideFlags.DontSave`。

## 兼容处理

当时为保住编译，旧 `TerrainType/City/Editor`、`TerrainType/Countryside/Editor`、`Editor/TerrainExperimentSetup.cs`、`Editor/TerrainExperimentValidation.cs` 曾被移入 `TerrainManager/Legacy~/`（`~` 目录不编译），旧 `TerrainGenerator/`、`TerrainRenderer/`、`TerrainType/` 运行时脚本暂留为未被引用的历史代码。

**2026-11-11 起全部删除**，`Legacy~/` 与旧类型体系不再存在；`TerrainGenerator/`、`TerrainRenderer/` 两个名字被新结构复用（内容已是 provider 与渲染层）。详见 [旧体系退役](2026-11-11-terrain-legacy-removal.md)。

## 验证

- `unityctl asset refresh` 编译通过。
- `Validation~/TerrainCoreChecks.cs`：Flat/Noise 高度与层、顶点采样一致性、网格顶点/三角形计数与绕序、渲染资源创建/释放、Manager 版本递增与释放全部通过。
- Play Mode 视觉冒烟：`Validation~/TerrainVisualSmoke.cs` 在 `(2000,0,2000)` 生成 60×60 噪声地形并架相机，截图可见单块起伏地表与 Grass/Sand/Rock 色块。

## 坑

- 编辑器失焦时 Unity 不跑帧循环，`ScreenCapture.CaptureScreenshot` 和 `unityctl screenshot capture` 都不会写文件；截图前先 `Application.runInBackground = true`（或让 Game 视图保持焦点）。

## 后续

- `TerrainGrassSource : GrassTerrainSource` 适配器挂在 TerrainManager 根节点，`TrySample` 转接网格采样。
- `TerrainInteractionAnchor` 用采样移动 `_followTarget` 的 Y；深度代理用独立层 + `DepthOnly`，复用同一网格。
- 分块 / LOD、其他程序化网格层从 `TerrainMeshBuilder` / 渲染层扩展，不改数据网格契约。
