# ExteriorMapping — 面片内的实体方块射线映射

**路径:** `Assets/Mine/Shaders/Render/InteriorMapping/ExteriorMapping.shader`
**类型:** Transparent（`Queue=Transparent`，`ZWrite Off`）
**目标管线:** Unity 6 / URP 17+

---

## 功能概述

把一张居中四边形当作“观察窗”，在片元里从相机发射射线，与窗内的虚拟实体方块求交，再按命中面采样六张独立纹理。它和 `InteriorMapping` 使用同一类 ray–AABB 数学，但取的是**近交点/入射面**，而不是房间远墙；因此看起来是一个有厚度的实体方块，而不是一面假室内。

支持两种载入方式：

- `Face Camera`：顶点阶段让四边形始终朝向相机，适合道具、卡牌、UI、体素精灵。
- `Fixed`：保留四边形自身朝向，相机可以绕着方块观察，适合做“任意视角”对照展示。

---

## 原理

```
屏幕像素
  │
  ├─ 由 ddx/ddy 重建四边形切线 T、副切线 B、法线 N
  │
  ├─ 相机射线：dir = normalize(P_surface - P_camera)
  │
  ├─ 变换到方块局部空间
  │     ro = R⁻¹ · ((P_camera - C) / size)
  │     rd = R⁻¹ · (dir·T, dir·B, dir·N)
  │
  ├─ Slab ray–AABB（[-0.5, 0.5]³）
  │     tnear = max(min(t0, t1))
  │     tfar  = min(max(t0, t1))
  │
  ├─ 取近交点（相机在方块内时取远交点）
  │     hit = ro + rd · t
  │
  ├─ 由命中轴和方向确定六面之一
  │     faceUV → 对应面纹理
  │
  └─ 假 Lambert 光照 + Tint → Alpha 混合输出
```

### 窗口与方块的空间解耦

`_CubeSpace` 决定虚拟方块使用哪套空间：

- `Object Transform`（默认）：billboard 只让**四边形窗口**朝向相机；方块吃对象组件的世界 Transform。方块中心跟随对象位置，方块旋转为“对象世界旋转 × `_CubeRotation` 局部 Euler”，方块尺寸为 `_CubeSize × 对象世界缩放`（支持非均匀缩放，此时是一个盒体）。相机绕行时，窗口始终正对你，但方块保持对象空间中的姿态。
- `Quad Relative`：方块轴绑定在面片切线/副切线/法线上，会随 billboard 一起转；这是之前的相对空间行为，适合把方块当作道具贴图的一部分。

Object Transform 下，`_CubeRotation` 是对象局部 Euler，`_CubeOffset` 是对象局部偏移（会乘对象缩放），`_LightDirection` 保持世界空间；Quad Relative 下三者都在面片空间。

### 与室内映射的区别

| 项目 | InteriorMapping | ExteriorMapping |
|---|---|---|
| 虚拟体 | 空心房间 | 实体方块 |
| 交点 | 远墙/内壁 | 近交点/外表面 |
| 法线 | 朝向相机内侧 | 命中面的外法线 |
| 纹理组织 | 房间展开图或 Cubemap | 六张独立面纹理 |
| 视觉 | 窗外房间 | 窗内实体方块 |

### 面 UV 约定

方块局部坐标的 `+X / +Y / +Z` 分别对应右、上、前。命中面统一按下表展开：

| 面 | 轴与符号 | UV |
|---|---|---|
| Right `+X` | `x = +0.5` | `(-z, y) + 0.5` |
| Left `-X` | `x = -0.5` | `(z, y) + 0.5` |
| Top `+Y` | `y = +0.5` | `(x, -z) + 0.5` |
| Bottom `-Y` | `y = -0.5` | `(x, z) + 0.5` |
| Front `+Z` | `z = +0.5` | `(x, y) + 0.5` |
| Back `-Z` | `z = -0.5` | `(-x, y) + 0.5` |

