# Water — FFT 水面材质（BRDF 界面 + 单步体积散射）

**路径：** `Assets/Mine/Shaders/Render/Water/Water.shader`  
**私有函数库：** `Assets/Mine/Shaders/Render/Water/WaterFunction.hlsl`  
**Wave 生成模块：** `Assets/Mine/Scripts/FieldManager/Wave/`  
**类型：** Transparent / Tessellation / UniversalForward  
**目标管线：** Unity 6 + URP 17+

---

## 功能概述

Water 由三层 FFT 频谱驱动几何位移与法线，片元阶段按**界面 BSDF**拆分能量：

```text
L_out = F · L_reflection + (1 - F) · L_water
L_water = L_background · T(pathLen) · (1 - C) + L_body · C
C = 1 - exp(-pathLen · _ScatterDensity)
```

- **界面**：`F` 是水-空气界面的 Fresnel（`BSDF_Fresnel(NdotV, 1.0, _IOR)`，`_IOR = 1.333` → `F0 ≈ 0.0204`）。反射项包含环境反射与 GGX 太阳高光；透射项整体乘 `(1 - F)`，保证反射/透射能量在同一处分配。
- **水体**：单步均匀介质解。`T = exp(-σ_t · pathLen)` 是背景的 Beer–Lambert 衰减，`C` 是水体覆盖率，`L_body` 是深水极限的水体辐亮度；`C → 1` 时背景被完全取代，正是积分收敛到纯水体色的那一端。`pathLen` 由屏幕深度反推的水柱垂直厚度除以折射方向的 Y 分量得到。
- **水体颜色不再由 `_baseColor` 模拟深度**：原来“用 basecolor 假装不同深度的水底反射”的路径已移除，水底的真实颜色一律经 `_CameraOpaqueTexture` 采样后按 `T` 衰减。

> 与旧版的差别：旧版把不透明/透明的混合写在一个 `_Alpha` 里，反射与折射是相加的，深水只能靠调 `_baseColor` 假装。现在反射与透射共用同一个 `F` 做能量分配，深度感由 `_Extinction` 与 `pathLen` 真实产生。

## 渲染流程

```text
Object Space 顶点
    → Hull / Domain 细分
    → Domain 采样 FFT displacement + Interaction height        (几何)
    → 世界空间水面位置 / FFT normal / Interaction slope         (法线)
    ─────────────────────────────────────────────────────────
    基础屏幕坐标 basicUV → _CameraDepthTexture → basicWaterDepth
    → refrDir = refract(-V, N, 1/_IOR)
    → offsetWS = refrDir.xz · clamp(depth) · _Distortion · 0.05
    → ComparePositionSS 防止扭曲穿透前景
    → screenUV → sceneDepth → sceneDepDf = waterY - sceneY
    → pathLen = sceneDepDf / |refrDir.y|（夹到 _MaxThickness）
    ─────────────────────────────────────────────────────────
    F            = BSDF_Fresnel(NdotV, 1, _IOR)
    reflection   = envRadiance · F + sunSpecular(GGX)
    transmittance= exp(-_Extinction · pathLen)
    coverage     = 1 - exp(-pathLen · _ScatterDensity)
    bodyRadiance = scatterColor(pathLen) · (sun·phase·shadow / sunPath + SH) · _ScatterIntensity
    waterColor   = background(sceneUV) · transmittance · (1 - coverage) + bodyRadiance · coverage
    color        = reflection + (1 - F) · waterColor
    ─────────────────────────────────────────────────────────
    泡沫覆盖 → Transparent 输出
```

## 参数说明

### Water Body

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_baseColorA` | Color | (0.02, 0.12, 0.18) | 浅水散射色（`pathLen → 0`） |
| `_baseColorB` | Color | (0.10, 0.42, 0.48) | 深水散射色（`pathLen ≥ _ScatterDepth`） |
| `_Extinction` | Vector | (0.35, 0.09, 0.045) | 消光系数 σ_t，单位 1/米，RGB 分别消光 → 红光先被吃掉 |
| `_ScatterIntensity` | Range(0, 4) | 1 | 水体辐亮度总倍率 |
| `_ScatterAnisotropy` | Range(-0.9, 0.9) | 0.7 | HG 相函数各向异性 `g`，正值=前向散射 |
| `_ScatterDepth` | Range(0.1, 30) | 5 | 浅→深散射色的过渡深度 |
| `_ScatterDensity` | Range(0.1, 20) | 2 | 水体覆盖率 C 的饱和速度，单位 1/米。≈2 时 1 m 处 C ≈ 0.86，与旧版 `_Alpha = 0.85` 的观感对齐；调大→薄水更快起色，调小→水更透 |
| `_MaxThickness` | Range(0.1, 50) | 20 | 路径长度上限，同时限制折射偏移；防止掠射角处 `pathLen` 爆掉 |

### Interface

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_IOR` | Range(1, 2) | 1.333 | 水的折射率；同时决定 `F0` 与折射方向 |
| `_Roughness` | Range(0, 1) | 0.08 | 环境反射的 mip 选择与 GGX 太阳高光粗糙度（内部夹到 ≥ 0.02） |

