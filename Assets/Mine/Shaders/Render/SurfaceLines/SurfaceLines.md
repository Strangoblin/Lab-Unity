# SurfaceLines — 表面附着程序化线条

> 与 `Render/SurfaceDither` 并列的纯线条版本。这里不使用 3D 纹理、亮度 ramp 或分形层级；
> 线条是解析式程序化图案，目标是先验证“线能否稳定附着表面”。

**路径：** `Assets/Mine/Shaders/Render/SurfaceLines/SurfaceLines.shader`
**类型：** Opaque 材质（Forward / DepthOnly / DepthNormals / ShadowCaster）
**目标管线：** URP 17+

---

## 功能概述

用表面坐标生成程序化平行线，可叠加第二组交叉线。线条跟随所选坐标空间移动：

- `UV0`：跟随网格 UV，美术可控，UV 岛边界有接缝。
- `Object Space`：跟随对象，不依赖 UV；按法线主轴投影，轴切换处有接缝。
- `World Space`：跟随世界，适合静态环境；物体移动时线条会从表面滑过。

线条本身不需要分形结构。屏幕导数只用于抗锯齿和“最小屏幕线宽”，不负责保持线距恒定。

---

## 渲染管线

```
表面坐标（UV / Object / World）
        │
        ├─ 旋转到 Line Angle
        │
        ├─ lineCoord = rotated.y / spacing + phase
        │        ├─ frac → 到线中心距离
        │        ├─ ddx/ddy → 像素 footprint
        │        └─ width / softness / minPixelWidth → 线遮罩
        │
        ├─ 可选第二组：Cross Angle + Cross Intensity
        │
        ├─ lerp(Background Color, Line Color, lineMask)
        │
        └─ × lerp(_MinShade, 1, 光照亮度) → 雾 → 输出
```

> 着色是风格化的：线/底色始终保留，光照只把明暗收在 `_MinShade..1`，避免场景强光把
> 深色底也冲成亮灰，导致线条对比消失。它不是完整 PBR 光照。

---

## 参数说明

### Surface

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_MainTex` | 2D | white | 基础反照率；留空时 Shader 回退为白色 |
| `_BaseColor` | Color | white | 基础色乘数 |
| `_AlphaClip` | Toggle | 0 | 启用 alpha 裁剪 |
| `_Cutoff` | Range(0,1) | 0.5 | Alpha 裁剪阈值 |

### Lighting

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_AmbientStrength` | Range(0,2) | 1 | SH 环境光强度 |
| `_MainLightStrength` | Range(0,2) | 1 | 主光强度 |
| `_MinShade` | Range(0,1) | 0.35 | 最暗处的明暗因子；1 = 完全平光，0 = 允许全黑 |
| `_ReceiveShadows` | Toggle | 1 | 是否采样主光阴影 |

### Line Pattern

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_CoordinateMode` | Enum | UV0 | `UV0` / `Object Space` / `World Space` |
| `_LineTiling` | Range(0.01,10) | 1 | 表面坐标缩放 |
| `_LineSpacing` | Range(0.01,2) | 0.10 | 线条间距；UV 模式为 UV 单位，3D 模式为对象/世界单位 |
| `_LineWidth` | Range(0,0.5) | 0.18 | 线宽占 `_LineSpacing` 的比例 |
| `_LineSoftness` | Range(0,0.5) | 0.01 | 软边占 `_LineSpacing` 的比例 |
| `_LineAngle` | Range(0,180) | 45 | 线条方向（度） |
| `_LinePhase` | Range(0,1) | 0 | 线条相位 |
| `_MinPixelWidth` | Range(0,4) | 1 | 最小屏幕线宽（像素）；0 = 关闭 |
| `_CrossAngleOffset` | Range(0,180) | 90 | 交叉线相对主线的角度 |
| `_CrossIntensity` | Range(0,1) | 0 | 交叉线强度；0 = 关闭 |

### Line Color

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_LineColor` | Color | 暖白 | 线条颜色 |
| `_BackgroundColor` | Color | 近黑 | 非线条区域颜色 |

---

## 算法要点

### 1. 表面坐标

- **UV0**：直接使用网格 UV。线条跟随 UV 岛；UV 接缝处线条会断。
- **Object Space**：用对象空间法线选主轴，把 `positionOS` 投影到 `xy` / `zy` / `xz`。物体移动/旋转时线条跟随物体；蒙皮变形时对象空间坐标不一定跟随蒙皮。
- **World Space**：用世界法线选主轴，把 `positionWS` 投影到对应平面。静态环境稳定；物体移动时线条固定在世界，表面会“滑过”线条。

对象/世界模式都是**主轴投影**，不是三轴混合，所以只采样一次线场，成本低；轴切换处有硬接缝。

### 2. 线场

```
rotated = rotate(surfaceCoord, angle)
lineCoord = rotated.y / spacing + phase
distance = abs(frac(lineCoord) - 0.5)
```

`distance < width/2` 的区域为线心，再用 `smoothstep` 做抗锯齿。线条是周期函数，没有纹理采样。

### 3. 导数抗锯齿与最小线宽

```
footprint = length(float2(ddx(lineCoord), ddy(lineCoord)))
halfWidth = max(width * 0.5, footprint * minPixelWidth * 0.5)
aa = max(softness * 0.5, footprint * 0.5)
```

- `footprint` 是线坐标在屏幕上的变化率；它只用于抗锯齿和最小线宽。
- `_MinPixelWidth = 0` 时使用作者设定的线宽；>0 时线宽至少保持该像素数，避免远处线条消失。
- 这里**不保持线距恒定**。线距会随距离/投影变化；如果要求屏幕上线距恒定，需要额外的层级/频率选择，类似 SurfaceDither 的分形层级，但线条的自相似构造会与 Bayer 不同。