这张表与参考实现的六面约定一致。Unity 的纹理 V 轴方向与 Godot 不同，但按上表直接采样时，正面、顶面和侧面的图像方向仍然是自然的；如果美术资源按相反方向绘制，只需在纹理导入设置里翻转，不必改 Shader。

---

## 参数说明

### Cube

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_BillboardMode` | Enum | Face Camera | `Fixed` 保留面片朝向；`Face Camera` 顶点朝向相机 |
| `_CubeSpace` | Enum | Object Transform | `Object Transform` 方块吃对象 TRS；`Quad Relative` 方块随面片转 |
| `_CubeSize` | Range(0.05, 2) | 0.6 | 方块边长相对四边形短边的比例；1 表示与短边等长 |
| `_CubeRotation` | Vector | (25, 45, 0, 0) | 方块局部 Euler 旋转（度），顺序为 Y→X→Z；Object Transform 下叠加在对象世界旋转之后 |
| `_CubeOffset` | Vector | (0, 0, 0, 0) | 偏移；Object Transform 下为对象局部偏移并乘对象缩放，Quad Relative 下为面片空间偏移 |

### Faces

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_FaceFront` | 2D | white | `+Z` 面 |
| `_FaceBack` | 2D | white | `-Z` 面 |
| `_FaceLeft` | 2D | white | `-X` 面 |
| `_FaceRight` | 2D | white | `+X` 面 |
| `_FaceTop` | 2D | white | `+Y` 面 |
| `_FaceBottom` | 2D | white | `-Y` 面 |

