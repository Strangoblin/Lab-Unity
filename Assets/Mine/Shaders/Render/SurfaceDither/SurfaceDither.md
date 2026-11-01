# SurfaceDither — 表面稳定分形抖动

> 参考：Rune Skovbo Johansen, *Surface-Stable Fractal Dithering*（视频 `HPqGaIMVuLs`，开源实现 `runevision/Dither3D`，MPL-2.0）。本项目为 URP 17 / Unity 6 的独立重写。

**路径：** `Assets/Mine/Shaders/Render/SurfaceDither/SurfaceDither.shader`
**类型：** Opaque 材质（Forward / DepthOnly / DepthNormals / ShadowCaster）
**目标管线：** URP 17+

---

## 功能概述

把最终光照亮度映射为两层色之间的抖动图案。图案使用表面坐标锚定，所以点会随物体一起移动；同时根据屏幕导数动态选择分形层级，使点在屏幕上的大小和间距近似恒定。物体靠近时增加更细层级的点，远离时移除点，而不是让点整体放大或缩小。

和普通屏幕空间 Bayer 抖动相比，点不会在表面上“游动”；和普通 UV Bayer 抖动相比，点不会随距离压缩成一片。

---

## 渲染管线

```
Base Map × Base Color
        │
        ├─ 主光 N·L + 主光阴影 + SH 环境光
        │        └──→ 最终亮度
        │
        ├─ Log2 曝光 / 偏移
        │
        ├─ 表面坐标（UV0 或对象空间投影）
        │        └──→ 屏幕导数 Jacobian
        │                 └──→ 最小/最大频率
        │                          └──→ 点间距 → 2 的幂层级
        │
        ├─ 3D 纹理：Bayer 点集 + Z 轴逐层增加点数
        │        └──→ 图案梯度
        │
        ├─ 亮度 ramp → 阈值 → 对比度 → 抗锯齿后的 0..1
        │
        └─ lerp(Dark Color, Light Color, dither)
```

---

## 参数说明

### Surface

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_MainTex` | 2D | white | 基础反照率，参与光照亮度计算；留空时 Shader 按白色处理 |
| `_BaseColor` | Color | white | 基础色乘数 |
| `_AlphaClip` | Toggle | 0 | 启用 alpha 裁剪；深度/阴影 Pass 同步 |
| `_Cutoff` | Range(0,1) | 0.5 | Alpha 裁剪阈值 |

### Lighting

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_AmbientStrength` | Range(0,2) | 1 | SH 环境光强度 |
| `_MainLightStrength` | Range(0,2) | 1 | 主光强度 |
| `_ReceiveShadows` | Toggle | 1 | 是否采样主光阴影 |

### Dither Input

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_InputExposure` | Range(0,5) | 1 | 抖动前亮度曝光 |
| `_InputOffset` | Range(-1,1) | 0 | 抖动前亮度偏移 |
| `_InverseDots` | Toggle | 0 | 反转明暗点；0 为亮点在暗背景 |

### Dither Pattern

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_DitherTex` | 3D | white | 由生成器写入的分形 Bayer 图案 |
| `_DitherRampTex` | 2D | white | 亮度到阈值的补偿曲线 |
| `_CoordinateMode` | Enum | UV0 | `UV0` 使用网格 UV；`Object Space` 按法线主轴投影 |
| `_DitherTiling` | Range(0.01,10) | 1 | 表面坐标缩放；影响点的密度 |
| `_DitherScale` | Range(2,10) | 5 | 点尺度的 log2 指数；越大点越小 |
| `_DotSizeVariability` | Range(0,1) | 0 | 0 = 亮度改变点数量；1 = 亮度改变点大小 |
| `_DotContrast` | Range(0,2) | 1 | 点边缘对比度；1 接近抗锯齿 |
| `_StretchSmoothness` | Range(0,2) | 1 | 斜视/拉伸时对最高频率的补偿 |
| `_QuantizeLayers` | Toggle | 0 | 将层级量化到单层，减少层间插值 |
| `_RadialCompensation` | Toggle | 0 | 透视相机边缘点放大补偿 |
| `_DebugFractal` | Toggle | 0 | 叠加层级 UV 与子层调试色 |

### Dither Color

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_DarkColor` | Color | 近黑 | 抖动暗部颜色 |
| `_LightColor` | Color | 暖白 | 抖动亮部颜色 |

---

## 算法核心

### 1. 屏幕变化率

对表面坐标的屏幕导数做 2×2 Jacobian：

```
J = [dx.x dx.y]
    [dy.x dy.y]
