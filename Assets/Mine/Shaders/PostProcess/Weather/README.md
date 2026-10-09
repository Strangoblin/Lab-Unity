# Weather

单预设天气管线：Rain、Sand、Snow 共用 Shader、噪声和调度入口，粒子与氛围模块分别调节。Rain 保留雨滴折射，已移除旧 Rainy 的 blur。

## 接入

在 URP Renderer 添加 `WeatherFeature`，指定：

- Shader：`PostProcess/Weather`（本目录 `Weather.shader`）。
- Noise Texture：`Assets/Textures/Noise2DRG.asset`，三天气共用原 RG 噪声。
- Profile：`Presets/Rain.asset`、`Sand.asset` 或 `Snow.asset`。

`Assets/Settings/PC_Renderer.asset` 已接入 Weather；天气选择与启用状态以 Renderer Inspector 的当前配置为准，旧 DebugOutput 天气入口关闭。Feature 自持运行材质，无需创建或编辑天气 Material。切换 `settings.profile` 即切换天气；运行时需要独立调参时先 `Instantiate` Profile，并在使用结束时释放副本。

通过 Create → Mine → Weather → Profile 创建新预设。Profile 是美术参数来源；Feature 只配置资源、Scene View 显示和 Debug。`Resolve()` 返回经过范围校验的值快照，不回写 Profile。

## 模块与参数

| 分组 | 参数 | 作用与关闭方式 |
|---|---|---|
| Common | Kind / Randomness / CycleRate | 单一天气、粒子随机程度、循环速度；CycleRate=0 冻结粒子，不关闭氛围 |
| Particles | Shape / Opacity | 雨雪形状阈值、粒子贡献；Opacity=0 关闭，雨雪 Shape=0 也关闭 |
| Particles | RefractionScale | 雨滴折射倍率，默认1；0关闭雨滴，沙雪忽略 |
| Lens | Strength | 水汽、积尘或霜边；0关闭 |
| Lens | EdgeBaseWidth / EdgeNoiseWidth | 雪霜边宽度，两者均0关闭；逐像素派生宽度0时返回零遮罩 |
| Fog | Strength | 雾强度；0关闭且不请求天气深度 |
| Fog | BaseDistance / NoiseDistance / NoiseScrollX | 雪雾基础距离、噪声距离增量和水平滚动，分别调节；基础距离至少0.01 |
| Tone | Strength / Tint | 去饱和与冷暖颜色倍率；Strength=0关闭，白 Tint 仍保留去饱和 |
| Technical | AlphaPolicy | PreserveInput 保留输入Alpha；LegacySnow 保留旧雪霜边／雾的Alpha合成 |

运行脚本赋值同样经 `Resolve()` 处理 NaN、Infinity、非法枚举及越界值。常规强度范围0–1；CycleRate 0–2，RefractionScale 0–4，Tint RGB 0–4。Shader内部参数由Feature绑定。

## 迁移预设

预设从旧 Material 的实际值迁移，旧 Randomness 均1、Wind均0.5、冷暖强度均1；CycleRate=2×旧Wind。

| 预设 | Shape / Opacity | Lens / Fog | Tone Tint | Alpha |
|---|---|---|---|---|
| Rain | 0.5 / 0.5 | 0.5 / 0.5 | (0.88, 0.96, 1.05) | PreserveInput |
| Sand | 1 / 1 | 1 / 1 | (1.08, 0.96, 0.82) | PreserveInput |
| Snow | 0.3 / 0.3 | 0.3 / 0.3 | (0.88, 0.96, 1.05) | LegacySnow |

Snow 霜边基础宽度0.03、噪声宽度0.3，雾基础距离50、噪声增量100、滚动0.5。周期、霜边、雾和调色在Weather中分别调节。

合成顺序：Rain 雨滴折射 → 水汽与高度雾联合染色 → 冷调色；Sand 粒子叠色 → 高度雾 → 积尘 → 暖调色；Snow 粒子叠色 → 霜边 → 眼空间深度雾 → 冷调色。没有滤波函数、参数或滤波 Pass。

## 渲染与诊断

执行点固定为 `AfterRenderingPostProcessing`。活动阶段最多两个天气 draw：`Weather.Particles`、`Weather.Atmosphere`；仅单阶段活动时一个，全关且Debug=Off时不入队。输入仅在雾需要时请求 Depth，不请求 Color／Normal／Motion。URP已有深度与最终输出成本另计。

支持最终输出的 Game Base Camera；Scene View 可关闭。Overlay、Preview、Reflection 与非末端 Camera Stack Base 跳过；首版不支持 Camera Stack 上的天气合成。相机颜色、深度缺失或资源无效时输出一次诊断；缺深度只关闭当前快照中的雾，其它模块继续。

Debug：Particles／Refraction 只运行粒子阶段，Lens／Fog 只运行氛围阶段；Fog 请求深度，其它单模块视图不请求深度。Final 使用正常合成路径。Debug强制绘制计入诊断成本。

雨沙保留旧透视／正交高度雾；Snow保留旧眼空间深度模型，正交画面的正确解算留待后续。雾依赖不透明深度，透明前景通常没有独立的深度遮挡。源RT尺寸和采样区域由RTHandle读取，避免以屏幕分辨率替代实际渲染尺寸。XR设备尚未验证。

## 保留范围

原 Rainy、Sandy、Snowy 可导入目录与材质已移除；历史对照快照保存在 `Validation~/Legacy/`，由验证脚本在内存中构造，不进入运行时资产。Noise2DRG 的像素、GUID及色彩空间保持原样。Sand原coverage计算未接入输出，Weather保留这一行为；原Snow Alpha与Rain折射长宽比响应也保留。天气混合、过渡、性能档、Volume与世界空间天气不在首版范围。

验证记录见同目录 `VALIDATION.md`。
