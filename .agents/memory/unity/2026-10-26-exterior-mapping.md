---
name: exterior-mapping
description: Solid-cube ray mapping on a quad, inverse of interior mapping.
date: 2026-10-26
---

# ExteriorMapping 实体方块面片映射（2026-10-26）

## 目标与结论

在 `Assets/Mine/Shaders/Render/InteriorMapping/` 新增 `ExteriorMapping`：把四边形当作观察窗，逐像素从相机发射射线，与窗内虚拟实体方块求交，按近交点命中面采样六张独立纹理。它与 `InteriorMapping` 共用 ray-AABB 数学，但取**入射面/近交点**，而不是房间远墙，因此是“室内映射的反向”视觉：实体方块而不是空心房间。

## 正式产物

- `ExteriorMapping.shader`：`Render/ExteriorMapping`，透明队列、单 Forward Pass、`Cull Off`、`ZWrite Off`。
- `ExteriorMappingFunction.hlsl`：私有射线、六面 UV、旋转与假光照实现。
- `Mine_Render_ExteriorMapping_Cube.mat`：示例材质，六面暂时共用 `Assets/Mine/Checkers/UVchecker.png`。
- `Mine_Render_ExteriorMapping_Noise.mat`：六面分别绑定 Perlin/Noise/Noise1/NoiseWave/Noise2DRG/Voronoi。
- `ExteriorMapping.md`：数学、面 UV 表、参数、限制与复验命令。
- `Editor/ExteriorMappingValidator.cs`：批处理编译检查；显式编译 Pass 0 并读取 `ShaderUtil` 消息。
- `Assets/Scenes/RenderScene.unity`：新增 `ExteriorMapping_NoiseQuad`，位于主相机前方 6 单位、3×3×3 缩放；场景 diff 仅 +91 行。

## 关键约定

- 方块局部空间为 `[-0.5, 0.5]³`；`+X/+Y/+Z` 分别为右/上/前。
- 面 UV：Right `(-z,y)+.5`、Left `(z,y)+.5`、Top `(x,-z)+.5`、Bottom `(x,z)+.5`、Front `(x,y)+.5`、Back `(-x,y)+.5`。
- 用 `ddx/ddy` 从 UV 导数重建面片切线/副切线/法线，再把相机射线变换到方块空间；因此支持任意旋转面片和 billboard。
- `_CubeSpace`：`Object Transform`（默认）让方块吃对象世界 TRS——中心跟随对象位置，旋转为对象世界旋转 × 局部 Euler，尺寸为 `_CubeSize × 对象世界缩放`（非均匀缩放会产生盒体）；billboard 只转窗口。`Quad Relative` 保留旧的“方块随面片转”行为。
- `_BillboardMode`：`Face Camera` 用顶点相机右/上基向量重建四边形；`Fixed` 保留网格朝向，便于相机绕行观察。
- `Object Transform` 下 `_CubeRotation` 是对象局部 Euler，`_CubeOffset` 是对象局部偏移并乘对象缩放，`_LightDirection` 在世界空间；`Quad Relative` 下三者都在面片空间。
- 相机在方块内时取远交点并翻转 U；面符号必须按“出射面”取 `sign(rd[axis])`，不能沿用近交点的取反规则。

## 验证

- CPU 数值复算通过：正面 UV、旋转 45° 后的侧面命中、相机在方块内时的远交点/U 翻转、默认 25/45 旋转下多面可见。
- `check_norm.py` 对 `.shader/.hlsl/.md/.cs` 全部通过。
- Unity 6000.3.14f1 批处理导入通过；`Mine.ExteriorMapping.ExteriorMappingValidator.Validate` 编译 Pass 0 后 `ShaderUtil` 消息为 0，`shaderSupported=True`，六个面纹理槽全部绑定。
- 报告：`/tmp/exterior-mapping-validation.txt`。
- `RenderScene` 离屏相机 A/B：关闭/开启 noise quad 的中心 1/3 平均差约 62.4/255，55% 像素差 >8，确认真实 URP 路径可见；场景保存后 `isDirty=False`。
- 对象空间三姿态对照：front/side/three-quarter 的中心 1/6 平均差约 57.4/43.2/63.6；`Quad Relative` 负对照约 15.4/12.3/24.1，证明窗口仍朝相机、方块保持对象空间姿态。
- Transform 继承对照：对象绕 Y 旋转 180° 后中心 1/6 平均差 75.5、79% 像素变化；仅 Z Scale 3→1（窗口 X/Y 不变）后中心 1/6 平均差 69.6、60% 像素变化，确认旋转与缩放均来自对象 Transform。
- `AssetDatabase` 未显式编译失败；运行中 Editor 直接 `ShaderUtil.CompilePass(...,0,true)` 后 `msgs=0`、`cubeSpace=1`。

## 未验证 / 后续

- 已做离屏相机实拍；Game View 的人工画质判断、透明排序和轮廓锯齿仍需观察者确认。
- 只有颜色 Pass；没有 ShadowCaster/DepthOnly/DepthNormals。
- 每材质一个方块；多格平铺、逐实例旋转、Cubemap/Texture2DArray 与描边仍是扩展点。
- 场景保存时 POSS 组件曾自动交换两个 `registrationIndex`；已按对象名恢复 `POSM_Caster_Box=0`、`POSM_Caster_Sphere=1`，最终场景 diff 仅新增 quad 的 91 行。

## 来源

- miwls, *Fake 3D Cube Shader (6 Textures, Free Rotation & Perspective)*, Godot Shaders, 2026-07-02, CC0：<https://godotshaders.com/shader/fake-3d-cube-shader-6-textures-free-rotation-perspective-godot-4-x/>
- Joost van Dongen, *Interior Mapping*, 2007/2008：<https://www.joostvandongen.com/Articles/>
