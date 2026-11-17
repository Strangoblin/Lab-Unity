# TerrainManager 接入 GrassManager（2026-11-09）

**接入**：新增 `GrassManager/Runtime/TerrainGrassSource.cs`（`Mine.Grass`），把 `TerrainManager` 适配成 `GrassTerrainSource`——位置、法线、地表层全部取自 `TerrainGrid.TrySample`，适配器不持有地形数据。`DataVersion` 把「地形版本」与「地块边长」合并成单调计数，所以改地块边长只重建草、不动地形；`Width/Depth` 覆盖基类的 `ChunkCount × ChunkWidth`，避免非整除边长把世界范围撑大。

**地表层映射**：`GrassTerrainLayer` 补 `Rock/Sand/Snow`（8/16/32），让 `TerrainSurfaceLayer` 的五种地表都能无损映射。旧枚举只有 Grass/Dirt/Water，其余会退化成 `None`，把「未知地表」和「不种草」混为一谈。

**Demo 场景**：`GrassManagerDemo.unity` 的根节点改为 `GrassChunkManager + TerrainManager + TerrainGrassSource`，`ProceduralGrassTerrain` 退出场景。地形 100×100 格、1 m 一格、原点 (-50,0,-50)、`Noise`、高度倍率 2；`_terrainLayers` 只选 Grass，沙地与雪顶保持裸露，用来说明地表筛选真的生效。手调的 density 10 / size 4 / chunk 20 原样保留（整场 `NewScene` 重建会冲掉手调，所以见下）。

**场景重写方式**：`GrassManagerDemoSetup` 拆出 `ConfigureRoot`，新增 `Tools/Grass Manager/Rebuild Demo Scene`：打开已存场景、就地重写地形接线、保存，不 `NewScene` 重建，因此不碰相机、风场和手调参数。`Create()` 走同一份 `ConfigureRoot`，两条路径产出一致。`ProceduralGrassTerrain` 仍保留在 `Runtime/`，作为隔离验证 fixture 和另一份 `GrassTerrainSource` 参照，只是不再出现在演示场景里。

**顺带修掉 TerrainManager 的两个生命周期问题**：① 它原本不是 `[ExecuteAlways]`，编辑模式下域重载不会可靠触发 `OnDisable`，`_renderer` 字段丢失而 `HideFlags.DontSave` 的子物体留在场景里——实测堆了 3 个同名 `Terrain Surface`；② 生成物没有身份标记，入口无从回收残留。修法：加 `[ExecuteAlways]`；新增 `Render/TerrainSurfaceInstance.cs` 作标记组件（该目录 2026-11-11 改名 `TerrainRenderer/`）；`Generate()` 里 `DestroyInstancesExcept(renderer.Root)` 兜底回收，`Release()` 一并清理。生成流程仍是「先建后换」——新地表建好才回收旧的，构建失败不破坏现有地形。

**验证**：`unityctl asset refresh` 编译通过；`GRASS_SAVED_SCENE_VALIDATION_PASS: terrain=100x100, chunkSize=20, instances=35865, visible=3424`。新增 `RunSavedSceneInEditor`，编辑器内即可跑（要求 Demo 是活动场景），不再强制 batch。地表唯一性：连续 `Generate()` 两次、Play 进出一轮后仍只有一块 `Terrain Surface`。证据 `GrassManager/Validation~/2026-11-09-terrain-integration/grass-terrain-integration.png`。

**边界**：草实例仍是内存数据，未存库；距离淡出、异步预算、GPU 剔除仍缺；`ProceduralGrassTerrain` 未删除。相关：[草区块模板](2026-11-03-grass-manager-template.md)、[地形核心重建](2026-11-09-terrain-core-rebuild.md)。
