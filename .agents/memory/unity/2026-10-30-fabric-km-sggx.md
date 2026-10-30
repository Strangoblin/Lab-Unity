# 织物层：KM 双流 + SGGX + R/T LUT（并入 PBRToon），2026-10-30

按 B 站「KM 双流丝袜渲染」的思路在本项目落地，只取物理部分，丢掉原教程的六处近似（用视线代替光路、把纹理与肤色点乘当密度 W、波长无关 K/S、皮肤当常量灰 Rg=0.458、coth 换 cosh 造边缘高光、把 T/R 当无能量守恒的线性混合权重）。

**最终形态：PBRToon 的一个可选层**。独立的 `Render/Fabric` shader 与 `FabricSheet.mat` 已退役，织物 BSDF 收进 `Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader` 的 `ENABLE_FABRIC` 分支：Frag 按语义切成「本体层 / 织物层 / 表面层」三段，合成逻辑在 `Assets/Mine/Special/HLSL/FabricFunction.hlsl`（`Fabric_EvaluateLayer` → `Fabric_CompositeLayer` → `Fabric_ShadeLayer`），PBRToon 只做一次调用。单物体、单材质、不透明队列即可。参数只留 `_FabricColor` / `_FabricCoverage` / `_FabricSheen` 加 LUT `_FabricLut`；纤维宽度、最大光程、K/S 上限、绒面色、透射偏色都是 `FabricFunction.hlsl` 里的宏。

**Rg 只能是反射率，不能是风格化后的最终色**（2026-10-30 用户判定）。第一版把「已着色的本体（Diffuse+Specular+Ambient+rim，经 Fresnel 与 Brightness）」当 Rg，用户直接指出这是风格化之后的量，不是物理反射率。修订后的契约：`Rg = baseColor·(1−metallic)` 只进 `1−R·Rg` 分母；`Diffuse+Specular+Ambient+rim` 作为本体辐亮度整体乘 `eff = T²/(1−R·Rg)`；`emission` 走单程 `T`；`fresnel·_Brightness` 在合成之后乘；织物自身反射吃 `radiance1`（含 cel/SSS/AO/补光）而不是裸 `NdotL`，否则 cel 面上会出现连续过渡。绒面只吃 `radiance1` 不吃 SH——这一点决定成图是否与重构前一致（吃 SH 会在边缘亮出 53/255）。

LUT 由 `Tools/Fabric Lut Baker...` 烘焙（生产工具标准结构：固定路径字段 + Bake/Load/Save + 资产所有权 + `CopySerialized` 原位保存），128² RGBAHalf，R=黑底反射率、G=透射率、B=无限厚反射率，UV 用 sqrt 映射到 K/S∈[0,`FABRIC_MAX_KS`]、S·d∈[0,`FABRIC_LUT_MAX_SD`]。

## 工具坑

1. **外部写盘后 Unity 不会重新导入 shader**。用平台 Write 改 `.shader` 后直接渲染会一直用旧变体——材质 `HasProperty` 仍为 true（属性表来自元数据），像素里却没有新代码，表现为「改了没反应」。必须 `AssetDatabase.ImportAsset(path, ForceUpdate)`。本次在这里空转四轮。重导入后的**第一帧**新变体可能还在异步编译，会整颗渲染成纯红/错误色，隔一次调用再渲染才是真结果——别把这一帧当失败。
2. **改 Editor 脚本后同样要显式重导入**。只 Import shader 不触发 Editor 程序集重编译，`Mine.FabricLutBaker.FabricLutBakerWindow` 仍是旧版编译产物（反射查属性会落空，看起来像「代码没生效」）。要 `ImportAsset(.cs, ForceUpdate)` + `Refresh`，等域重载后再探测。
3. **`EditorUtility.DisplayDialog` 是原生模态**，会把 Unity 主线程顶住：unityctl 的 `script.execute` 60s 超时、`dialog list` 也探测不到（它不是 EditorWindow）。自动化里不要走「覆盖确认」分支；真顶住了只能从 System Events 点 Cancel。
4. **`_CameraOpaqueTexture` 在本项目取到全黑**（透明队列 + `cam.Render()` 到手动 RT），不能靠它做透出；要么真 alpha 混合吃帧缓冲，要么像最终方案那样把背景直接算进材质。
5. **PC_RPAsset 的 Additional Lights 是 PerVertex**（`m_AdditionalLightsRenderingMode: 1`），像素阶段 `_ADDITIONAL_LIGHTS` 不编译，点光源对织物无效；验证只能靠主光。要支持额外光源得把该资源改 PerPixel（影响全项目观感，未改）。
6. `close.py` 的 `find_verification` 在项目根 `rel_root="."` 处即剪枝，`Validation~` 与 `tmp/sessions` 都扫不到，verify 义务永远 MISS，只能 `--waive` 留痕。

