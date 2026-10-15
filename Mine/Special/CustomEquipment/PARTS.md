# 装备核心与配方目录

当前选择目录共 16 个核心配件、10 个配方。四类核心各占一个开放槽位；空配件清除该核心的整组效果。

四个核心的主属性分别对应生命、护盾、体力、能量上限，其他效果单独保存为固定副词条。旧八槽和旧四核心资产保留用于兼容；迁移核心不计入选择目录。兼容配方保留旧属性，人物基础能量为 100、回复 10/s、消耗倍率 1。替换核心会替换对应整组加成；随机词条尚未实现。

## 生命核心（4）

| 配件 | 效果 | 资源 |
| --- | --- | --- |
| 重甲生命核心 | 主属性：生命 +50。副词条：护甲 16、伤害 ×0.9、受击保护 0.3 秒。 | [定义](Samples/Definitions/Resource_ArmoredHealth.asset) |
| 均衡生命核心 | 主属性：生命 +40。副词条：护甲 6、伤害 ×0.85、五类抗性 20%、脱战回复 3/s。 | [定义](Samples/Definitions/Resource_BalancedHealth.asset) |
| 隔离生命核心 | 主属性：生命 +20。副词条：护甲 4、燃烧/中毒抗性 65%、减速/眩晕 40%、易伤 50%。 | [定义](Samples/Definitions/Resource_ResistantHealth.asset) |
| 急救生命核心 | 主属性：生命上限 ×2。副词条：低生命治疗 20%、冷却 12 秒；不能复活。 | [定义](Samples/Definitions/Resource_VitalHealth.asset) |

## 护盾核心（4）

| 配件 | 效果 | 资源 |
| --- | --- | --- |
| 重型护盾核心 | 主属性：护盾 +120。副词条：回复 10/s、受伤后等待 3 秒。 | [定义](Samples/Definitions/Resource_HeavyShield.asset) |
| 快充护盾核心 | 主属性：护盾 +40。副词条：回复 20/s、受伤后等待 1 秒。 | [定义](Samples/Definitions/Resource_QuickShield.asset) |
| 储备护盾核心 | 主属性：护盾 +200。无回复副词条，依靠显式重置补充。 | [定义](Samples/Definitions/Resource_ReserveShield.asset) |
| 常规护盾核心 | 主属性：护盾 +50。副词条：回复 8/s、受伤后等待 3 秒。 | [定义](Samples/Definitions/Resource_ShieldCell.asset) |

## 体力核心（4）

| 配件 | 效果 | 资源 |
| --- | --- | --- |
| 均衡体力核心 | 主属性：体力 +40。副词条：步行 ×1.2、击退抗性 40%、回复 +8/s、冲刺消耗 ×0.75。 | [定义](Samples/Definitions/Resource_BalancedStamina.asset) |
| 耐力体力核心 | 主属性：体力 +60。副词条：步行 ×1.1、回复 +15/s、冲刺消耗 ×0.5。 | [定义](Samples/Definitions/Resource_EnduranceStamina.asset) |
| 疾跑体力核心 | 主属性：体力 +20。副词条：步行 ×1.5、冲刺倍率 ×1.2、冲刺消耗 ×1.2。 | [定义](Samples/Definitions/Resource_SprintStamina.asset) |
| 稳定体力核心 | 主属性：体力 +30。副词条：击退抗性 80%、减速/眩晕抗性 40%。 | [定义](Samples/Definitions/Resource_StableStamina.asset) |

## 能量核心（4）

| 配件 | 效果 | 资源 |
| --- | --- | --- |
| 均衡能量核心 | 主属性：能量 +40。副词条：能量回复 +4/s。 | [定义](Samples/Definitions/Resource_BalancedEnergy.asset) |
| 节能能量核心 | 主属性：能量 +30。副词条：能量消耗 ×0.65、每 5 秒净化持续状态。 | [定义](Samples/Definitions/Resource_EfficientEnergy.asset) |
| 快充能量核心 | 主属性：能量 +20。副词条：能量回复 +20/s。 | [定义](Samples/Definitions/Resource_QuickEnergy.asset) |
| 储备能量核心 | 主属性：能量 +150。副词条：能量消耗 ×1.15，容量与消耗取舍。 | [定义](Samples/Definitions/Resource_ReserveEnergy.asset) |

## 配方

| 配方 | 内容 | 资源 |
| --- | --- | --- |
| 纯护甲对照 | 只装备重型护甲，验证平减与持续伤害区别。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_ArmorOnly.asset) |
| 均衡防护 | 生命、减伤、护盾、抗性、回复与机动兼顾。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Balanced.asset) |
| 复合配件 | 内部多操作配件：双生、双缓冲、双电芯、双维修、双驱。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Composite.asset) |
| 自定义 / 空配方 | 标准本体，无配件；四个核心槽位全部开放。 | [定义](Samples/Definitions/Recipe_Custom.asset) |
| 移动堡垒 | 高生命、重甲与大护盾，适合强受击测试。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Fortress.asset) |
| 火毒隔离 | 火毒双抗性与周期净化，便于对比持续伤害。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Hazmat.asset) |
| 纯生命对照 | 只装备生命核心，比较原始直接伤害。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_HealthOnly.asset) |
| 应急恢复 | 低生命触发治疗，观察冷却与不能复活的边界。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Rescue.asset) |
| 疾行者 | 观察移动、冲刺体力、减速抗性与击退。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Runner.asset) |
| 护盾循环 | 短护盾等待与高回复，适合连续攻击。<br>四核心兼容配方：保留原装配参数；替换或清空核心会替换整组加成。<br>资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。 | [定义](Samples/Definitions/Recipe_Shield.asset) |
