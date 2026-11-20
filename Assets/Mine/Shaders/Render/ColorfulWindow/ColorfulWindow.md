# ColorfulWindow — 管线级透明透射彩窗

从主光视角渲染玻璃层的 RGB 透射 + 光空间深度，地面与 SSL 通过深度比较判断玻璃前后：
玻璃前/无玻璃 = 普通阳光，玻璃后 = 彩窗颜色。主光 Cookie 与单窗平面 mask 方案已退役。

模块在 `Assets/Mine/Special/Renderer/TransparentShadow/`（2026-11-20 由 `ColoredShadow` 改名），
契约见其 README。

## 文件

| 文件 | 用途 |
|---|---|
| `ColoredGlass.shader` | 玻璃材质：普通透明 Forward（Blend + `ZWrite Off`，正常距离排序，双面）；`TransparentShadow` Pass 输出透射色 + 归一化光空间深度 |
| `StainedGlassPane.asset` | 512² 窗面图案（5×7 色块 + 黑铅条） |
| `ColorfulWindowPane.mat` | 玻璃材质（`Render/ColoredGlass` + Pane 纹理） |
| `ColorfulWindowGround.shader` | 测试地面/墙体接收端：主光阴影 × `TransparentShadow_Transmission`；带 ShadowCaster。当前演示不再使用，保留作接收函数参考 |

旧 Cookie 方案的 `StainedGlassCookie.asset` 与 `ColorfulWindowLight.cs` 已于 2026-11-19 删除。
独立测试场景 `ColorfulWindowTest.unity`、测试 Renderer `ColorfulWindowTestRenderer.asset` 与地面/墙体材质已于 2026-11-20 删除；
演示并入 `Assets/Scenes/RenderScene.unity`，由 `PC_Renderer` 承载。

## 层级约定

玻璃放在命名为 **`TransparentWithShadow`** 的层（当前项目 Layer 8）。Feature 的 `layerMask`
直接按索引选层，由使用者手动对齐到该层（`PC_Renderer` 当前 `m_Bits = 256`）。

普通渲染仍走普通管线，Feature 只负责额外的彩色阴影：

1. 场景相机的 Culling Mask 必须包含该层；
2. 所用 Renderer 的 Opaque / Transparent Layer Mask 必须包含该层（URP 常规设置，Feature 不改）；
3. 玻璃材质提供 `TransparentShadow` Pass。

可见窗面走普通透明队列，与透射图 Pass 相互独立。普通透明材质没有 `TransparentShadow` Pass
时不会进入透射图——透射色与深度只能由该 Pass 输出。

## 数据流

```text
TransparentShadowFeature（BeforeRenderingOpaques）
  layerMask = TransparentWithShadow
  → 收集该层 Renderer（当前为 `RenderScene` 里的 `Plane (8)`）
  → 并集 world bounds 投到主光光空间
  → 光空间 RT：RGB = 玻璃透射色，A = 归一化光空间深度；背景白 A=1
  → 发布 _TransparentShadowTex / _TransparentShadowWorldToLight / _TransparentShadowParams / _TransparentShadowDepth

地面/墙 Frag：
  shadow = GetMainLight(shadowCoord).shadowAttenuation
  mainLight.color *= TransparentShadow_Transmission(positionWS)
  direct = albedo × light.color × NdotL × shadow

SSL 每步：
  if (shadow <= 0) continue
  density += stepSize × shadow × TransparentShadow_Transmission(samplePos)
```

接收端深度比较：

```text
pLS = mul(_TransparentShadowWorldToLight, positionWS)
uv  = (pLS.xy - center) / size + 0.5
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1 - uv.y          // 自定义 clip 的 RT V 方向补偿
#endif
d   = (pLS.z - zMin) / (zMax - zMin)
glass = sample(map, uv)              // .rgb 透射色，.a 玻璃深度
transmission = d > glass.a + bias ? glass.rgb : 1
```

- 玻璃前 / UV 越界 / 无玻璃背景 → 白；
- 玻璃后（沿光方向 pLS.z 更大）→ 玻璃色；
- 彩色图是纯颜色 RT（`DepthBits.None`），A 是原始光空间 z，不是 reversed-Z；没有光源相机。
- 不做 Y 翻转会出现玻璃深度梯度上下翻，表现为“一半漏光、一半近处无光”；`1 - d` 只是镜像深度，颜色图仍会上下翻。
- 不再需要 `_ColorfulWindowPlane` 单平面判断。

## 场景

演示已并入 `Assets/Scenes/RenderScene.unity`：

- Layer 8 的 `Plane (8)` 使用 `ColorfulWindowPane.mat`；`PC_Renderer` 挂 `TransparentShadowFeature`（`layerMask` = Layer 8）与 `SSLFeature`。
- 主光 `cookie = null`；玻璃颜色只进透射图，不投 opaque 阴影。
- 原密闭房间测试场景 `ColorfulWindowTest.unity`（背墙 z = 6、窗洞 [-1.5,1.5]×[1.5,4.5]、室内 `Main Camera` / 室外 `Outside Camera`）与 `ColorfulWindowTestRenderer` 已于 2026-11-20 删除，数据保留在 Git 历史与下方验证输出中。

## 验证

`tmp/sessions/2026-11-15-colorfulwindow-cookie-01/output/`：

- `coloredshadow-inside.png` — 室内：彩色地面光斑 + 彩色体积光柱。
- `coloredshadow-outside.png` — 室外：墙/地面为白色阳光，无彩色光柱穿墙。
- `coloredshadow-flip-inside.png` / `coloredshadow-flip-outside.png` — Y 翻转后静态室内/室外。
- `coloredshadow-flip-dynamic.png` — 玻璃平移 + 12° 旋转后：彩色图案随玻璃移动；白色带是**窗洞未被玻璃覆盖**的部分（透射图背景白 = 无玻璃），不是玻璃漏光。
- `ssl-quality-low/medium/high.png` — SSL Debug=Light 三档均值 10.14 / 10.63 / 10.63。

## 边界

- **单层**：透射图只存一个颜色/深度；玻璃 Pass `ZTest Always`，重叠玻璃以最后绘制者为准。多层需要 deep/multi-layer。
- **包围盒来自层内 Renderer 的 world bounds 并集**，每帧 `FindObjectsByType`；适合少量动态玻璃。
- **RendererList 使用相机 culling**：相机看不到的玻璃不会进入该相机透射图。
- 光空间 RT 分辨率 1024；玻璃自身屏幕像素不受影响，但地面/体积里的透射细节受该分辨率限制。
- 生产地面（PBRToon / `LightFunction.hlsl`）尚未接入 `TransparentShadow_Transmission`；当前接收端只有 `ColorfulWindowGround.shader`（测试参考）与 SSL。
- 普通玻璃要进入透射图仍需提供 `TransparentShadow` Pass；仅靠层无法凭空产生透射色与深度。
- 旧 Cookie/平面方案资产已删除（2026-11-19），实现仍在 Git 历史；当前 Shader 不再读取 `_LIGHT_COOKIES` / `_ColorfulWindowPlane`。