### FFT Wave

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_DisplacementScale` | Float | 1 | FFT 几何位移倍率 |
| `_NormalIntensity` | Range(0, 2) | 1 | FFT 法线与网格法线混合强度 |
| `_TessellationFactor` | Range(1, 32) | 8 | Hull 固定细分系数 |

### Foam

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_FoamIntensity` | Range(0, 1) | 0.5 | 边缘泡沫与波峰泡沫的阈值控制 |
| `_FoamColor` | Color | 白 | 泡沫受主光 + SH 着色后的基色 |

> ⚠️ `_FoamIntensity = 0` **不是关闭泡沫**，而是把 `exp(-edge · 0 · 10)` 推到 1（最大边缘泡沫）。
> 要真正关掉泡沫，把 `_FoamColor` 设为黑色。

### Caustics

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_CausticsScale` | Range(0, 1) | 0 | 反推残差匹配半径的基础尺度；0 = 关闭 |
| `_CausticsIntensity` | Range(0, 2) | 1 | 焦散附加亮度倍率 |

### Refraction / Interaction

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_Distortion` | Range(0, 1) | 0.1 | 屏幕空间折射偏移强度 |
| `_InteractionHeightScale` | Range(0, 4) | 1 | 交互高度场 → 垂直位移 |
| `_InteractionNormalStrength` | Range(0, 4) | 1 | 交互高度场梯度叠加到法线 |

## 公式清单

| 物理量 | 实现 | 说明 |
|---|---|---|
| 界面 F0 | `((n-1)/(n+1))²` | `n=1.333` → 0.0204 |
| Fresnel | `BSDF_Fresnel(NdotV, 1, n)` | 含掠射全反射 |
| 太阳高光 | `Spec_CK(NdotH,NdotV,NdotL,VdotH,r) · F_P5(F0,VdotH) · L · shadow · NdotL` | GGX + Schlick |
| 环境反射 | `unity_SpecCube0.SampleLevel(reflect(-V,N), mip(_Roughness))` | 由调用方乘 `F` |
| 环境反射方向 | `reflectDir.y = max(reflectDir.y, 0.05)` 后再归一化 | 见下方「地平线夹取」 |
| 折射方向 | `refract(-V, N, 1/n)` | 全反射时退回视线方向 |
| 垂直厚度 | `sceneDepDf = max(waterY - sceneY, 0)` | 来自 `_CameraDepthTexture` |
| 路径长度 | `pathLen = min(sceneDepDf / max(|refrDir.y|, 0.1), _MaxThickness)` | 沿折射光线 |
| 透射率 | `exp(-σ_t · pathLen)` | Beer–Lambert，单步 |
| 水体覆盖率 | `1 - exp(-pathLen · _ScatterDensity)` | 独立于 `σ_t` 的美术曲线，见下方「薄水面为什么没有颜色」 |
| 太阳光路衰减 | `k = 1 + 1/|Lw.y|`，太阳项除以 `k` | 只压太阳项，环境光不压 |
| 相函数 | 峰值归一化 HG | 保证 `g` 变化时整体亮度不漂 |
| 散射色 | `lerp(_baseColorA, _baseColorB, pathLen / _ScatterDepth)` | 浅/深过渡 |

## 为什么是单步而不是 SSC 那样的多步积分

SSC（`Shaders/PostProcess/SSC`）在屏幕空间沿光路做多段采样，能表达**非均匀介质**（不同位置密度不同、被遮挡的光路、体积阴影）。水体在预期使用场景里是**常系数均匀介质**：单步解析解

```text
L = L_bg · exp(-σ_t d) + ∫₀^d σ_s · p(θ) · L_s · exp(-σ_t (t + t_sun)) dt
```

在 σ_t、σ_s、相函数都与位置无关时可解析积出，代价只有一次 `exp`。代价是：

- 单步散射沿**一条**折射路径计算，不区分路径上被遮挡的区段；
- 没有体积阴影，云影/船影不会在散射里留下柱状阴影；
- `k = 1 + 1/|Lw.y|` 是太阳斜射的近似光路，不是真实的多段积分。

需要这些效果时再上多步（参考 SSC），否则单步的成本收益最好。

## 薄水面为什么没有颜色（覆盖率 C）

纯加性的单步散射在**薄水**上几乎给不出颜色，原因是两条曲线都很平：

