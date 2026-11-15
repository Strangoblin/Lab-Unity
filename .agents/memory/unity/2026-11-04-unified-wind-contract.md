# 统一风场契约（2026-11-04）

用户定调：`FieldManager` 只提供统一风场，不再为单个消费者保留特殊局部契约。

- 风场契约只有一条：发布世界空间速度（m/s），不发布位移、不做幅度限制。
- 速度如何转成位移/加速度/阻力、响应系数和幅度上限，全部属于消费者自己的材质参数。
- Shader 直接应用采样到的效果，不在共享风场集成里 clamp；需要限制时由消费者参数入口处理。
- 据此删除 README 中 Rain/Plant 的专属契约段，并移除 `Plant.shader` 的 mesh 预算限幅（`remainingBudget`、`maxOffset = min(0.25, …)`、`min(1, maxOffset / length)`），`PlantWindOffsetWS` 现在只做 `velocity × _WindResponse × 高度权重 × PlantScale`。
- `GrassWind.shader` 已先一步移除 `_WindMaxOffset`，两者现在遵循同一契约。
- 用户随后要求“去掉局部风源”：删除 `Wind/WindSource.cs`（走 `.mcp` 垃圾桶留底），`WindField.compute` 去掉 16 源数组与叠加循环，`WindFieldProvider` 去掉 `UploadSources` 与源上传，Demo 场景与 Setup 去掉 `Wind Source` 物体；`GizmoRender.md` / `GizmoRenderChecks` 同步移除该类型。场景现为 `Field Manager → Wind` 两级。

这与 2026-09-18 的参数入口钳制策略一致：端点/范围保护放参数入口，不在计算点重复 clamp。风场现在只由基础风、阵风、空间湍流和解析高度增益构成。

2026-11-04 后续：用户确认 base wind 双层作用并要求拆分力/速。旧实现里 `_baseWind` 同时进入 `_BaseVelocity`（消费者位移）和 `_AdvectionOffset`（图案推进）；`_gustStrength` 同样同时进入两者（解析积分项）；湍流幅度不进 advection，但其时间频率 = 载流速度 / `_turbulenceLength`。新参数为：`_windAngle`（度，0=+X，90=+Z）、`_windStrength`（m/s，力→位移）、`_windSpeed`（m/s，图案推进）、`_gustStrength`（m/s）、`_gustFrequency`（Hz，默认 0.11 ≈ 旧 0.7 rad/s）、`_turbulenceStrength`（m/s）、`_turbulenceLength`（m）。advection 只由 `_windSpeed` 解析积分并按长度取模，strength 与 gust 不再影响推进；阵风频率由硬编码常量改为参数。场景保留旧观感：angle=45、strength=speed=0.4243、gust 0.5/0.1114、turbulence 0.6/20；prefab angle=8.53、strength=speed=2.0224。验证：`ComputeAdvection(1s)` 每轴 0.3（0.4243×0.7071），`GustAngle(1s)=0.7 rad`，当前时刻 `_WindFieldBaseVelocity` 的 x=z 证实 45°，128² 纹理 `_WindFieldValid=1`，640×360 非天空像素 168147。`PrefabUtility.LoadPrefabContents` 会激活预览中的 FieldManager/WindFieldProvider 并触发重复 producer 禁用，随后 `SaveAsPrefabAsset` 把 `m_Enabled: 0` 写回 prefab；本次已手工改回 1，后续改 prefab 应避免加载预览或显式恢复 enabled。

2026-11-04 阵风改为行波（用户选 A）：用户指出「持续整体方向 + 持续整体振荡」叠加后仍是整体振荡，前两层至少改一层才有波动；第三层湍流不动。联网调研（Unity terrain 多层行波、Cyanilux/Roystan/GlobalWind 的噪声滚动、Book of the Dead 的 base+gust+flutter）后确认阵风缺的是空间相位。实现：`_gustFrequency` 替换为 `_gustSpeed`（m/s）+ `_gustLength`（m），compute 中 `gustPhase = 2π(dot(_GustDirection, positionXZ) − _GustOffset) / _GustLength`，`gust = sin(gustPhase)·_GustStrength` 沿风向叠加；固定点看到的频率 = GustSpeed / GustLength。推进量 `_GustOffset` 在 CPU 用 double 积分并按波长取模（`ComputeGustOffset`），phase 不随时间线性膨胀——与湍流 advection 的做法一致，避免长会话掉精度。`WindFieldProvider.SimulateField` 只发布定常 `_BaseVelocity`（`direction·_windStrength`），并传 `_GustDirection/_GustStrength/_GustLength/_GustOffset`；删除 `GustAngle`、`TwoPi`。迁移时 Unity 反序列化把旧 `_gustFrequency` 丢弃并填新字段默认值（2.2/20），必须按旧观感换算回写：场景旧 0.5 Hz × 20 m → gustSpeed=10；prefab 旧 0.1114 Hz × 20 m → 2.2282。验证（独立 ComputeShader，64 m 覆盖、64/128²）：阵风峰值 t=0/1/2.5 s 位于 4.5/9.5/17.5 m（每 5 m/s 移动 5 m），幅值 0.494≈0.5，剖面每 20 m 完全重复（0.3 m 与 20.3 m 值相同）；场景实测行 (r,b) 相等证实 45°，幅值 0.5，`_WindFieldValid=1`、128²、主相机非天空采样 49060。`LoadPrefabContents` 预览仍会产生 duplicate 日志，但磁盘 `m_Enabled` 保持 1。

2026-11-04 竖直风（用户要求「草根据风场增高 Y」）：用户先问能否直接用现有风场纹理结算，不能才另建推进层。结论是能从场本身导出，用连续性：`vy = −_VerticalScale × (∂vx/∂x + ∂vz/∂z)`，`_VerticalScale` 为特征混合高度（m，默认 10）。阵风为 `A·sin(k(d·p − off))`，散度 `A·k²·cos(...)`，所以竖直分量与阵风推力**正交**（相位差 90°），幅值 = `A·k²·H`；基础风均匀、湍流两项各只沿垂直于自身的轴变化，二者散度恒为 0，**只有阵风产生升降**。若要湍流也抬升，必须给湍流加发散项，会改变其观感，未做。纹理 G 通道因此不再恒为 0；`SampleWindVelocityWS` 原样返回 float3，消费者无需改动的关键是 `GrassWind.shader` 的 `FieldWindOffsetWS` 本来就返回整个速度向量并乘 `_WindResponse`、`uv.y` 和手绘草高。验证：独立 compute（128²）base=2 或 turb=0.6 单独时 vy∈[0,0]，gust=0.5/L=20/H=10 时 vy∈[−0.492,0.492]（理论 0.4935），与水平层叠加不变；场景实时纹理 vy∈[−0.493,0.493]、均值 0.004；同一帧渲染对比 verticalScale 0 vs 10，640×360 半采样中有 22158 处像素变化、最大色差 0.506。场景与 prefab 均补 `_verticalScale: 10`。
