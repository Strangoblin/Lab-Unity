# 配件与配方目录

当前共 73 个配件、12 个配方。每类只有一个开放槽位，空配件是 UI 清空操作，不计入内容数量。

## 弹丸（10）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 电弧弹 | 紫色高速弹体，适合连锁效果。 | [定义](Samples/Definitions/ArcBolt.asset) |
| 冰晶球 | 蓝色慢速球体，适合追踪；减速由伤害槽配置。 | [定义](Samples/Definitions/FrostOrb.asset) |
| 重型弹 | 大弹体、高伤害，飞行较慢。 | [定义](Samples/Definitions/HeavyRound.asset) |
| 远射弹 | 高速、长寿命的远程载荷。 | [定义](Samples/Definitions/Longshot.asset) |
| 针弹 | 小半径、高速，适合精确追踪与穿透。 | [定义](Samples/Definitions/Needle.asset) |
| 碎石 | 廉价低伤害，适合密集分布和分裂。 | [定义](Samples/Definitions/Pebble.asset) |
| 巨型弹 | 伤害 48，半径 0.28，速度 18。 | [定义](Samples/Definitions/Slug.asset) |
| 火花弹 | 轻量橙色弹体，适合多发组合；燃烧由伤害槽配置。 | [定义](Samples/Definitions/Spark.asset) |
| 标准弹 | 均衡弹丸，伤害 12，速度 38。 | [定义](Samples/Definitions/StandardRound.asset) |
| 毒液球 | 绿色长寿命球体，适合延迟分裂。 | [定义](Samples/Definitions/ToxicOrb.asset) |

## 弹丸分布（10）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 立体散射 | 12 发，以固定螺旋采样填充 35° 锥形。 | [定义](Samples/Definitions/Cone.asset) |
| 十字分布 | 中心及水平、垂直四方向，共 5 发。 | [定义](Samples/Definitions/Cross.asset) |
| 环形十六发 | 水平 360°，16 个方向。 | [定义](Samples/Definitions/DenseRing.asset) |
| 五发扇形 | 5 发，24° 水平扇形。 | [定义](Samples/Definitions/Fan.asset) |
| 聚焦锥束 | 7 发，12° 锥形。 | [定义](Samples/Definitions/NarrowCone.asset) |
| 平行排射 | 5 个并排枪口，方向一致。 | [定义](Samples/Definitions/Parallel.asset) |
| 环形八发 | 水平 360°，均匀向八个方向射击。 | [定义](Samples/Definitions/Ring.asset) |
| 单发 | 沿瞄准方向发射一颗弹丸。 | [定义](Samples/Definitions/Single.asset) |
| 密集三发 | 3 发，8° 扇形，集中打击。 | [定义](Samples/Definitions/TightFan.asset) |
| 宽幅散射 | 9 发，70° 扇形覆盖。 | [定义](Samples/Definitions/WideFan.asset) |

## 飞行轨迹（11）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 加速 | 沿当前速度方向加速 18 m/s²。 | [定义](Samples/Definitions/Accelerating.asset) |
| 阻力 | 速度指数衰减，可与定时分裂组合。 | [定义](Samples/Definitions/Drag.asset) |
| 抛物线 | 受到向下 9.8 m/s² 加速度。 | [定义](Samples/Definitions/Gravity.asset) |
| 螺旋 | 沿飞行轴绕行，半径 0.45 m。 | [定义](Samples/Definitions/Helix.asset) |
| 追踪 | 在 25 m 内寻找目标，以有限转向速度追踪。 | [定义](Samples/Definitions/Homing.asset) |
| 轻飘轨迹 | 较小重力，延长平射距离。 | [定义](Samples/Definitions/LowGravity.asset) |
| 回旋 | 飞行 0.55 s 后转向本次发射位置。 | [定义](Samples/Definitions/Return.asset) |
| 螺旋追踪 | 同一配件叠加螺旋位移与目标追踪。 | [定义](Samples/Definitions/SeekingHelix.asset) |
| 强追踪 | 45 m 搜索范围，快速转向。 | [定义](Samples/Definitions/StrongHoming.asset) |
| 波浪 | 横向振幅 0.65 m，角频率 12。 | [定义](Samples/Definitions/Wave.asset) |
| 波浪抛物线 | 同时施加重力和横向波浪。 | [定义](Samples/Definitions/WaveGravity.asset) |