- 单次散射量 ∝ 路径长度：0.2 m 处 `(1 - exp(-σ_t·d·k))/k ≈ 0.026`，只有 2.6%；
- 同时 `T ≈ 0.97`，亮沙底照旧是亮的。

在这块**已经很亮**的背景上加 2.6% 的散射色，人眼完全看不出“这是水”。旧版靠的是
`lerp(transparent, opaque, _Alpha)` 里那个恒定的 `_Alpha = 0.85`——它让每个水面像素都至少
有 85% 的水体色，与深度无关，所以薄水也有颜色。

现在的做法是把「水体覆盖背景」这件事交给独立的覆盖率曲线：

```text
C       = 1 - exp(-pathLen · _ScatterDensity)
L_water = L_bg · T · (1 - C) + L_body · C
```

- `C` 只控制**多快变浑**，不参与能量守恒，调它不会改变深水亮度（`C → 1` 时自然收敛到
  `L_body`，正是单步积分的深水极限）。
- 背景衰减 `T` 与覆盖 `C` 相乘，薄水段两者都接近 1，所以水底仍然透；深水段两者都趋 0，
  背景完全让位给水体色。
- `_ScatterDensity = 2` 时 1 m 处 `C ≈ 0.86`，正好复刻旧版 `_Alpha = 0.85` 的手感；
  想要薄水更早起色就调大（4 → 0.2 m 处 C ≈ 0.55），想要更通透就调小。

实测（空场景，亮沙底 0.62/0.58/0.48，俯视；无水参考色 `(0.984, 0.878, 0.729)`）：

| 水深 | 改前（加性） | 改后（覆盖率混合，`_ScatterDensity = 2`） |
|---|---|---|
| 0.2 m | (0.949, 0.867, 0.729) | (0.792, 0.725, 0.612) |
| 0.5 m | (0.906, 0.863, 0.733) | (0.576, 0.553, 0.478) |
| 1.0 m | (0.835, 0.847, 0.733) | (0.337, 0.365, 0.345) |
| 5.0 m | (0.447, 0.761, 0.737) | (0.071, 0.302, 0.380) |

改前 0.2 m 与「无水」几乎无法区分（蓝通道只差 0.000），改后薄水立刻有水体色。
注意 `_baseColorA` 现在直接就是**浅水时看到的水体色**；默认的深藏青会把薄水压成暗调，
需要“明亮浅滩”请把它调成偏亮的青绿（例如 `(0.15, 0.50, 0.55)`）。

> 顺带解释一个观测陷阱：水深 0.1 m 时画面会读到接近纯黑，那不是体积散射的问题，而是
> 边缘泡沫 `exp(-sceneDepDf · _FoamIntensity · 10)` 在 0.1 m 处仍约 0.6，测试脚本又把
> `_FoamColor` 设成了黑色，于是深色的泡沫盖住了画面。

## 环境反射的地平线夹取

设视线仰角 `θ`、波面法线相对竖直方向的倾角 `α`，镜面反射方向的仰角正好是

```text
R.y = sin(θ − 2α)        （法线朝远离视线一侧倾斜时）
```

掠射时 `θ` 只有几度，而 FFT 法线在 `_NormalIntensity = 1` 时倾角常在 10°–25°，
`2α` 很容易吃掉 `θ`：**浪坡背向视线的一侧，反射方向会钻到地平线以下**。
`unity_SpecCube0` 在水平线以下装的是天空盒 / HDRI 的地面半球（默认程序化天空盒是灰，
HDR 全景多半是沙土色），于是浪尖会出现与环境无关的灰/土色块——物理上那些方向看到的
应该是地平线附近的天空，不是地面。

修法是把反射方向的 y 夹到地平线稍上方（`0.05 ≈ 2.9°`，同时避开 HDRI 天空/地面的接缝），
**水平方位保持不变**，所以波峰仍然会打散天空渐变，不会塌成一条平灰带。
实测（同一场景、同一材质，只切换夹取）：不夹取时浪背出现成片的土色面，夹取后全部变成天空色。

## 屏幕折射与深度

- `_CameraDepthTexture` 既决定水面下的水柱厚度（`sceneDepDf`），也决定扭曲坐标是否穿透前景（`ComparePositionSS`）。
- 需要 URP Asset 开启 **Opaque Texture**、**Depth Texture**，焦散需要 **Depth Normals**。
- 水面处于 Transparent 队列，`ZWrite Off`、`ZTest LEqual`、`Blend One Zero`，靠自身 alpha 混合关闭。
- `DepthOnly` Pass 不做位移、不细分，只被 `TransparentDepthMergeFeature`（layer 6 + `shaderTags: [DepthOnly]`）用来把水面写进交互相机自己的 `_CameraDepthTexture`，作为穿透深度的参照面。

