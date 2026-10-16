# TerrainPlantDistributor

> 更新：2026-10-06；实现与本文位于 `TerrainManager/TerrainGenerator/`，方案配方位于 `TerrainManager/TerrainType/`。

`TerrainPlantDistributor` 只消费 `TerrainMapData`、已经生成的完整个体占地计划和 `TerrainPlantSettings`，输出确定性的 `TerrainPlantPlan`。它不创建场景对象，也不依赖特定草 Shader 或郊野内容 ID。`TerrainTypeDefinition.DistributePlants()` 决定是否调用它以及使用哪些规则；`TerrainManager` 在地图与 Stuff 计划生成之后请求 Plant 计划，避免先生成地被再让完整个体覆盖它。

共用 `TerrainPlantSettings` 默认规则为空，不再自带草/小麦配方。区域与植被类型均用非负整数 ID；ID 的含义和有效范围由所选方案校验，共用分布器不要求 ID 连续或使用郊野枚举。

## 配方

当前郊野配方位于 `CountrysideTerrainType` 资产 Inspector 的 `Dense Vegetation > Rules`，不在 Manager 的通用地图配置里。每项指定区域、地被种类、每世界单位平方的期望密度与占地半径。郊野预置矮草、高草、小麦和花，水域无预置规则；其它方案可以提供完全不同的 ID 和配方，或直接返回自己生成的 `TerrainPlantPlan`。

密度是期望值，不是固定数量，例如 0.5 表示平均每 2 平方单位尝试生成 1 株，实际数量会受道路、区域边缘和 Stuff 占地拒绝影响。`Max Plants` 是安全上限，超过会报错，不会悄悄截掉地图尾部。多个 Manager 可引用同一方案资产；修改配方会影响全部引用者，需要独立配置时复制资产。

每条规则使用与地图种子和规则序号相关的独立随机序列；对每格按面积换算候选数，再在格内抖动。候选足迹不能跨出目标区域、覆盖道路或进入房屋、风车和树木的占地。修改规则顺序会改变其随机序列；只调整某条规则的密度不会改变地形和道路。

`TerrainPlantPlacement` 保留类型 ID、来源区域 ID、世界位置、朝向、缩放和占地半径。高度按当前地表三角形插值采样，使靠近岸线的植被贴合网格。`TerrainPlantPlan` 提供空计划与接收放置记录集合的公开构造函数，便于新方案自行分布。后续的 `PlantManager`／草 Shader 可以直接消费这些记录，不需要另一份植被位置数据库。

## 当前实验表现

`TerrainRenderer/TerrainPlantRenderer` 把同一区块、同一类型 ID 的地被按最多 1023 株一批绘制为交叉面片，无逐株 GameObject；分组支持非连续 ID。面片尺寸和颜色通过 `ITerrainAppearance.PlantVisual()` 从当前方案取得，渲染器不再硬编码矮草、高草或小麦。材质沿用 URP Unlit，颜色仅用于辨别种类；真实造型、风摆和 LOD 均未实现。`TerrainSceneRenderer` 拥有场景表现及材质，统筹重复生成、禁用组件和退出 Play Mode 时的资源释放。

在郊野方案中，树与果树由 Stuff 配方输出，当前仍以单独的 Capsule 占位，后续可替换为完整预制体。`CountrysideTerrainType` 资产的 `Tree Count` 控制森林树目标数量；大于零时果树目标数量暂按其一半、至少 15 株生成，设为零则两类树都关闭。这是郊野配方约定，不是共用分布器规则。

`AlgorithmScene` 使用 `TerrainType/Countryside/AlgorithmCountryside.asset` 保存原场景地被规则，迁移不覆盖其已调参数。Manager 本身不持有郊野 Plant 配方。

## 已退役内容

旧 `Grass Count`、`Wheat Count` 与森林树/果树 Plant 自动迁移已清理；旧 `TerrainManager > Dense Vegetation` 配置位置及全局郊野 Plant 枚举已被方案资产与整数 ID 契约替代。郊野方案只定义矮草、高草、小麦和花这四种 Plant ID，不接受旧树类规则；其他方案可以自行定义 ID，不能把郊野 ID 范围误当作全系统限制。

## 验证

`Tools/Terrain/Validate Experiment Data` 检查郊野地被类型覆盖、固定种子可复现、区域与道路归属、全部完整个体避让，以及树木之间的占地互斥。通用方案测试另验证非郊野、稀疏整数 ID 可进入地图、计划、地表与渲染消费链路。本轮真实回归和 Play Mode 结果记录于 `../Plan.md`；视觉密度及面片观感仍需在 Game 视图人工确认。
