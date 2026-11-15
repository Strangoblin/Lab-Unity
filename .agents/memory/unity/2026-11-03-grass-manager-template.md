# 独立 GrassManager 基础模板（2026-11-03）

`Assets/Mine/Scripts/GrassManager/` 提供独立演示场景与运行时代码。`GrassTerrainSource` 定义世界空间地表采样和版本契约；演示地形生成 5×5 块、每块 20 单位，草管理器维护相机周围最多 3×3 块，按密度与固定种子生成位置、法线、旋转和尺寸。草按块复用 SimpleGrass 的模型、材质及间接绘制缓冲契约；默认无风、交互或地表混合。每块候选上限 4,000；地形本体当前仅支持平移。

Unity 6000.3.14f1 批处理编译通过；Editor 验证覆盖地表 25 块、中心 9 块、移动替换、边缘和出界、确定性重建、贴地采样、零密度和显式生命周期清理。Metal 640×360 有草/无草对照变化 35,122 像素。首轮 Bee 因继承环境的非法字符失败，使用精简 UTF-8 环境启动后成功。以上为 Editor 调用与相机渲染，Play 模式和键盘操作未验证。

后续按用户要求增加编辑模式预览：地形和草在 Scene 视图默认生成，按 Main Camera 六平面在 CPU 上逐株剔除包围盒；Main Camera 与 Scene 视图显示同一批可见实例，黄色 Gizmo 显示视锥。隔离预览场景实测前向 658、旋转后 592，覆盖 GPU 索引、区块迁移和零密度；隔离相机有草/无草图像变化 31,915 / 230,400 像素，真实 SceneView 截图可见草和剔除边界。预览场景测试确认生成和销毁临时地形不使场景变脏。

边界：现阶段同步生成，无预取、距离淡化、GPU 剔除、手绘和 TerrainManager 适配；演示场景尺寸设为 4，以便观察原始 0.2 单位草模型。

2026-11-04：演示地表改为单块 100×100 Mesh，使用固定 41×41 高度点和 40×40 类型网格；地块边长仅控制逻辑分区与草的水平剔除半径，不改变地表大小，边缘块按实际面积生成。地形采样接口提供 Grass/Dirt/Water 类型与实际范围，管理器可选择类型；超过每块 4,000 个候选时限制数量并警告。Unity 6000.3.14f1 独立批处理隔离验证通过：20→30→200 地块边长、不完整边缘块和密度、地表稳定、相机视锥及水平半径交集、类型筛选、相机移动和零密度；渲染前后变化 36,378 像素。地形类型只是演示数据，地表材质不显示类型差异；人工 Scene 观感仍需观察。

另在独立批处理载入已保存 Demo，确认默认 20、Grass 层和 Main Camera 绑定，生成 1,897 株、筛选可见 204 株；首次运行验证通过后退出时 Burst 编译线程崩溃，使用 `--burst-disable-compilation` 重跑验证通过且正常退出。

2026-11-04 后续修正：Demo 地形类型材质相同但原先仅 Grass 层生成导致大片空白，场景现默认选择全部三种类型，管理器仍允许筛选。导入草模型沿本地 +Z 生长，Demo 的网格基础旋转为 X=-90°，然后对齐地形法线；不同模型可通过 Mesh Rotation 配置。运行中的 Editor 刷新后验证 RPC 超时，未切场景或重启；在独立项目副本中 Unity 批处理验证通过地形类型覆盖、筛选、旋转更新与采样法线对齐及原剔除逻辑，实际保存的 Demo 生成 2,880 株、视锥内可见 235 株。人工 Scene 观感尚待确认。

同日用户进一步要求 Demo 地形统一草地：演示地形全部数据格为 Grass，场景默认选择 Grass；原有地形类型接口保留供其他数据源使用。独立项目批处理遍历全部 40×40 格并验证分布、筛选、朝向、剔除和 Demo 场景，场景生成 2,880 株、可见 235 株。前段的多类别示例和默认全选只记录中间状态。

同日增加 Scene 草高笔刷：沿用 GrassGenerator 的笔刷强度累加和平滑衰减，保存独立于实例的地形相对高度数据；默认 1，可擦低或增高，0 不绘制，并按地形法线实际缩放草网格。原 GrassGenerator 的高度只影响 Compute 中的零值剔除，Shader 不做连续高度缩放。笔刷拾取可读地表 Mesh，无需 Collider；数据随场景保存，支持 Undo/Redo。256×256 高度点按需分配 16×16 小块；首版全量数组 Redo 单样本 46,098 ms，改为稀疏小块后同类单样本 60 ms，非性能基准。独立项目批处理通过笔刷与跨块采样、拾取、草高矩阵、原剔除、Undo/Redo、密度和分区改变及场景保存重载；实际 Scene 鼠标操作和长笔画性能仍待人工验证。