## 验证（空场景，PBR 路径）

用独立空场景（无交互相机、无其他场景叠加）跑 5 项：

| 检查 | 结果 |
|---|---|
| 掠射变亮 | `F` 从俯视 0.0203 → 掠射 0.115 / 0.347（像素越靠地平线越大）¹ |
| 俯视透出水底 | 水底 1m → sRGB (0.341,0.369,0.345)；5m → (0.067,0.286,0.369)，红通道先衰减 |
| 5m 与 1m 不同 | 亮度与色相均不同，符合 `exp(-σ_t d)` 与覆盖率曲线 |
| 薄水起色 | 0.2 m 与无水参考 (0.984,0.878,0.729) 的蓝通道差 0.117（加性散射时只有 0.000） |
| 俯视朝阳/背光 | 两者相同 (0.161,0.282,0.388)——俯视时折射方向接近竖直，HG 相函数在方位角 ±180° 上对称，只差在掠射角下的高光 |
| 前景不泄漏 | 前方立方体像素保持纯红 (1.000,0.314,0.318) / (1.000,0.298,0.298)，水面另侧 (0.255,0.361,0.475)，天空 (0.059,0.098,0.129) |

¹ `F` 是用临时 `_DebugMode` 通道读出的像素值，该通道已在收尾时移除；数值与 Schlick 公式在
`cosθ ≈ 0.375 / 0.195` 处一致（`F0 + (1-F0)(1-cosθ)^5`），掠射角读数随行号单调上升。
表中其余数字来自空场景复跑，可由 `tmp/sessions/2026-11-13-water-brdf/staging/Verify.cs` 重建。

> **已知问题（未解）**：在 `WaterInteractionDemo` 的离屏截图路径下，主相机 `_CameraDepthTexture`
> 读到的深度对应水面自身（`sceneDepDf ≈ 0`），因此体积散射在该场景截图中不生效。
> 已排除：PC_Renderer 的 8 个 RendererFeature、IA_Renderer 的两个 Feature、交互相机本身、
> 相机设置（克隆相机同样复现）、`DepthOnly` 的 LightMode 改名。
> 同一材质放到空场景时深度正确（水底 -5m 精确读出）。该现象在重构前的旧 shader
> 中也存在（旧版同样采样 `_CameraDepthTexture`），不是本次改动引入。

## 性能

- 几何：Hull/Domain 细分，`_TessellationFactor` 固定。
- 片元：3 次 FFT 法线采样 + 3 次 displacement alpha（泡沫）+ Scene Color/Depth/Normals + `unity_SpecCube0` 1 次采样。
- 体积：无循环，两次 `exp`（透射率 + 覆盖率）与一次 `pow`。
- 焦散关闭（`_CausticsScale = 0`）时仍会执行一次 `confidence` 计算，但没有额外纹理采样。

## 维护约定

- 渲染入口、结构体、Pass 保留在 `Water.shader`；Water 专属数学放在 `WaterFunction.hlsl`。
- 跨效果通用函数继续放在 `Assets/Mine/Special/HLSL/`（`PBRFunction`、`ENVFunction`、`DepthDiffFunction`），不复制进私有库。
- FFT 数据由 `FieldManager/Wave/WaveField.hlsl` 提供：位移 3 层级联、法线权重 `0.6/0.3/0.1`、泡沫权重 `0.5/0.3/0.2`。
- `_CausticsScale` 用 `max(scale, 1e-4)` 兜底，避免默认值 0 造成 0/0 带状伪影。
- **改 `.hlsl` 共享库后必须强制重导入**：`AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)`，
  否则渲染继续用旧的着色器二进制，且 `GetShaderMessageCount == 0`，看不出任何异常（本轮实测：
  改了 `Water_EnvReflection` 后连“返回纯洋红”都不出现在画面里，强制重导入后立刻生效）。

## 迁移提示

- 旧的 `_Alpha` 已移除：能量分配改由 `F` 完成，不要再手工混不透明度。
  想要旧版那种“恒定的水体感”，调低 `_ScatterDensity`（1 m 处 `C = 1 - exp(-_ScatterDensity)`）。
- 旧的“用 `_baseColor` 模拟深度”的设置请改为调 `_Extinction`：它才是深度感的来源。
- `_baseColorA` 现在直接就是浅水时看到的水体色，不再当反照率用；做浅滩要把它的亮度提到合理值。
- 新材质的推荐起点：`_Extinction ≈ (0.35, 0.09, 0.045)`、`_ScatterIntensity = 1`、`_ScatterDepth = 5`、`_ScatterDensity = 2`、`_MaxThickness = 20`。
