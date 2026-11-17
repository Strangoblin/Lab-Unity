# TerrainManager 旧体系整体退役（2026-11-11）

用户原话：「删掉旧的 generator/renderer/type，完全用新的简单结构代替，不考虑保留任何旧功能结构」→「原来 terrain 的 scene 和资料都不要了」。这是一次**删除 + 最小结构替换**，不是移植。

## 删除范围

- 运行时：`TerrainDataGenerator`、`TerrainDataNoise`、`TerrainGenerationSettings`、`TerrainVoronoi*`（Generator/Sdf/Settings）、`TerrainMapData`/`TerrainMapBuilder`、`TerrainHeightGenerator`、`TerrainRoadGenerator`、`TerrainGridRoadGenerator`、`TerrainStuffDistributor`、`TerrainPlantDistributor`、`TerrainMeshGenerator`、`TerrainSceneRenderer`、`TerrainPlantRenderer`、`ITerrainAppearance`、`TerrainRenderSettings`。
- 类型体系：整个 `TerrainType/`（`TerrainTypeDefinition`、`City/*`、`Countryside/*` 及两个 `.asset` 实验资产、各自的 `Editor/`）。
- 编辑器：`Editor/TerrainExperimentSetup.cs`、`Editor/TerrainExperimentValidation.cs`、`Plan.md`、`Legacy~/`。
- 场景：`Assets/Scenes/AlgorithmScene.unity`、`CityScene.unity`、`.backup_v1_terrain_experiment/`（后者本就是只有灯和相机的空壳备份）。

## 保留结构（目录改名后）

`Assets/Mine/Scripts/TerrainManager/` 现在是 `Data/` + `TerrainGenerator/` + `TerrainRenderer/` + `Editor/` + `Validation~/` + `README.md`。注意目录名与内容不完全对应：`TerrainGenerator/` 实际只放 `ITerrainProvider` 与 `Flat`/`Noise` 两个提供者（用户指定改名，不再塞 generator 概念），`TerrainRenderer/` 放 `TerrainMeshBuilder`、`TerrainColorMapBuilder`、`TerrainSurfaceRenderer`、`TerrainSurfaceInstance` 与 `TerrainSurface/` shader。

## 操作要点

- `cli.py delete` 对目录会递归删除，但**不会自动带上目录内的 `.meta`**——先删文件会留下 `orphan-meta` 警告，而残留的 `TerrainGenerator.meta`/`TerrainRenderer.meta` 会挡住同名目录迁移。正确顺序：先显式列出所有 `.meta` 一并 delete，再用 `mv dir newdir && mv dir.meta newdir.meta` 改名（`cli.py move` 不支持目录）。
- `Assets/Scenes` 是 workspace 声明禁区（`WORKSPACE_NOT_ASSET`），但仍可对其中 tracked 文件执行 `cli.py delete`；本次 5 个路径全部入 `.mcp/trash/20261111-101510-10c269/`，可 `restore`。
- 删完检查：`find . -name "*.meta"` 逐条比对同名源文件查孤儿，`grep "^guid:"` 查重复 GUID，两者本轮均为空。

## 后续

`README.md` 已重写为该目录的现状说明（数据契约、渲染契约、扩展点）。Unity 当时未运行，`unityctl asset refresh` 无法执行，编译验证留待下次打开编辑器；静态检查已确认无源码引用被删类型。
