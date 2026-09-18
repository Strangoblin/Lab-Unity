# HyperTube

`Render/HyperTube` 是 Frostbyte Shadertoy 片段的 Unity 6 / URP 材质移植，保留原始的十步体积光线步进、Dot Noise、隧道约束、扰动和 ACES 色调映射。

## 源代码对应关系

| Shadertoy | Unity |
|---|---|
| `iTime` | `_Time.y` |
| `iResolution` / `fragCoord` | `GetNormalizedScreenSpaceUV()` + `_ScreenParams.xy` |
| `r(vec2,float)` | `HyperTubeRotate(float2,float)` |
| `a(vec3)` | `HyperTubeAces(float3)` |
| `n(vec3)` | `HyperTubeDotNoise(float3)` |
| `for (...; i < 10.; i++)` | `HYPERTUBE_RAYMARCH_STEPS 10` 的固定循环 |
| `mainImage` | 网格材质的 `Frag` |

## 核心流程

1. 从屏幕坐标构造相机射线：
   `normalize(float3(2 * fragCoord - resolution, resolution.y))`。
2. 将初始位置设为 `(0, 0, time)`，沿射线执行 10 次步进。
3. 每步对 `sin(samplePosition.xy)` 做随 z 和时间变化的二维旋转。
4. 通过两次 Dot Noise 的差异计算步长，并叠加清晰隧道约束与正弦扰动。
5. 以步长倒数累积 RGB 光照，最后执行 `light * light / 600` 和 ACES 色调映射。

## 与原文的必要移植处理

原片段中的局部变量写法依赖 Shadertoy 编译环境对未显式初始化的输出/向量的处理。Unity HLSL 中显式初始化为：

```hlsl
float3 position = float3(0.0, 0.0, time);
float3 light = 0.0;
```

这保留了预期的 `(p.x, p.y) = (0, 0)` 与零累积，同时避免未定义值在 Metal 上产生不稳定结果。

## 使用

给任意朝向相机的平面或 Quad 赋予 `HyperTube_Demo` 材质。Shader 使用屏幕坐标，因此平面只需覆盖目标画面区域；不需要额外纹理、Render Feature 或 C# 组件。

## 性能

固定 10 步光线步进，每步执行两次三维 Dot Noise 计算。该效果的特点就是低步数体积感，不能直接按常规体积云方式增加步数，否则会改变原始外观和性能特征。