2026-11-04 风场接入：Demo 场景新增 `FieldManager` + `Wind/WindFieldProvider`（跟随 Main Camera、64×64、高度增益 1/1）；局部 `WindSource` 曾加入，后于同日按用户要求整体移除。新渲染资源 `GrassManager/Grass/GrassWind.shader` + `GrassWind.mat` 取代旧 `GrassInstance` 材质，保留原颜色、光照、旧噪声风、交互贴图与地表混合逻辑，并在顶点阶段用 `SampleWindVelocityWS` 叠加世界空间风场位移（当时 `_WindResponse` 0.12 s、`_WindMaxOffset` 0.35 m，后者已于同日按用户要求移除；根部固定，手绘高度同步缩放）。风场缺失时 `_WindFieldValid=0`，自动回到无风。Shader 编译无错误（仅剩既有 include 的 pragma/截断警告）；场景加载生成 2,880 株、可见 284 株；Edit 模式风场开/关 640×360 图像差异 48,637 像素（21%），Play Mode 渲染正常；隔离验证通过，已保存场景的独立批处理验证未重跑。证据见 [evidence/2026-11-04-grass-wind](evidence/2026-11-04-grass-wind/)。

2026-11-04 资源迁移与“风过快”结论：草模型 `Normalized Grass.fbx` 与贴图 `Grass.png`（连同 `.meta`）从 `Shaders/Render/SimpleGrass/Material` 迁至 `GrassManager/Grass/`，GUID 不变，场景、材质和 `_Recovery` 场景的引用自动保持；`GrassManagerDemoSetup` 与 `GrassManagerDemoValidation` 的三处硬编码路径同步更新。迁移后 Unity 重编译无错误，Demo 仍生成 9 块、材质 `_Grass` 解析到新路径，640×360 相机渲染可见草地。用户反馈“风场过快”在重启 Unity 后恢复正常，判断为改造 shader 常量后编辑器内已实例化的运行时材质/变体未刷新；未修改 FieldManager 源码或工程设置，重启未改动它们。

2026-11-04 用户定调“shader 只接收效果，不进行效果钳制”：`GrassWind.shader` 移除 `_WindMaxOffset` 属性、CBUFFER 字段和 `min(1, maxOffset/magnitude)` 幅度限幅，位移直接为 `风速 × _WindResponse × 叶片权重 × 高度权重`；`GrassWind.mat` 同步删除该属性，README 改为说明“需要限幅时调 `_WindResponse` 或上游风速，并让最大位移留在区块包围盒余量（当前约 0.6 m）内”。这与 2026-09-18 参数入口钳制策略一致：端点/范围保护放在参数入口，不在 shader 计算点重复 clamp。验证：Shader 支持、仅剩既有 include 警告，材质 `HasProperty("_WindMaxOffset")=false`，640×360 渲染正常。

2026-11-04 风场速度不稳定的真因（用户复现：Edit 模式忽快忽慢，进入 Play 后正常）：`WindField.compute` 用 `position - _BaseVelocity.xz * _FieldTime` 计算扰动相位，而 `_BaseVelocity` 含 `sin(time*0.7)` 阵风，等于把“瞬时速度 × 已运行时间”当位移；相位速度变成 `B(t) + t·B'(t)`，随时间线性增长。`FieldManager.Clock` 在 Edit 模式取 `Time.realtimeSinceStartup`（编辑器已开越久越大），Play 模式取 `Time.time`（每次从 0 开始），所以 Edit 模式越开越乱、进 Play 就“正常”，长时间 Play 同样会逐渐加速。修复：`WindFieldProvider` 用解析积分 `∫B dt = base·t + dir·gust·(1-cos(0.7t))/0.7` 计算 `_AdvectionOffset`，按 `_TurbulenceLength` 取模（扰动本身以该长度周期重复，取模数学等价且保持浮点精度），Compute 改用 `position - _AdvectionOffset`；阵风相位与积分都用 double 计算并取模。实测漂移速度在 t=110/3600/36000 s 时分别为 2.48/2.37/1.53 m/s（上界 base+gust=2.52），旧公式在 t=36000 s 时可达约 1.26×10⁴ m/s。重编译通过，`_WindFieldValid=1`、128×128 纹理正常，无 Compute 错误。