## 伤害属性（9）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 额外打击 | 命中后追加一次 50% 基础伤害。 | [定义](Samples/Definitions/BonusDamage.asset) |
| 燃烧 | 命中施加 4 秒燃烧，每秒伤害 6。 | [定义](Samples/Definitions/Burn.asset) |
| 火毒双属性 | 一个槽位同时施加燃烧和中毒。 | [定义](Samples/Definitions/FirePoison.asset) |
| 寒霜易伤 | 减速并标记，后续攻击增伤。 | [定义](Samples/Definitions/FrostMark.asset) |
| 易伤标记 | 后续伤害放大至 1.5 倍，持续 5 秒。 | [定义](Samples/Definitions/Mark.asset) |
| 中毒 | 命中施加 7 秒中毒，每秒伤害 4。 | [定义](Samples/Definitions/Poison.asset) |
| 震击 | 追加伤害并短暂眩晕。 | [定义](Samples/Definitions/ShockStun.asset) |
| 冰冻减速 | 目标移动速度变为 30%，持续 4 秒。 | [定义](Samples/Definitions/Slow.asset) |
| 眩晕 | 暂停靶标巡航 1.5 秒。 | [定义](Samples/Definitions/Stun.asset) |

## 碰撞响应（11）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 弹跳三次 | 碰撞后反射，最多延续 3 次。 | [定义](Samples/Definitions/Bounce.asset) |
| 连锁闪电 | 从命中点链向 6 m 内最多 3 个其他目标，各受 60% 伤害。 | [定义](Samples/Definitions/Chain.asset) |
| 连锁冲击 | 命中目标受击退，同时伤害附近目标。 | [定义](Samples/Definitions/ChainPush.asset) |
| 双弹跳组件 | 内部两个弹跳单元各提供 2 次延续，总计 4 次。 | [定义](Samples/Definitions/DoubleBounce.asset) |
| 双重爆炸 | 一次命中执行两次爆炸：近距强爆与远距弱爆。 | [定义](Samples/Definitions/DoubleExplosion.asset) |
| 爆炸 | 命中时产生 3 m 范围额外伤害；直接命中和爆炸可以叠加。 | [定义](Samples/Definitions/Explosion.asset) |
| 穿透爆破 | 穿透两次，每次接触产生范围爆炸。 | [定义](Samples/Definitions/ExplosivePierce.asset) |
| 穿透三次 | 穿过 3 个碰撞体，同一碰撞体不重复结算。 | [定义](Samples/Definitions/Pierce.asset) |
| 牵引 | 向反方向施加冲量，将目标拉向射击者。 | [定义](Samples/Definitions/Pull.asset) |
| 击退 | 沿飞行方向施加冲量，靶场可观察位移。 | [定义](Samples/Definitions/Push.asset) |
| 广域爆炸 | 5 m 范围，额外伤害为基础伤害的 45%。 | [定义](Samples/Definitions/WideExplosion.asset) |

## 触发载荷（8）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 命中双分裂 | 一次接触执行两组分裂，每组三颗，角度不同。 | [定义](Samples/Definitions/DoubleHitSplit.asset) |
| 双分裂 | 一个配件在 0.20 s 和 0.40 s 各触发一次三发分裂。 | [定义](Samples/Definitions/DoubleSplit.asset) |
| 终寿分裂 | 寿命耗尽时生成五颗碎片；提前碰撞终止不触发。 | [定义](Samples/Definitions/ExpirySplit.asset) |
| 命中分裂 | 第一次接触时生成 5 颗碎片。 | [定义](Samples/Definitions/HitSplit.asset) |
| 延迟散花 | 0.7 s 后生成八颗宽角碎片。 | [定义](Samples/Definitions/LateSplit.asset) |
| 时间命中双触发 | 先定时生成两颗；后续命中再生成三颗。 | [定义](Samples/Definitions/TimedHitSplit.asset) |
| 定时分裂 | 0.25 s 后生成 3 颗碎片；子弹继承效果，但不再次分裂。 | [定义](Samples/Definitions/TimedSplit.asset) |
| 三段分裂 | 0.15 / 0.30 / 0.45 s 各生成两颗碎片。 | [定义](Samples/Definitions/TripleSplit.asset) |

