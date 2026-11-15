# 彩窗彩色主光 Cookie + SSL 单模式 HG 体积光（2026-11-15）

把彩窗写成**主方向光的 RGB Cookie**，地面与 SSL 共用同一份 URP 主光光照数据；阴影图仍只做可见性判断。测试入口在 `Assets/Mine/Shaders/Render/ColorfulWindow/ColorfulWindowTest.unity`。

## 已验证的主光 Cookie 事实（URP 17.3）

- `Light.cookie` 可以是彩色 2D 纹理：`GetLightCookieShaderFormat` 对非单通道格式返回 `RGB`，`SampleMainLightCookie` 返回 `.rgb`。单通道 R8 会被当灰度（`RED`），Alpha8 走 `.aaa`。
- 表面 Shader 必须用三参数重载 `GetMainLight(shadowCoord, positionWS, shadowMask)`；URP 只在这个重载里执行 `light.color *= SampleMainLightCookie(positionWS)`。两参数 `GetMainLight(shadowCoord)` 不采样 Cookie。
- Cookie UV：`pLS = light.transform.InverseTransformPoint(positionWS)`，`uv = (pLS.xy - lightCookieOffset) / lightCookieSize + 0.5`。`lightCookieSize` 默认 `(1,1)` 世界单位，必须按窗的投影包围盒设置；`lightCookieOffset` 是光空间包围盒中心。
- 主光 Cookie 是沿光方向的正交投影（`s_DirLightProj`），不是窗口透视；适合平行光 + 平面窗。
- 主光 Cookie 采样传 `URP_TEXTURE_WRAP_MODE_NONE`，越界行为由纹理自身 wrap 决定；单窗用 Clamp + 黑边。
- 运行时把程序化 `Texture2D` 资产（RGBA32、sRGB）赋给 `Light.cookie` 可用，不要求导入类型为 Cookie；URP 只读 `graphicsFormat` / `dimension`。

## SSL 改动（2026-11-15 后续）

- **单模式**：移除 `SSLMode`、`RAY3D_Fog`、`RBR2D` 与全部模式关键字；只剩 HG `Light` 路径。`Frag_RBR2D`、`GetLightFocusUV`、Fog 分支删除；Mix 固定为 `main + ssl`。
- **质量亮度一致**：旧实现 `stepSize = rayLen/maxSteps` 后又 `density /= maxSteps`，完全照亮射线密度 ∝ `1/maxSteps`，Low(16) 是 Medium(32) 的 2 倍。改为 `density * (phase / 32.0)`（`SSL_REFERENCE_STEPS`），质量只改采样精度/模糊链，不改亮度。三档 SSL Debug 图显示均值 10.14 / 10.63 / 10.63；Low 的约 4.6% 差异来自 16 步采样与无模糊链。
- **相位提升**：HG 相位每像素算一次，循环外乘到累积密度，不再每步重复计算。
- **阴影跳过**：循环内先采主光阴影，`shadow <= 0` 时 `continue`，不采样 Cookie、不累积。
- **窗平面限制**：新增全局 `_ColorfulWindowPlane` / `_ColorfulWindowMask`；`SampleLightColor` 在平面外侧直接返回白色，避免 2D Cookie 把向阳面也染色。无组件时 mask=0，行为不变。
- `SSL_LIGHT` 的累积是 `float3 density = stepSize * shadow * cookieColor`；最终 `density * phase / 32 * _Intensity * mainLight.color`。
- 每步 Cookie 用 `SAMPLE_TEXTURE2D_LOD(..., 0)` 直接读 `_MainLightCookieTexture`，避免隐式导数在变长循环中的警告。
- Raymarch Pass 保留 `#pragma multi_compile_fragment _ _LIGHT_COOKIES`；无 Cookie 时返回白色。
- 已编译 Pass 0–3（含与不含 `_LIGHT_COOKIES`），`ShaderUtil` 无消息。

## Cookie Clamp 伪阴影（2026-11-15 后续）

- **现象**：房间侧/室外部分地面没有阳光，看起来像整片 shadow map 阴影；关掉主光 shadows 后暗区不变。
- **根因**：主光 Cookie 使用纹理自身 Clamp。Cookie 边缘是窗格黑边/铅条；采样 UV 越界时重复边框黑色，
  `mainLight.color` 变 0，看起来像阴影。**不是 shadow map**，也不是 SSL（SSL=0 时仍存在）。