### 4. 交叉线

第二组线使用 `_LineAngle + _CrossAngleOffset`，以 `_CrossIntensity` 混入：

```
lineMask = max(primary, cross * crossIntensity)
```

`_CrossIntensity = 0` 时只有一组平行线。

---

## 与 SurfaceDither 的对比

| 维度 | SurfaceDither | SurfaceLines |
|---|---|---|
| 图案 | 点阵 | 线条 |
| 资源 | 3D Bayer 纹理 + 亮度 ramp | 无纹理，纯解析函数 |
| 分形层级 | 有；缩放时增删点、保持屏幕点距 | 无；线距随坐标/距离变化 |
| 屏幕尺度控制 | 分形层级 + 对比度 | 仅最小线宽 + 导数抗锯齿 |
| 输出 | 两色抖动 | 两色线条 |
| 坐标 | UV0 / 对象空间主轴 | UV0 / 对象空间主轴 / 世界空间主轴 |
| 接缝 | UV 接缝 / 轴接缝 | UV 接缝 / 轴接缝 |
| 适用 | 1-bit 点阵风格 | 排线、轮廓线、程序化条纹 |

关键点：**线条附着表面不需要分形**。分形解决的是多尺度层级下的自相似和“只增不减”；线条是否无缝取决于坐标/投影是否连续，不取决于有没有分形。

---

## 与 SSO 三平面阴影线的关系

SSO 的阴影线是**全屏后处理**：

- 从深度/法线重建世界坐标；
- 用世界空间三平面投影生成 45° 条纹；
- 乘 `(1 - shadowMask)`，只画在阴影区；
- 还包含深度/法线差分轮廓。

SurfaceLines 是**逐材质表面着色器**：

- 线条是材质反照率的一部分；
- 不做深度/法线边缘检测；
- 不做阴影区门控；
- 只影响使用该材质的物体。

因此 SurfaceLines 不能直接替代 SSO 的全局阴影线/轮廓。若要做“材质级阴影线”，可以在 `Frag` 里用 `mainLight.shadowAttenuation` 给线场加门控；若要全局屏幕空间阴影线/轮廓，仍应使用 SSO 这类后处理。

---

## 使用方式

1. 等待 Shader 编译完成。
2. 创建材质并选择 `Render/SurfaceLines`，或使用同目录生成的 `SurfaceLines.mat`。
3. 把材质拖到网格上。
4. 常用调参：
   - 线太密：增大 `_LineSpacing` 或减小 `_LineTiling`。
   - 线太细：增大 `_LineWidth`。
   - 远处线条消失：增大 `_MinPixelWidth`。
   - 需要网格/交叉排线：增大 `_CrossIntensity`。
   - 需要贴合 UV：`_CoordinateMode = UV0`；需要不依赖 UV 的刚体：`Object Space`；静态环境：`World Space`。

### 推荐场景

- 程序化排线、hatching、轮廓线、科幻网格。
- 角色/道具的 UV 线（UV0）或对象空间线（Object Space）。
- 静态建筑/地形的世界空间线（World Space）。

### 性能

- Forward Pass 一次解析线场求值（交叉线时两次），无纹理采样。
- 四 Pass 与 SurfaceDither 相同；阴影/深度/法线 Pass 与 Forward 共用几何入口。
- 比 SurfaceDither 少一次 3D 纹理采样和 ramp 采样；交叉线会增加一次线场求值与导数。

---

## 已知限制

- **线距不保证屏幕恒定**：只保证最小线宽和抗锯齿；远近变化时线距会变化。需要屏幕恒定线距时，必须加层级/频率选择。
- **UV 接缝**：UV0 模式在 UV 岛边界断线。
- **对象/世界主轴接缝**：`Object Space` / `World Space` 在法线主轴切换处有硬接缝；三轴混合可减轻但会增加成本和重影。
- **世界空间滑动**：`World Space` 模式下物体移动时线条不跟随表面。
- **蒙皮**：`Object Space` 使用未蒙皮的对象坐标，蒙皮角色可能滑动；UV0 通常更适合蒙皮。
- **斜率影响**：对象/世界投影在斜面上会压缩线距/线宽；`_MinPixelWidth` 只能保证最小可见宽度。
- **光照范围**：只接主光 + SH 环境光，不接附加光、Lightmap、Meta Pass、MotionVectors。
- **Alpha Clip**：只保证四个 Pass 的裁剪一致；透明混合未设计。
- **平台**：macOS Metal / URP 17 编译验证；其他平台、XR 未验证。

---

## 扩展点

- 增加真 3D 平面族线场（`dot(position, planeNormal)`），消除主轴投影接缝，但线距会随表面斜率变化。
- 增加屏幕恒定线距的层级线族（2 的幂频率、只增不减）。
- 增加阴影门控条纹，做材质级 SurfaceLines + ShadowLine。
- 增加第二套 UV 自适应选择，减少 UV 接缝。
- 增加 RGB/双色线、虚线、渐变线宽等风格变体。

---

## 验证记录

- 静态结构：四 Pass、统一 CBUFFER、私有 HLSL 库、解析线场。
- 编译与渲染：见本次任务的 Unity ShaderUtil 检查和 Game View 冒烟对照；最终结果以实际 Editor 日志为准。
- 线条像素质量仍需人工在 Game View 判断；不同相机距离、UV 岛和轴切换处应分别观察。