## 两个建模结论

- **SGGX 各向异性在球面上会出一条竖带**。σ(dir)/σ(N) 在视线与纤维轴平行时取最大值，纤维宽度 0.22 时比值可达 4.5×，球正面就是一条明显的加厚竖条；放宽到 0.55（比值 ≤1.8×）后消失。要更细的纤维就得让纤维方向跟着 UV 走，而不是任意正交基。
- **会计口径决定分层位置**（用户 2026-10-30 判定）：rim / specular 要不要被 `eff` 压住，按能量流判断——本体层的光就是流进织物层、被它吸收与再发射的，所以镜面与边缘光同处薄层之下，一律乘 `eff`。效果维持现状，不再按「镜面不是多次反弹」去拆分。
- **标量 K/S 没有光谱吸收**。织物越深 → K/S 越高，透射率只是个标量，黑丝只能得到中性变暗。用 `saturate(fiberColor*3+0.35)` 给透射一个近似偏色，黑丝才有那层冷灰，染色织物也会跟着偏色。边缘绒面直接取 `pow(1-NdotV,3)` 的 Fresnel 近似（教程的 coth→cosh 就是干这个），因此 `Fabric_Sheen` 的 SGGX 高光瓣在本材质里没接。

## 验证（2026-10-30 并入 PBRToon 后）

- 验证用单球场景（主光左上；材质 = PBRToon + `ENABLE_FABRIC`）成图与并入前逐像素一致，`Validation~/2026-10-30-pbrtoon-fabric/fabric_sphere.png`。该演示场景按用户要求已删除，只留 `Fabric.mat` 参考材质与 `FabricLut.asset`。
- `fabric_ab.png`：同材质开关 `ENABLE_FABRIC` 的左右对照，肤色与薄纱层差异明确，说明层真的生效。
- `fabric_param_grid.png`：覆盖度 0.2/0.85 × 绒面 0/1.0 四格，四个参数都接线。
- Baker：`ValidationLut.asset` 上跑 Bake→Save→Load→Save，烘焙产物与原 `FabricLut.asset` 最大通道差 0.000000，落盘后窗口持有资产系统实例，GUID 稳定，关闭后不留自产纹理。
- `Assets/Mine/Scripts/ProductionTools/Validation~/ProductionToolChecks.cs` 扩到 46 项并纳入 Fabric 窗口（同时把已退化为兼容壳的 `InteriorMapBakerWindow` 换成 `HDRMapBakerWindow`），全部通过。
- PBRToon shader 消息 0 条，6 个 Pass；非织物路径（同材质关 `ENABLE_FABRIC`）正常。
- **三段式重构等价性**：合成搬进 `FabricFunction.hlsl`、Rg 改为本体 albedo 之后，用同一套隔离场景复渲 2×2 网格，与重构前归档图 `fabric_compose_grid.png` 逐像素一致（最大通道差 0），新图存为 `fabric_threestage_grid.png`。四格中心值：裸肤色 (0.839,0.643,0.569)、织物 (0.333,0.247,0.216)、织物+cel (0.239,0.184,0.169)、织物+cel+rim 同前。

## 写库时踩到的两件事

- **共享库内要先声明后使用**。`Fabric_ShadeLayer` 调用 `Fabric_Basis`，而 `Fabric_Basis` 当时排在合成段之后，直接 `undeclared identifier 'Fabric_Basis'`。别按「逻辑分组」排函数，按调用顺序排。
- **判断重构是否等价，看差异分布而不是总量**。复渲 diff 的世界里，裸肤色格 0 差、只有织物格在剪影处差到 53/255，才定位到「绒面项多吃了一份 `SampleSH`」。另外复渲前必须 `ImportAsset(.shader, ForceUpdate)`，否则拿到旧变体，重跑一遍数字一模一样。
