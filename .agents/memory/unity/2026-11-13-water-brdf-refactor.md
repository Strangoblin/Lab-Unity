---
name: water-brdf-refactor
description: Water.shader 从「opaque baseColor 假装深度 + 反射折射相加」改为 Fresnel 分配 + 单步 Beer–Lambert 体积散射；含 0/0 焦散、_FoamIntensity=0、地平线夹取、薄水无色四个陷阱
date: 2026-11-13
---

# Water 重构：界面 BSDF + 单步体积散射

## 结论

`Assets/Mine/Shaders/Render/Water/Water.shader` 的能量结构改成

```text
L_out   = F · L_reflection + (1 - F) · L_water
L_water = L_background · exp(-σ_t · pathLen) + L_inScatter
```

`F = BSDF_Fresnel(NdotV, 1, _IOR)`（`_IOR = 1.333` → F0 ≈ 0.0204）。反射与透射**共用同一个 F**，
不再各算各的再加起来；旧版 `_Alpha` 与「用 `_baseColor` 模拟不同深度水底」两条路径整体删除，
深度感只由 `_Extinction` (m⁻¹) 与 `pathLen` 产生。

## 为什么是单步而不是 SSC 的多步积分

SSC（`Shaders/PostProcess/SSC`）在屏幕空间沿光路多段采样，能表达**非均匀介质**与体积阴影。
水体在预期使用场景里是常系数均匀介质，`σ_t / σ_s / 相函数` 都与位置无关，积分可解析求出，
代价只有一次 `exp`。放弃的是：路径上被遮挡的区段、云影/船影的柱状阴影 —— 需要再上多步。

## 关键实现

| 量 | 做法 |
|---|---|
| 垂直水柱厚 | `sceneDepDf = max(waterY − scenePosWS.y, 0)`，`scenePosWS` 由 `_CameraDepthTexture` 反投影 |
| 路径长度 | `pathLen = min(sceneDepDf / max(\|refrDir.y\|, 0.1), _MaxThickness)` |
| 透射率 | `exp(-_Extinction.xyz * pathLen)`，RGB 分别消光（红先掉） |
| 覆盖率 | `C = 1 - exp(-pathLen * _ScatterDensity)`，与背景**混合**而非相加 |
| 水体色 | `ScatterColor(pathLen) * (sun·phase·shadow/k + SH) * _ScatterIntensity`，`C→1` 的极限 |
| 相函数 | 峰值归一化 HG，`g` 变化时整体亮度不漂 |
| 折射偏移 | `refrDir.xz · clamp(depth) · _Distortion · 0.05`，再经 `ComparePositionSS` 防穿透前景 |

`WaterFunction.hlsl` 里集中放 `Water_F0 / Water_InterfaceFresnel / Water_EnvReflection /
Water_SunSpecular / Water_RefractionDirection / Water_LightDirectionUnderwater /
Water_RefractionOffsetWS / Water_PathLength / Water_PhaseHG / Water_BodyCoverage /
Water_ScatterColor / Water_BodyRadiance`；通用数学仍走 `Special/HLSL/` 的
`PBRFunction` / `ENVFunction` / `DepthDiffFunction`，不复制。

## 四个陷阱（都会静默出错，不报编译错）

**1. `_CausticsScale = 0` 导致 0/0 带状伪影。** 它被同时当作半径用：
`surfaceResidualRadius = _CausticsScale · (1 + sceneDepDf)`，默认值 0 让 `confidence` 除零，
掠射/遮挡视角出现褐色团块。修法：

```hlsl
float eps = max(_CausticsScale, 1e-4) * (1.0 + sceneDepDf);
```

scale = 0 时退化成 `confidence ≈ 0`，语义正好是「关掉焦散」。

**2. `_FoamIntensity = 0` 不是关泡沫，而是开满。** `saturate(exp(-edge · 0 · 10)) = 1`。
要关掉泡沫必须把 `_FoamColor` 设黑（验证脚本里踩过一次，整屏白）。

**3. 环境反射钻到地平线以下。** 设视线仰角 θ、法线倾角 α，镜面反射仰角 `R.y = sin(θ − 2α)`。
掠射时 θ 只有几度，`_NormalIntensity = 1` 的 FFT 法线倾角 10°–25°，浪坡背向视线的一侧
`R.y < 0` —— cubemap 该方向是天空盒/HDRI 的**地面半球**（程序化天空盒是灰，HDR 全景是沙土色），
于是浪尖出现与环境无关的灰/土色块。修法：`reflectDir.y = max(reflectDir.y, 0.05)` 再归一化，
水平方位不变所以波峰仍能打散天空渐变。实测（同一材质只切夹取）：不夹取时浪背是成片土色面，
夹取后全部变天空色；`_NormalIntensity` 0 → 1 时地平线以下像素从 0% 涨到 1.64%（按红/绿通道
分别编码 R.y 的 debug 输出统计）。0.05 ≈ 2.9°，同时避开 HDRI 天空/地面接缝。

