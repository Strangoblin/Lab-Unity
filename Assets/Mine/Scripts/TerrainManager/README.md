# TerrainManager 地形核心

地形统一管理数据层与渲染层，向上输出两种产物：给草系统的 `GrassTerrainSource`（`GrassManager/Runtime/TerrainGrassSource.cs`）和后续的交互参考层。当前只实现单块默认网格，不包含分块、LOD 与旧的内容生成系统。

| 目录 | 职责 |
|---|---|
| `Data/` | 数据层：`TerrainGrid`（顶点高度 + 逐格地表层）、`TerrainSample`、`TerrainSettings`、`TerrainSnapshot`、`TerrainSurfaceLayer` |
| `TerrainGenerator/` | 数据生成：`ITerrainProvider` 契约与 `Flat` / `Noise` 两种内置提供者，工厂按枚举创建 |
| `TerrainRenderer/` | 渲染层：`TerrainMeshBuilder`、`TerrainColorMapBuilder`、`TerrainSurfaceRenderer`、`TerrainSurfaceInstance` 与 `TerrainSurface/` Shader |
| `Editor/` | `TerrainManagerInspector`：数据参数、生成/释放按钮与当前快照状态 |
| `Validation~/` | 不参与编译；`TerrainCoreChecks` 覆盖数据、采样、网格与渲染资源，`TerrainVisualSmoke` 用于 Play Mode 截图 |

## 数据契约

- 高度存在**顶点**，尺寸 `(WidthCells+1) × (HeightCells+1)`；地表层存在**格**。
- 每格沿 `(0,0) → (1,1)` 对角线切两个三角形：`u >= v` 用 `(00,11,10)`，否则 `(00,01,11)`。
- `TerrainGrid.TrySample(worldX, worldZ, out TerrainSample)` 与 `TerrainMeshBuilder` 共用同一剖分，返回世界位置、三角面法线、地表层与局部高度；剖分不一致会让草与交互落在错误的面上。
- `TerrainSnapshot.Version` 每次成功生成递增，消费者以版本变化作为重建信号。

## 渲染契约

- 单块 Mesh，UV 覆盖整图；颜色图尺寸等于格数，按 `TerrainSurfacePalette` 烘焙地表层颜色。
- 颜色图通过 `TerrainManager.SurfaceColorMap` 对外暴露；草系统经 `GrassTerrainSource.TryGetGroundColor` 复用这张图作为 basecolor，与地形网格共用同一套世界 XZ → UV 映射。
- 材质始终创建实例，不修改传入的 `_surfaceMaterial`；Mesh / Material / Texture 均由渲染层释放。
- `TerrainSurface.shader` 有 `Forward` / `DepthOnly` / `DepthNormals` 三个 Pass：本项目 `PC_Renderer` 的 Copy Depth 是 ForcePrepass，`_CameraDepthTexture` 只由 prepass 产生，缺 `DepthOnly` 的地表不会出现在深度与法线缓冲里，SSGI / AO 之类会把它当成空面。`DepthNormals` 用 `TerrainMeshBuilder` 写入的平滑顶点法线，`_GBUFFER_NORMALS_OCT` 分支与 URP 一致。
- 生成物挂 `HideFlags.DontSave` 并带 `TerrainSurfaceInstance` 标记，域重载后 `TerrainManager` 靠标记回收残留；生成流程是「先建后换」，新地表成功才释放旧的。
- `TerrainManager` 为 `[ExecuteAlways]`，编辑模式下同样生成与释放。

## 扩展点

- 新数据来源：实现 `ITerrainProvider` 写入 `TerrainGrid`，并在 `TerrainProviderFactory` 注册。
- 分块 / LOD / 深度代理：从 `TerrainMeshBuilder` 与 `TerrainSurfaceRenderer` 扩展，不改数据网格契约。
- 草与交互：继续通过 `TrySample` 与 `Snapshot.Version` 消费，无需持有渲染资源。
