# ColorfulWindow — 彩窗彩色主光 Cookie

把彩窗作为**主平行光的彩色 Cookie** 写入 URP 光照数据：地面通过 `GetMainLight(..., positionWS, ...)` 得到彩色直接光，SSL 在体积步进的每一步读取同一个 `SampleMainLightCookie`，阴影图仍只负责可见性。

## 文件

| 文件 | 用途 |
|---|---|
| `StainedGlassPane.asset` | 512² sRGB RGBA32 窗面图案（5×7 色块 + 黑铅条），只用于窗面参照 |
| `StainedGlassCookie.asset` | 同一图案沿主光方向正交投影到光空间；`Light.cookie` 使用它 |
| `ColorfulWindowGround.shader` | 测试地面；Forward 使用三参数 `GetMainLight`，变体含 `_LIGHT_COOKIES` |
| `ColorfulWindowGround.mat` | 地面材质（白灰底色） |
| `ColorfulWindowWall.mat` | 房间墙体（测试地面 Shader，深灰，带 ShadowCaster） |
| `ColorfulWindowPane.mat` | 窗面参照（URP Unlit + `StainedGlassPane`，双面） |
| `ColorfulWindowLight.cs` | 发布 `_ColorfulWindowPlane` / `_ColorfulWindowMask`，把彩窗色限制在房间侧 |
| `ColorfulWindowTestRenderer.asset` | 独立 Forward+ Renderer，只挂 `SSLFeature`（HG 体积光） |
| `ColorfulWindowTest.unity` | 密闭房间 + 窗洞 + 平行光 + 室内/室外相机测试场景 |

## 光照数据写入

```csharp
sun.type = LightType.Directional;
sun.cookie = stainedGlassCookie;              // RGB = 透射色，铅条/框为黑
var data = sun.GetUniversalAdditionalLightData();
data.lightCookieSize   = new Vector2(width, height); // 光空间世界尺寸
data.lightCookieOffset = new Vector2(cx, cy);        // 窗中心在光空间 XY
```

URP 每帧发布 `_MainLightCookieTexture` / `_MainLightWorldToLight` / `_MainLightCookieTextureFormat`，
有 Cookie 时开启 `_LIGHT_COOKIES`。主光 Cookie 采样映射：

```text
pLS = light.transform.InverseTransformPoint(positionWS)
uv  = (pLS.xy - lightCookieOffset) / lightCookieSize + 0.5
```

场景把窗洞四角投到光空间取包围盒；主光 Cookie 是沿光方向的正交投影，因此地面和体积读到同一份颜色。

Cookie 背景为白色：窗格投影之外仍是普通阳光。`ColorfulWindowLight` 再发布窗平面（房间侧法线），
SSL 与测试地面在平面外侧直接返回白色，避免 2D 投影把向阳面也染彩：

```text
_ColorfulWindowPlane.xyz = 房间侧法线
_ColorfulWindowPlane.w   = -dot(normal, windowCenter)
_ColorfulWindowMask      = 1
side = dot(positionWS, normal) + w   // >0 房间侧，<0 向阳侧
```

Cookie 包围盒放大 1.25× 留出白色外边；SSL 与测试地面在 Cookie UV 越界时也直接返回白色。
否则主光 Cookie 的 Clamp 会把窗格边缘的黑像素重复到包围盒外，形成大片“伪阴影”——不是
shadow map 的阴影。测试场景在没有主光 Cookie 的向阳侧与包围盒外都应保持白色阳光。

## 场景结构

- 背墙 z = 6，窗洞 x ∈ [-1.5, 1.5]、y ∈ [1.5, 4.5]，四周由 4 个 Cube 留洞，墙体投射阴影。
- 窗面 Quad 不投影（`ShadowCastingMode.Off`），避免把整束 Cookie 光挡掉；它只作可见参照。
- 主光方向 `(0, -0.8, -0.6)`：窗洞在地面的投影是矩形，x 与窗对齐、沿 z 向房间内偏移；这是平行光穿过竖直窗面的物理投影。
- `StainedGlassCookie.asset` 由窗面图案沿光方向正交投影生成：每个 Cookie 像素与窗平面求交，命中窗洞才写窗面颜色。因此地面光斑与窗面图案在对应光线上颜色一致；矩形在 z 向被压缩是投影本身，不是错位。
- 相机 Renderer Index = 2（`ColorfulWindowTestRenderer`）；`PC_RPAsset` 的默认 Renderer 目前也是 2（用户保留），其他场景默认走测试 Renderer。
- 房间只留一个彩窗洞口；彩窗色被 `_ColorfulWindowPlane` 限制在房间侧。室内只有**彩色 SSL 光柱**
  （进入房间的光全部经过彩窗）；室外是白色阳光与白色 SSL（没有遮挡变化时表现为均匀亮度，不是可见光柱）。
- `Outside Camera` 默认关闭，位于房间外朝墙，用于检查室外阳光/SSL；`Main Camera` 在室内看彩色光柱。
- 墙体/地面使用同一测试 Shader，均 `ZWrite On` 且带 ShadowCaster；SSL 的射线终点来自相机深度，
  因此室外相机看不到室内的彩色光柱。透明或 `ZWrite Off` 的材质不会进入深度纹理，SSL 会穿过去。

## 验证

在 Editor 打开 `ColorfulWindowTest.unity` 直接观察；当前任务截图在
`tmp/sessions/2026-11-15-colorfulwindow-cookie-01/output/`：

- `colorfulwindow-inside-margin.png` — 室内：地面投影与窗面图案对应，彩色体积光连接窗洞与地面。
- `colorfulwindow-outside-margin.png` — 室外：墙面/地面受白色阳光（墙面本身反照率低，SSL=0 时会显得暗）。
- `overview-sun-margin.png` — 俯瞰向阳侧：建筑周围地面全亮，Cookie Clamp 造成的暗区已消失。
- `overview-shadow-margin.png` — 俯瞰背光侧：剩下的暗区是建筑投下的**真实阴影**，不是 Cookie 黑边。
- `overview-sun-noshadow-margin.png` — 中途关闭 shadows 的诊断图，关键字/纹理状态不一致，不作通过证据。
- `ssl-quality-low.png` / `ssl-quality-medium.png` / `ssl-quality-high.png` — SSL Debug=Light 的三档对照；
  显示空间均值约 10.14 / 10.63 / 10.63，质量不再按步数缩放亮度（Low 的约 4.6% 差异来自 16 步采样与无模糊链）。
- 场景没有 skybox，画面中“上方”的黑色是相机背景，不是阴影；需要天空/太阳观感时应另加天空盒或背景几何。

已编译：SSL Pass 0–3（含与不含 `_LIGHT_COOKIES`）与地面 Shader 的 Cookie/阴影变体，`ShaderUtil` 无消息。

## 边界

- 只支持**一盏主方向光**的 Cookie；多个窗、不同方向或多个图案需要拆分自定义光数据块。
- Cookie 是正交投影，适合平行光 + 平面窗；近窗点/聚光灯的透视图案需要 Spot Cookie 或自定义投影。
- 生产地面若使用 `LightFunction.hlsl` / PBRToon，需要同样改成三参数 `GetMainLight` 或显式
  `SampleMainLightCookie`，并加 `_LIGHT_COOKIES` 变体；本次只提供测试地面 Shader。
- 窗洞阴影决定光斑形状，Cookie 决定颜色；细铅条应画进 Cookie，不要依赖阴影图分辨率。
