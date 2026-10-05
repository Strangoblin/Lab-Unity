# TerrainPlantDistributor

`TerrainPlantDistributor` 只消费 `TerrainMapData`、已经生成的完整个体占地计划和 `TerrainPlantSettings`，输出确定性的 `TerrainPlantPlan`。它不创建场景对象，也不依赖特定草 Shader。`TerrainManager` 按“地图 → 地表 → 房屋／风车／树木 → 密集地被”的顺序调用，避免先生成地被再让完整个体覆盖它。

## 配方

`TerrainManager > Dense Vegetation > Rules` 中每项指定区域、地被种类、每世界单位平方的期望密度与占地半径。当前预置矮草、高草、小麦和花；水域无预置规则。密度是期望值，不是固定数量，例如 0.5 表示平均每 2 平方单位生成 1 株。`Max Plants` 是安全上限，超过会报错，不会悄悄截掉地图尾部。

每条规则使用与地图种子和规则序号相关的独立随机序列；对每格按面积换算候选数，再在格内抖动。候选足迹不能跨出目标区域、覆盖道路或进入房屋、风车和树木的占地。修改规则顺序会改变其随机序列；只调整某条规则的密度不会改变地形和道路。

`TerrainPlantPlacement` 保留类型、来源区域、世界位置、朝向、缩放和占地半径。高度按当前地表三角形插值采样，使靠近岸线的植被贴合网格。后续的 `PlantManager`／草 Shader 可以直接消费这些记录，不需要另一份植被位置数据库。

## 当前实验表现

`TerrainPlantRenderer` 把同一区块、同一类型的地被按最多 1023 株一批绘制为交叉面片，无逐株 GameObject。材质沿用 URP Unlit，颜色仅用于辨别种类；小麦和草的真实造型、风摆和 LOD 均未实现。树与果树由 `TerrainStuffDistributor` 输出，目前仍以单独的 Capsule 占位，后续可替换为完整预制体。绘制资源在重复生成、禁用组件及退出 Play Mode 时释放。

地图设置里的 `Tree Count` 继续控制森林树目标数量；大于零时果树目标数量暂按其一半、至少 15 株生成，设为零则两类树都关闭。旧 `Grass Count`、`Wheat Count` 字段及场景序列化值已清理；地被数量只由 `Dense Vegetation > Rules` 的密度规则控制。

上一版已序列化的森林树／果树 `Plant Settings` 规则会在反序列化或校验时自动移除，避免旧场景因枚举值 4／5 失效或重复生成树木。其他无效规则仍会报错。

## 验证

`Tools/Terrain/Validate Experiment Data` 检查地被类型覆盖、固定种子可复现、区域与道路归属、全部完整个体避让，以及树木之间的占地互斥。视觉密度及面片观感仍需在 Game 视图人工确认。
