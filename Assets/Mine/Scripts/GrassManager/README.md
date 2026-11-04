# GrassManager 基础模板

`GrassManagerDemo.unity` 是独立演示场景。在编辑模式下即生成地形和草；在 Scene 视图移动或旋转 Main Camera 可以观察剔除，开启 Gizmos 可观察范围。进入 Play Mode 后用 WASD 或方向键移动相机。

| 组件参数 | 默认值 | 含义 |
|---|---:|---|
| ProceduralGrassTerrain / Chunk Size | 20 | 逻辑地块的边长，单位为世界单位；范围 0.1–10000 |
| GrassChunkManager / Density | 0.8 | 每平方米期望的草实例数，范围 0–10 |
| GrassChunkManager / Size | 1（演示场景为 4） | 统一尺寸倍率，范围 0–10；每株另有 0.8–1.2 的确定性随机倍率 |
| GrassChunkManager / Terrain Layers | Grass | 允许生成草的地形类型，可多选 |
| GrassChunkManager / Mesh Rotation | (0, 0, 0)（演示场景为 (-90, 0, 0)） | 草网格在贴合地形法线之前的本地旋转 |

`ProceduralGrassTerrain` 在内存生成高度顶点网格和格子类型数据，并以一块连续 Mesh 显示地表。地形固定为 100×100 世界单位，默认按边长 20 划分为 5×5 个逻辑地块；地块并非独立地表 Mesh。调整 Chunk Size 改变逻辑地块的数量并重建草，不改变地形范围或高度。例如边长为 30 时，地形分为 4×4 块，边缘地块不足 30 的部分按实际面积处理。地形高度、法线和类型均通过 `GrassTerrainSource` 查询；Demo 的所有格子均为 Grass，通用接口仍支持按地形类型筛选。

`GrassChunkManager` 在相机所在块及周围最多 3×3 个地块生成并缓存草实例，地图边缘按范围裁剪。可见实例需同时满足 Main Camera 视锥和距离条件：草实例的地面位置与相机在水平 XZ 平面的距离不超过一个地块边长。因此距离限制呈圆形，和视锥相交后的可见区域通常呈扇形；相机高度不计入距离。Scene 视图显示由 Main Camera 筛选出的同一批草。离开缓存范围的草块会释放，再次进入时根据区块坐标和固定种子重新生成。

实例的位置、贴合法线的旋转、法线和高度属性，以及 0.8–1.2 随机尺寸参考 `Shaders/Render/SimpleGrass/GrassGenerator.cs` 和 `GrassManager.cs`；复用其草模型、材质与间接绘制缓冲契约。风、交互和地表融合在此模板关闭。

## 手绘草高

选中场景中的 `Grass Manager Demo`，在 Grass Chunk Manager 组件的 **Grass Height Painter** 中启用 **Paint in Scene**，然后在 Scene 视图中左键点击或拖动。调整 Brush Radius 和 Brush Strength；默认笔刷降低草高，勾选 Raise Height 可重新增高。Alt 保留 Scene 视图导航，支持 Unity 撤销与重做。

未绘制的草高度为 1，绘制值限制在 0–1，0 时不绘制该实例。笔刷沿用原 `GrassGenerator` 的平滑衰减和累加方式，以地形水平距离计算影响范围；新系统将数值实际用于沿地形法线缩放草高。高度数据独立于草实例，随草管理器保存在场景中，需保存场景；改变密度、逻辑地块尺寸或重新加载区块后，草仍按地形相对位置读取手绘结果。手绘采用 256 × 256 数据采样网格，按需保存已绘制的小块；地形越大笔刷的空间精度越低。拾取需要地形组件子对象提供可读 Mesh，Demo 不需要额外添加 Collider。

本模板同步生成和释放区块，使用 CPU 逐株做距离和视锥剔除，尚无距离淡出、异步预算、GPU 剔除或原 TerrainManager 数据接入。地形高度、类型和草实例均为内存数据，不保存草实例数据库。后续可通过 `GrassTerrainSource` 接入其他地形：实现世界空间范围与采样，并在地形数据改变时递增版本；模板要求方形区块，每块候选草实例最多 4,000 株，超出时会限制数量并报告警告。地表组件目前只支持平移，不支持旋转和缩放。