- **修复**：`lightCookieSize` 放大 1.25× 留出白色外边；SSL 与测试地面在 Cookie UV 越界时直接返回白色。
- **证据**：暗点 `(8,0,0)` / `(0,0,-8)` 的 Cookie 从 `(0,0,0)` 变 `(1,1,1)`；
  `overview-sun-margin.png` 建筑周围地面全亮；`overview-shadow-margin.png` 剩下的暗区是建筑真实阴影。
- `overview-sun-noshadow-margin.png` 是中途切 shadows 的诊断图，关键字/纹理状态不一致，不作参考。

## 测试场景与渲染器隔离

- `ColorfulWindowTestRenderer.asset`：新建 Forward+ `UniversalRendererData`，`ResourceReloader.ReloadAllNullIn` 补默认资源，只挂 `SSLFeature`（HG Light，quality Medium，intensity 3，sslScale 0.2，maxDistance 20，passEvent 550）。
- 测试 Renderer 通过 `PC_RPAsset.m_RendererDataList` 追加为 index 2；测试相机 `UniversalAdditionalCameraData.SetRenderer(2)`。**用户选择保留 `m_DefaultRendererIndex = 2`**，其他场景默认也走测试 Renderer（只含 SSL）。
- 场景：背墙 z=6、窗洞 [-1.5,1.5]×[1.5,4.5] 由 4 个 Cube 留出；窗面 Quad `ShadowCastingMode.Off`，避免挡光；墙体投射阴影。地面使用同目录 `ColorfulWindowGround.shader` 三参数 `GetMainLight` 读 Cookie。
- 主光方向改为 `(0,-0.8,-0.6)`：地面光斑与窗面 x 对齐、沿 z 向房间内偏移，是竖直窗面的物理投影。
- `StainedGlassPane.asset` 保存整洁 5×7 窗格；`StainedGlassCookie.asset` 由窗面图案沿光方向正交投影生成（Cookie 像素与窗平面求交，命中窗洞才写颜色），**窗格投影之外为白色**（普通阳光）。测试中 `lightCookieSize=(3.00,1.80)`、`lightCookieOffset=(0,0)`。
- `ColorfulWindowLight` 在 Sun 上发布 `_ColorfulWindowPlane`（房间侧法线 (0,0,-1)、点 (0,3,6)）与 `_ColorfulWindowMask=1`；SSL/地面/墙体在平面外侧白色。墙与地面共用测试 Shader（`ZWrite On` + ShadowCaster）。
- 场景有室内 `Main Camera` 与默认关闭的 `Outside Camera`；室外相机验证向阳面是白色阳光/SSL，且室内彩色光柱不会穿过墙体。

## 证据

- `tmp/sessions/2026-11-15-colorfulwindow-cookie-01/output/colorfulwindow-inside-plane.png`：室内，地面投影与窗面图案对应 + 彩色体积光。
- `colorfulwindow-outside.png`：室外，向阳面白色阳光/SSL，未见彩色光柱穿过墙体。
- `ssl-quality-low/medium/high.png`：SSL Debug=Light 三档对照，均值 10.14 / 10.63 / 10.63。
- 截图由 `SingleCameraRequest` 离屏 960×540 获取；显示 PNG，不是线性 HDR 回读。

## 未验证 / 后续

- 生产地面 Shader（`LightFunction.hlsl` 的 `MainLight()`、PBRToon Frag）仍是两参数重载，不会读 Cookie；本次只在测试地面验证。接入生产需改共享入口并补 `_LIGHT_COOKIES` 变体。
- 多窗、多方向、运行时动态 Cookie（RenderTexture 赋 `Light.cookie`）未实测；当前窗平面限制是单平面近似，多窗/曲面玻璃需要深度感知彩色阴影图。
- SSL 只使用相机深度作为射线终点，不做逐点场景深度遮挡；建筑/地形必须不透明、`ZWrite On` 并进入相机深度纹理，透明或 `ZWrite Off` 材质会穿透。
- 其他平台（Metal/移动）、XR、动态分辨率、性能开销未测；体积光观感仍需用户在 Editor 判断。
- 三档亮度仍有 Low 约 4.6% 的采样/模糊链差异，不是旧的 2× 步数缩放；如需完全一致需另做能量校准。
- 测试 Renderer 的 APV 调试资源为空（新建资产默认），不影响本测试的非 APV 路径。