### Shading

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_Tint` | Color | (1,1,1,1) | 颜色与整体透明度 |
| `_LightDirection` | Vector | (0.4, 0.8, 0.5, 0) | 假光方向；Object Transform 下为世界空间，Quad Relative 下为面片空间 |
| `_Ambient` | Range(0,1) | 0.35 | 环境项 |
| `_ShadingStrength` | Range(0,1) | 0.65 | 漫反射强度；最终亮度为 `ambient + NdotL * strength` |

---

## 使用方式

1. 使用 Unity 内置 `Quad`，或任意 **1×1、以原点为中心** 的四边形网格。Billboard 模式用对象原点作为旋转枢轴，非居中网格会出现偏移。
2. 新建材质并选择 `Render/ExteriorMapping`，或直接使用同目录的 `Mine_Render_ExteriorMapping_Cube.mat`。
3. 给六个面分别指定纹理。`Mine_Render_ExteriorMapping_Cube.mat` 用 `UVchecker.png` 放在六面，便于观察朝向与透视；`Mine_Render_ExteriorMapping_Noise.mat` 的六面分别使用项目内的 Perlin/Noise/Voronoi 纹理。
4. `RenderScene.unity` 中已放置 `ExteriorMapping_NoiseQuad`，位于主相机前方 6 个单位、缩放 3×3×3，可直接在 Scene / Game 视图观察。需要重建时直接复制该对象或重新创建 quad；材质可复用 `Mine_Render_ExteriorMapping_Noise.mat`。
5. `Face Camera` + `Object Transform` 是“窗口始终朝向相机、方块保持对象 TRS”的组合；相机绕行即可看到不同侧面。`Fixed` 则保留面片自身朝向。
6. 方块尺寸、旋转和偏移都只影响虚拟方块，不会改变四边形网格本身。

### 渲染状态

- `Cull Off`：固定模式下从背面也能看到方块。
- `ZWrite Off`：透明队列，避免在方块内部产生错误深度写入。
- `ZTest LEqual`：仍会被前方不透明几何遮挡。
- `Blend SrcAlpha OneMinusSrcAlpha`：方块外部的 alpha 为 0，因此四边形其余区域透明。

---

## 验证记录

- 六面 UV、近交点选择、旋转后命中面和相机在方块内时的远交点分支，已用独立的 CPU 数值复算检查。
- 文件结构、Shader 声明和分隔线风格通过项目规范检查。
- Unity 6000.3.14f1 批处理导入通过；`Tools` 验证入口显式编译 Pass 0 后 `ShaderUtil` 消息为 0，六个面纹理槽均已绑定。批处理命令：
  `Unity -batchmode -nographics -quit -projectPath <项目> -executeMethod Mine.ExteriorMapping.ExteriorMappingValidator.Validate`
  报告写入 `/tmp/exterior-mapping-validation.txt`。
- `RenderScene` 实拍 A/B：关闭/开启 `ExteriorMapping_NoiseQuad` 的离屏相机图中心 1/3 平均差约 62.4/255，55% 像素差 >8，确认 quad 实际参与 URP 渲染；Scene 已保存且 `isDirty=False`。
- `CubeSpace` 三姿态对照（front / side / three-quarter，相机绕方块同距离）：`Object Transform` 的中心 1/6 平均差约 57.4 / 43.2 / 63.6；切回 `Quad Relative` 的负对照约 15.4 / 12.3 / 24.1。对象空间模式会随观察角看到不同面，而面片 billboard 仍朝向相机。
- Transform 继承对照：同一相机下，对象绕 Y 旋转 180° 后中心 1/6 平均差约 75.5、79% 像素变化；仅把对象 Z Scale 从 3 改为 1（窗口 X/Y 尺寸不变）后中心 1/6 平均差约 69.6、60% 像素变化。确认旋转与缩放均由对象 Transform 驱动。
- Game View 的人工画质判断仍由观察者完成；重点看：正面/斜视、`Fixed` 模式绕行、`CubeRotation` 三个轴、`CubeSize` 两端值，以及透明排序。

---

## 已知限制

- 每个材质/四边形只表示一个方块；没有实现网格内多房间平铺。
- 只有前向颜色 Pass，没有阴影、深度、运动矢量或自定义深度写入。透明物体的排序问题仍然存在。
- 面纹理只支持单次采样，不读取 `_ST`，因此没有逐面 Tiling/Offset。
- 轮廓是硬边，MSAA 可以缓解锯齿；没有做边缘抗锯齿或描边。
- 相机进入方块内部属于近似处理：使用远交点并翻转 U；主要用于避免近距离穿帮，不作为室内场景功能。
- `Face Camera` 模式下四边形始终正对相机，因此相机绕行不会改变方块的相对观察角；需要绕行观察时使用 `Fixed`。
- Object Transform 使用对象世界缩放的全部分量；对 1×1 Quad，若希望方块保持等边，请把 Transform Scale 的 Z 也设为与 X/Y 相同。负缩放未验证，建议保持正缩放。

---

## 扩展点

- 用 `Texture2DArray` 或 Cubemap 替代六张 2D 纹理，减少采样分支和材质槽位。
- 通过 `UNITY_INSTANCING_BUFFER` 增加逐实例旋转/尺寸，支持大量不同方块。
- 用屏幕空间导数估计轮廓距离，做抗锯齿或描边。
- 增加 `ShadowCaster` / `DepthOnly` Pass，并复用同一套 billboard 与面裁剪逻辑。
- 把 UV 先 `frac` 成网格单元，可在同一大面片上复用多个方块（需要同时传入每格随机种子）。

---

## 来源

- miwls, *Fake 3D Cube Shader (6 Textures, Free Rotation & Perspective) — Godot 4.x*, Godot Shaders, 2026-07-02：<https://godotshaders.com/shader/fake-3d-cube-shader-6-textures-free-rotation-perspective-godot-4-x/>（CC0；本实现的六面 UV 约定与射线思路参考了该文，Unity/URP 版本为重新实现）。
- Joost van Dongen, *Interior Mapping*（2007/2008）：<https://www.joostvandongen.com/Articles/>（室内映射的原始思路，用于对照“远墙/内壁”与“近交点/外表面”的差异）。
- Andrew Gotow, *Interior Mapping – Part 1*：<https://andrewgotow.com/2018/09/09/interior-mapping-part-1/>（室内映射的射线与面选择说明，作为背景参考）。
