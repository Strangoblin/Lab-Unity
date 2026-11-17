# GrassManager 基础模板

`GrassManagerDemo.unity` 是独立演示场景。在编辑模式下即生成地形和草；在 Scene 视图移动或旋转 Main Camera 可以观察剔除，开启 Gizmos 可观察范围。进入 Play Mode 后用 WASD 或方向键移动相机。

| 组件参数 | 默认值 | 含义 |
|---|---:|---|
| TerrainGrassSource / Chunk Size | 20 | 逻辑地块的边长，单位为世界单位；范围 0.1–10000 |
| GrassChunkManager / Density | 0.8（演示场景为 10） | 每平方米期望的草实例数，范围 0–10 |
| GrassChunkManager / Size | 1（演示场景为 4） | 统一尺寸倍率，范围 0–10；每株另有 0.8–1.2 的确定性随机倍率 |
| GrassChunkManager / Terrain Layers | Grass | 允许生成草的地形类型，可多选 |
| GrassChunkManager / Mesh Rotation | (0, 0, 0)（演示场景为 (-90, 0, 0)） | 草网格在贴合地形法线之前的本地旋转 |

地形由 `TerrainManager` 提供，`TerrainGrassSource` 把它的数据层适配成 `GrassTerrainSource`：位置、法线与地表层全部来自 `TerrainGrid`，本组件不持有地形数据。演示场景的地形是 100×100 格、1 m 一格（世界范围 100×100，原点 (-50, 0, -50)），使用 `Noise` 提供者、高度倍率 2，按高度与坡度自动分配 Grass / Sand / Snow / Rock 地表层。

逻辑地块的边长属于草侧划分，记在 `TerrainGrassSource` 上：默认按边长 20 划分为 5×5 个逻辑地块，地块并非独立地表 Mesh。调整 Chunk Size 只重建草，不改变地形范围或高度；例如边长 30 时分为 4×4 块，边缘地块不足 30 的部分按实际面积处理。Demo 只勾选 Grass，所以沙地与雪顶保持裸露，用来演示地表层筛选。

旧实现 `ProceduralGrassTerrain`（内存生成一块 100×100 地表）仍然保留在 `Runtime/`，作为另一份 `GrassTerrainSource` 实现，供隔离验证与对照使用，不再出现在演示场景里。

`GrassChunkManager` 在相机所在块及周围最多 3×3 个地块生成并缓存草实例，地图边缘按范围裁剪。可见实例需同时满足 Main Camera 视锥和距离条件：草实例的地面位置与相机在水平 XZ 平面的距离不超过一个地块边长。因此距离限制呈圆形，和视锥相交后的可见区域通常呈扇形；相机高度不计入距离。Scene 视图显示由 Main Camera 筛选出的同一批草。离开缓存范围的草块会释放，再次进入时根据区块坐标和固定种子重新生成。

实例的位置、贴合法线的旋转、法线和高度属性，以及 0.8–1.2 随机尺寸沿用旧 SimpleGrass 原型的实例契约（该原型已于 2026-11-06 整体退役并删除，实现见本目录 `Runtime/`）；草由本目录 `Grass/GrassWind.mat` 与 `Grass/GrassWind.shader` 渲染。旧原型中的草模型 `Normalized Grass.fbx` 与贴图 `Grass.png` 已迁入本目录，GUID 未变。

## 地表颜色

草的 basecolor 直接采样地形颜色图，不再做基础色 / 风色 / 地面色的多色混合：`GrassTerrainSource.TryGetGroundColor` 把 `TerrainManager.SurfaceColorMap` 连同它的世界 XZ 原点与尺寸交给区块，shader 用 `(worldXZ - origin) / size` 采样——和地形网格的 UV 完全同一套映射，所以草的颜色天然跟着脚下的地表层走。

| 颜色公式 | 含义 |
|---|---|
| `basecolor = 地表色 * (height + 1) * 草纹理` | `height` 是**运动后**的高度：位移（风场 + 踩踏 + 旧风偏移）结束后的顶点相对实例根部、沿叶片朝向的投影，单位米；静止时根部 0、叶尖等于实际叶片长度 |

因为用的是运动后的高度，草被吹弯或踩下去时颜色会同步变暗，回弹后恢复。`height` 在顶点着色器里算好传给片元，不在片元里重算位移。

采样用实例根部位置而不是位移后的顶点，否则风偏移和踩踏偏移会让叶尖跨格串色。地形色图缺失时（例如 `ProceduralGrassTerrain` 作为数据源）退化为纯白，草只受纹理与高度影响。地形每次重建都会生成新色图并递增 `DataVersion`，草随之重建，不会挂在旧图上。

## 渲染管线接入

草不是场景里的 Renderer，进不了 `CullResults`，所以绘制由 `GrassRenderFeature`（`Runtime/GrassRenderFeature.cs`）在管线内直接下发，而不是 `Graphics.DrawMeshInstancedIndirect`——后者只在 `RenderSingleCamera` 末尾的 `context.Submit()` 落地，跑在整条 pass 序列之外，既进不了 prepass 也不进后处理。

| 时机 | 做什么 |
|---|---|
| `AfterRenderingPrePasses` | 用 `DepthNormals` / `DepthOnly` Pass 把草画进 `_CameraDepthTexture` 与 `_CameraNormalsTexture`；接在 URP 自己的 prepass 之后，避免被它的 Clear 抹掉 |
| `AfterRenderingOpaques` | 用 `Forward` Pass 写相机颜色，早于天空盒、SSGI 与后处理 |