## 属性修饰（8）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 大型化 | 半径 ×2。 | [定义](Samples/Definitions/BigRound.asset) |
| 增伤 | 基础伤害 ×1.8。 | [定义](Samples/Definitions/DamageBoost.asset) |
| 双增伤 | 内部两次 ×1.4 增伤，合计 ×1.96。 | [定义](Samples/Definitions/DoublePower.asset) |
| 节能 | 整次费用 ×0.6。 | [定义](Samples/Definitions/Economy.asset) |
| 重装强化 | 伤害 ×2、半径 ×1.5，速度 ×0.7。 | [定义](Samples/Definitions/HeavyPower.asset) |
| 长寿命 | 寿命 ×2。 | [定义](Samples/Definitions/LongLife.asset) |
| 针刺调校 | 速度 ×2、半径 ×0.5、伤害 ×0.8。 | [定义](Samples/Definitions/NeedleTune.asset) |
| 高速 | 弹速 ×1.7。 | [定义](Samples/Definitions/SpeedBoost.asset) |

## 射击控制（6）

| 配件 | 行为 | 资源 |
| --- | --- | --- |
| 三连发 | 同一次开火以 0.12 s 间隔提交三组弹丸。 | [定义](Samples/Definitions/Burst.asset) |
| 双连发组件 | 两个双击单元合成四组弹丸，间隔 0.1 s。 | [定义](Samples/Definitions/DoubleBurst.asset) |
| 双击 | 两组弹丸，间隔 0.08 s。 | [定义](Samples/Definitions/DoubleTap.asset) |
| 五连发 | 五组弹丸，间隔 0.1 s。 | [定义](Samples/Definitions/FiveBurst.asset) |
| 快射 | 冷却时间 ×0.5。 | [定义](Samples/Definitions/Rapid.asset) |
| 稳射 | 冷却时间 ×1.6，便于逐发观察。 | [定义](Samples/Definitions/SlowFire.asset) |

## 配方

| 配方 | 内容 | 资源 |
| --- | --- | --- |
| 自定义 / 空配方 | 八类全开，所有槽位为空，从零组合。 | [定义](Samples/Definitions/Recipe_Custom.asset) |
| 双分裂试验 | 标准弹 + 双分裂；观察分裂事件计数。 | [定义](Samples/Definitions/Recipe_DoubleSplit.asset) |
| 连锁冰电 | 追踪 + 连锁 + 减速。 | [定义](Samples/Definitions/Recipe_Electric.asset) |
| 追踪火雨 | 标准弹 + 立体散射 + 追踪 + 燃烧。 | [定义](Samples/Definitions/Recipe_Firestorm.asset) |
| 分裂爆弹 | 重型弹 + 命中分裂 + 爆炸。 | [定义](Samples/Definitions/Recipe_Fragment.asset) |
| 抛物线榴弹 | 重型弹 + 重力 + 爆炸。用方向键稍抬高枪口。 | [定义](Samples/Definitions/Recipe_Grenadier.asset) |
| 穿透标记 | 穿透弹 + 易伤标记 + 三连发。 | [定义](Samples/Definitions/Recipe_Piercing.asset) |
| 精确单发 | 标准弹；其余槽位为空。 | [定义](Samples/Definitions/Recipe_Precision.asset) |
| 环形弹跳 | 环形八发，碰撞后反射。 | [定义](Samples/Definitions/Recipe_Ricochet.asset) |
| 散弹 | 五发扇形 + 标准弹。 | [定义](Samples/Definitions/Recipe_Scatter.asset) |
| 螺旋毒弹 | 重型弹 + 螺旋 + 中毒 + 长寿命。 | [定义](Samples/Definitions/Recipe_Spiral.asset) |
| 定时分裂 | 标准弹 + 一次定时分裂。 | [定义](Samples/Definitions/Recipe_Timed.asset) |