**4. 纯加性的单步散射做不出薄水色。** 0.2 m 处单次散射只有 2.6%，而 `T ≈ 0.97`，在已经很亮的
沙底上加 2.6% 的散射色，人眼完全看不出来。旧版有颜色是因为 `_Alpha = 0.85` 是**与深度无关的
常量**。改成覆盖率混合 `L = bg·T·(1-C) + body·C`、`C = 1 - exp(-pathLen·_ScatterDensity)` 之后
薄水才起色；`_ScatterDensity = 2` 时 1 m 处 C ≈ 0.86，正好复刻旧 `_Alpha = 0.85` 的手感。
实测（亮沙底，俯视，无水参考 `(0.984,0.878,0.729)`）：0.2 m 从 `(0.949,0.867,0.729)` 变成
`(0.792,0.725,0.612)`，5 m 从 `(0.447,0.761,0.737)` 变成 `(0.071,0.302,0.380)`。
代价：俯视时朝阳/背光变得一样（`refrDir` 接近竖直，HG 相函数在方位角 ±180° 对称），
方向感只剩掠射角下的高光——需要方向差异就得上多步。
`_baseColorA` 也从“反照率”变成“浅水时看到的水体色”，默认的深藏青会把薄水压暗，
做明亮浅滩要把它调到 `(0.15, 0.50, 0.55)` 这个量级。

**0.1 m 读数是纯黑的陷阱**：那不是体积散射，是边缘泡沫 `exp(-sceneDepDf · _FoamIntensity · 10)`
在 0.1 m 处仍约 0.6，测试脚本又把 `_FoamColor` 设成黑色，深色泡沫盖住了画面。

**改 `.hlsl` 共享库不强制重导入，Unity 会继续用旧二进制**：`GetShaderMessageCount == 0`、
面板一切正常，但画面不变。本轮实测把 `Water_EnvReflection` 改成 `return float3(4,0,4)`
都不出现在画面里，`AssetDatabase.ImportAsset(..., ForceUpdate)` 后立刻生效。
排查「改了没反应」先强制重导入，再怀疑逻辑。

## 实测（独立空场景，无交互相机）

| 检查 | 结果 |
|---|---|
| 掠射变亮 | F：俯视 0.0203 → 掠射 0.115 → 0.347（越靠地平线越大） |
| 俯视透底 | 水底 1 m → sRGB (0.341,0.369,0.345)；5 m → (0.067,0.286,0.369) |
| 深度差 | 红通道先衰减，符合 `exp(-σ_t·d)` + 覆盖率曲线 |
| 薄水起色 | 0.2 m 蓝通道与无水底差 0.117（加性散射时只有 0.000） |
| 朝阳更亮 | 俯视时两者相同 (0.161,0.282,0.388)，方向感只剩掠射高光（见陷阱 4） |
| 前景不泄漏 | 水面前方立方体保持纯红，水面不上色 |

`_Extinction = (0.35, 0.09, 0.045)`、水底 −5 m 时 `pathLen ≈ 5.8`，
`T = (0.128, 0.588, 0.766)`，与手算一致。

## 未解：`WaterInteractionDemo` 里深度读到水面自身

该场景的离屏截图路径下主相机 `_CameraDepthTexture` 读到的是水面（`sceneDepDf ≈ 0`），
体积散射不生效。已排除：PC_Renderer 全部 8 个 Feature、IA_Renderer 两个 Feature、
交互相机本身、相机设置（克隆相机同复现）、`DepthOnly` 的 LightMode 改名、跨相机全局纹理泄漏
（空场景相机 + Demo 叠加时仍正确读到自己的水底）。**重构前的旧 shader 同样如此**，不是本次引入。
同一材质放进空场景一切正常，所以结论只能写进 `Water.md` 的「已知问题」。

## 相关

- 文档：[Water.md](../../../Assets/Mine/Shaders/Render/Water/Water.md)（含参数表、公式表、迁移提示）
- 前序：[水体互动 Demo](2026-11-04-water-interaction-demo.md)（`DepthOnly` Pass 与 `TransparentDepthMergeFeature` 的由来）
- 前序：[Water 焦散屏幕无关化](2026-08-04-water-caustics-screen-independent.md)
- 对照：[SSC 云遮蔽](2026-11-13-ssc-cloud-occlusion.md)（多步积分的另一侧）