`GrassChunkManager` 每帧在 `beginContextRendering` 里重建 `GrassDrawRequests`（mesh / material / argsBuffer / MPB / layer），feature 按相机 `cullingMask` 过滤后逐条 `cmd.DrawMeshInstancedIndirect`。`_CameraNormalsTexture` 只有在有 pass 声明 `ConfigureInput(Normal)`（例如 SSGI）时才存在，缺失时自动降级成 `DepthOnly`。

`PC_Renderer.asset` 上必须挂 `GrassRenderFeature`，否则草完全不显示；换到别的 renderer 也要一并添加。

## 阴影

**草只接收阴影，不投射阴影。**

接收走 `GrassWind.shader` 的 `MainLightShadow`，与地形同一套语义（含 `BEYOND_SHADOW_FAR` 的距离淡出），不做额外的距离混合：视野内的草一律按阴影图取值。

投射在 2026-11-12 放弃。此前的 `GrassShadowPass` 在 `AfterRenderingShadows` 用 `DrawMeshInstancedIndirect` 往 URP 主光阴影图里追加草几何（切片用 `ShadowUtils.ExtractDirectionalLightMatrix` 按 URP 内部同样参数复算），但它始终无法与接收端对齐：Game 视图会稳定出现一大片固定在相机视角上的错位阴影，调整级联数、阴影距离、`_ShadowCasterOffset`、接收淡出都只改变它的形状和浓度，不改变错位本身。因此删除了该 pass；`GrassWind.shader` 里保留 `ShadowCaster` Pass 与 `_ShadowAlphaClipOffset` / `_ShadowCasterOffset` 两个参数，但没有任何绘制路径会用到它们。

后果：草不会在地面或彼此之间投下阴影。需要草参与投影时，正确做法不是再往 URP 的阴影图里追加绘制，而是把草接成正常的 Renderer 交给 URP 剔除（代价是放弃当前这套间接绘制管线）。

## 风场

Demo 场景中的 `Field Manager` 包含 `FieldManager` 和 `Wind / WindFieldProvider`，向全局发布跟随 Main Camera 的 64×64 风场纹理（RGB = 世界空间风速，m/s）。`GrassWind.shader` 通过 `Assets/Mine/Scripts/FieldManager/Wind/WindField.hlsl` 的 `SampleWindVelocityWS` 采样风场：

| 材质参数 | 默认值 | 含义 |
|---|---:|---|
| `_WindResponse` | 0.12 | 风速到位移的响应时间（秒）；位移 = 风速 × 响应 × 叶片高度权重 |

叶片根部（`uv.y = 0`）固定，叶尖位移最大；手绘草高会同步缩放风力位移。风场现在还会输出竖直分量（由水平散度导出，见 FieldManager README），`FieldWindOffsetWS` 直接把它计入世界空间偏移，所以阵风锋过境时草叶会被抬起、锋后回落；竖直位移使用同一个 `_WindResponse` 和叶片高度权重。Shader 只应用风场给出的效果，不对位移做幅度钳制；需要限制摆幅时应调整 `_WindResponse` 或上游风速，并让最大位移保持在区块包围盒的余量内（当前 mesh 缩放下约 0.6 m），否则边缘剔除可能提前。风场缺席或被禁用时全局 `_WindFieldValid = 0`，草回到无风状态，不会残留纹理。旧版噪声风（`_WindNoiseTexture` / `_WindStrength`）和交互贴图（`_InteractionTexture`）的代码路径仍然保留，但区块管理器以零强度绑定；实际可见的风来自 FieldManager。


## 手绘草高

选中场景中的 `Grass Manager Demo`，在 Grass Chunk Manager 组件的 **Grass Height Painter** 中启用 **Paint in Scene**，然后在 Scene 视图中左键点击或拖动。调整 Brush Radius 和 Brush Strength；默认笔刷降低草高，勾选 Raise Height 可重新增高。Alt 保留 Scene 视图导航，支持 Unity 撤销与重做。

未绘制的草高度为 1，绘制值限制在 0–1，0 时不绘制该实例。笔刷沿用原 `GrassGenerator` 的平滑衰减和累加方式，以地形水平距离计算影响范围；新系统将数值实际用于沿地形法线缩放草高。高度数据独立于草实例，随草管理器保存在场景中，需保存场景；改变密度、逻辑地块尺寸或重新加载区块后，草仍按地形相对位置读取手绘结果。手绘采用 256 × 256 数据采样网格，按需保存已绘制的小块；地形越大笔刷的空间精度越低。拾取需要地形组件子对象提供可读 Mesh，Demo 不需要额外添加 Collider。

本模板同步生成和释放区块，使用 CPU 逐株做距离和视锥剔除，尚无距离淡出、异步预算或 GPU 剔除。地形数据由 `TerrainManager` 持有，草实例仍是内存数据，不保存草实例数据库。后续可通过 `GrassTerrainSource` 接入其他地形：实现世界空间范围与采样，并在地形数据改变时递增版本（`TerrainGrassSource` 就是这样把 `TerrainManager` 的地形版本与地块边长合并成一个版本号的）；模板要求方形区块，每块候选草实例最多 4,000 株，超出时会限制数量并报告警告。地表组件目前只支持平移，不支持旋转和缩放。
