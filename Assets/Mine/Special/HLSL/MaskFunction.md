# MaskFunction

原始深度可见性、背景判断与标量覆盖度组合。无纹理／全局参数声明、无 CBUFFER、无兄弟 include；采样和管线绑定由调用方负责。调用 Shader 先 include Core.hlsl，提供当前平台的 `UNITY_REVERSED_Z` 约定。

| API | 契约 |
| --- | --- |
| `Mask_VisibleRawDepth(fragmentDepth, sceneDepth, tolerance)` | 同一相机／坐标域的 raw depth 比较；容差内可见，返回 0 或 1 |
| `Mask_BackgroundRawDepth(rawDepth, tolerance)` | 远平面清屏深度容差内为背景，返回 0 或 1 |
| `Mask_Union(a, b)` | `max(a,b)`，取较大覆盖度 |
| `Mask_Intersection(a, b)` | `min(a,b)`，取较小覆盖度 |
| `Mask_Invert(value)` | `1-saturate(value)` |
| `Mask_Difference(a, b)` | `saturate(a-b)`；这是饱和相减，不是乘法排除 |

深度输入有限且在 [0,1]，容差非负、单位为 raw depth；不使用 eye depth 或世界距离。反向 Z 时可见性是 `step(sceneDepth-tolerance, fragmentDepth)`，普通 Z 时是 `step(fragmentDepth, sceneDepth+tolerance)`。背景只表达无不透明深度／远端区域，不检查天空颜色。

组合输入为 [0,1] 覆盖度，0 不参与、1 完全参与；支持软权重。并集和交集不自动钳制输入。这些算子是明确的软遮罩规则，不是概率独立性或颜色／ID 的合成规则。

现有消费者：PBRToon 的 MaskBlur 材质 Pass、MaskRBRMask 覆盖材质及 MaskRBR 的背景／物体联合；三处均保留原方程和 `1e-7` 容差。公共生成与生命周期说明见 [Screen Mask](../Rendering/Mask/README.md)。Stencil、颜色提取与多通道输出属于独立输入／载荷策略，当前库不含其实现。