```

用 `Q = |dx|² + |dy|²` 与 `R = det(J)` 求奇异值平方：

```
λ± = (Q ± sqrt(Q² - 4R²)) / 2
```

`sqrt(λ)` 是表面坐标在屏幕上的最小/最大变化率。用最小频率定义点间距，避免斜视时点被拉大。

### 2. 最近 2 的幂层级

```
spacing = minFrequency × 2^DotScale × dotsPerSide/8
level   = floor(log2(spacing))
frac    = log2(spacing) - level
```

表面坐标除以 `2^level` 后查询 3D 图案。`frac` 用于在相邻两个子层之间插值，避免层级切换时整片闪变。

### 3. 3D 图案的 Z 轴

生成器创建 8×8 基础点、128×128×64 的 R8 3D 纹理：

- X/Y：每点占 16×16 texel，周期重复。
- Z：第 `z` 层包含前 `z+1` 个 Bayer 点。
- 点从 25% 层到 100% 层使用，因此放大时只会增加点，不会移除已存在的点。

每个 texel 存的是到最近点的径向梯度；着色器通过对比度把梯度变成清晰点。亮度 ramp 保证不同亮度下输出平均值接近输入。

### 4. 亮度与点大小

`_DotSizeVariability = 0` 时，亮度通过改变点数量表达（Bayer 风格）；`= 1` 时，亮度通过改变点大小表达（半调风格）。中间值在两者之间连续过渡。

---

## 使用方式

1. 等待 Shader 编译完成。
2. 执行菜单 `Tools/Surface Dither/Generate Assets`。
3. 生成器会在同目录创建：
   - `SurfaceDitherPattern8x8.asset`
   - `SurfaceDitherRamp.asset`
   - `SurfaceDitherWhite.asset`
   - `SurfaceDither.mat`
4. 把 `SurfaceDither.mat` 拖到网格上，或复制后调整参数。

生成器可重复执行：已存在的纹理原位更新，不会删除资产；材质保留原引用并重新绑定纹理。

### 推荐场景

- 1-bit / 黑白风格的角色、道具、场景表面。
- 需要点在屏幕上保持稳定的近景/远景混合。
- 低分辨率渲染后放大，配合 Obra Dinn 风格的黑白输出。

### 性能

- Forward Pass 一次 3D 纹理采样 + 一次 2D ramp 采样。
- 每像素做一次 2×2 Jacobian 与少量标量运算。
- 3D 纹理约 1 MB（128×128×64 R8），ramp 128 B。
- 主要成本是主光阴影和 3D 纹理采样；没有额外 RenderTexture 或 Pass。

---

## 已知限制

- **UV 接缝**：`UV0` 模式下，UV 岛边界会出现图案不连续；需要美术拆分 UV 或使用 `Object Space` 模式。
- **对象空间投影接缝**：`Object Space` 模式按顶点法线主轴选择投影面，球体等曲面在轴切换处可能出现接缝；立方体/硬表面最合适。
- **蒙皮/顶点动画**：对象空间坐标不随蒙皮变形移动，蒙皮角色可能出现图案滑动。
- **强各向异性**：极端拉伸时最小频率会压缩点间距；`_StretchSmoothness` 只能缓解，不能完全消除。
- **3D 纹理尺寸约定**：着色器通过 `width / 16` 推断 `dotsPerSide`，通过 `dotsPerSide²` 推断 Z 层数；修改生成器的 `TexelsPerDot` 或层数规则时必须同步改着色器。
- **Radial Compensation**：只是近似补偿，透视边缘仍可能有轻微尺寸变化。
- **光照范围**：只接主光 + SH 环境光，不接附加光、Lightmap、Meta Pass、MotionVectors；需要这些能力时按项目标准模板扩展。
- **Alpha Clip**：只保证 Forward / DepthOnly / DepthNormals / ShadowCaster 的裁剪一致；透明混合另做深度与排序设计。
- **平台**：已在 macOS Metal 上按 URP 17 编译验证；其他平台/XR 变体未验证。

---

## 扩展点

- 增加 RGB 三通道独立抖动（每个通道复用 `SurfaceDither_Evaluate`）。
- 增加 CMYK 半调旋转，按通道旋转表面坐标。
- 增加第二套 UV 的自适应选择，减少 UV 接缝。
- 将 3D 纹理生成器改为可配置的 4×4 / 8×8 / 16×16 预设。
- 把抖动结果接到自定义后处理，做低分辨率 1-bit 放大。

---

## 验证记录

验证入口：`Tools/Surface Dither/Validate`，或批处理
`-executeMethod Mine.SurfaceDither.SurfaceDitherValidator.Validate`。
输出在 `Validation~/`（Unity 忽略该目录），最近结果见 `Validation~/results.txt`。

- **编译**：4 个 Pass 全部通过；Forward 的 `_INVERSE_DOTS`、
  `_QUANTIZE_LAYERS`、`_RADIAL_COMPENSATION`、`_DEBUG_FRACTAL`、
  `_ALPHATEST_ON` 五个变体全部通过。
- **Shader 消息**：导入后与逐 Pass 编译后均为 0 条（macOS Metal）。
- **隔离渲染**：512×512，2.2×2.2×0.2 平板立方体，FOV 32，
  相机 Z = −3.2 / −6.4；为隔离抖动算法，预览场景使用平铺环境光 0.55。
  - 近：非背景 262144 px，暗 152861，亮 73300，中间 35983，边缘密度 0.3497。
  - 远：非背景 97344 px，暗 56912，亮 27678，中间 12754，边缘密度 0.3483。
  - 近/远边缘密度比 **0.996**，与“屏幕点尺寸/间距近似恒定”的目标一致。
- **Object Space 分支**：边缘密度 0.3498，非背景 262144 px，渲染通过。
- **亮度诊断**：`_DotContrast=0`、黑白端点时输出均值 0.5482，
  与环境光 0.55 对齐，说明亮度入口与抖动输出链路正常。

### 未验证项

- 预览场景的 `GetMainLight` 未提供有效主光，因此主光/阴影路径只验证了编译，
  没有在隔离测试中做独立画面判读；真实场景的主光路径仍需人工在 Game View 确认。
- UV 接缝、强各向异性、蒙皮角色、附加光、Lightmap、透明混合与 XR 未覆盖。
- PNG 是显示证据（ARGB32 / sRGB 显示），不是线性 HDR 数值证据。
