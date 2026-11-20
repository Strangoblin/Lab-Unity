# TransparentShadow — 光空间透明透射图

管线级替代主光 Cookie 的彩窗方案：从主光视角把透射玻璃层渲染成一张光空间 RT，
RGB = 玻璃透射色，A = 归一化光空间深度。接收者用“自身光空间深度 vs 玻璃深度”判断
玻璃前后：玻璃前回白（普通阳光），玻璃后取玻璃颜色。

2026-11-20 由 `ColoredShadow` 改名；类、全局、Shader Tag 统一为 `TransparentShadow`。

## 层级约定

- 目标层由 Feature 的 `layerMask` 直接指定，不按名字查找；把它对齐到命名为 `TransparentWithShadow`
  的那一层（当前 Layer 8），由使用者手动维护。
- 正常渲染由普通管线负责：可见窗面是普通透明材质（Blend、`ZWrite Off`、距离排序），不经过 Feature。
  Feature 只额外把该层物体画进光空间透射图。
- 要让物体“既正常显示、又进透射图”，它的层必须同时被所用 Renderer 的 Opaque / Transparent Layer Mask
  和场景相机 Culling Mask 放行——这是 URP 的常规设置，Feature 不修改 Renderer 配置。
- 玻璃材质需提供 `LightMode = "TransparentShadow"` 的 Pass。普通透明材质没有这个 Pass 时不会进入
  透射图：透射色与深度只能由该 Pass 输出。

## 组成

| 文件 | 作用 |
|---|---|
| `TransparentShadowFeature.cs` | RendererFeature + Raster Pass：收集 `layerMask` 内的 Renderer、计算光空间包围盒、渲染 RT、发布全局 |
| `TransparentShadowFunction.hlsl` | 接收端函数 `TransparentShadow_Transmission(positionWS)`；SSL 与测试地面共用 |

玻璃材质需要提供一个 `LightMode = "TransparentShadow"` 的 Pass（参考
`Assets/Mine/Shaders/Render/ColorfulWindow/ColoredGlass.shader`），输出
`float4(transmission.rgb, normalizedDepth)`。

## 数据与全局

| 全局 | 含义 |
|---|---|
| `_TransparentShadowTex` | RGB 透射色 + A 归一化光空间深度；背景白色 A=1 |
| `_TransparentShadowWorldToLight` | 主光 `worldToLocalMatrix`；接收者用它求 `pLS` |
| `_TransparentShadowParams` | xy = 包围盒中心，zw = 包围盒尺寸 |
| `_TransparentShadowDepth` | x = zMin，y = zMax，z = enabled，w = bias |

接收端：

```hlsl
pLS = mul(_TransparentShadowWorldToLight, float4(positionWS, 1)).xyz;
uv = (pLS.xy - center) / size + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;   // 自定义 clip 的 RT V 方向补偿
#endif
pointDepth = saturate((pLS.z - zMin) / (zMax - zMin));
glass = sample(map, uv);       // .a = glass depth, .rgb = transmission
transmission = pointDepth > glass.a + bias ? glass.rgb : 1;
```

- 点在玻璃前 → 白（日光）；
- 点在玻璃后（沿光方向 pLS.z 更大）→ 玻璃色；
- UV 越界 / 无玻璃背景 → 白。

透射图是**纯颜色 RT**（`DepthBits.None`），A 通道是**原始归一化光空间 z**，不是 reversed-Z clip depth；
创建光源相机/深度附件不在这条路径里。D3D 下 RT 的 V 方向与自定义 clip 不一致，接收端按
`UNITY_UV_STARTS_AT_TOP` 翻转 Y；否则玻璃深度梯度会上下翻，出现“一半漏光、一半近处无光”。

## 设置

| 字段 | 说明 |
|---|---|
| `layerMask` | 参与透射图的 Renderer 层；与 `TransparentWithShadow` 层对齐；0 = 关闭（Warn 一次） |
| `shaderTag` | 玻璃材质的 LightMode / pass 标签，默认 `TransparentShadow` |
| `resolution` | 光空间方形 RT 分辨率 |
| `padding` | 包围盒 XY 外扩，留白色边界 |
| `depthPadding` | 包围盒 Z 外扩 |
| `depthBias` | 归一化深度比较 bias |
| `passEvent` | 默认 `BeforeRenderingOpaques`，必须早于地面/不透明接收者采样；与 `layerMask` 同属 `Event` 组 |

## 当前边界

- **单层**：RT 只存一个颜色/深度；透射图 Pass 用 `ZTest Always`，重叠玻璃以最后绘制者为准。
  多层透射需要 deep/multi-layer 或按层拆分。
- **包围盒来自层内 Renderer 的 world bounds 并集**（`FindObjectsByType`），每帧重新计算；
  适合少量动态玻璃，不适合大量对象。
- **RendererList 使用主相机 culling 结果**：相机看不到的玻璃不会进入该相机的透射图。
- 分辨率是光空间 RT 分辨率，玻璃自身屏幕像素不受影响；光斑细节受该分辨率限制。
- 与 opaque shadow map 共存：shadow map 负责墙/窗洞可见性，本图只负责透射色与前后。
