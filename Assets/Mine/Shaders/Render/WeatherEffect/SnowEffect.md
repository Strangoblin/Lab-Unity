# SnowEffect — 雪天积雪

独立 shader `Render/SnowEffect`：朝上面积雪、雪面法线与雪面闪烁，叠加在同一材质上，不新增物体、不切材质。
2026-11-01 从合并版 `WeatherEffect` 拆出，只保留积雪相关的参数与代码；雨天湿身见 [RainEffect.md](RainEffect.md)，
两者共用的数学在 [WeatherFunction.hlsl](WeatherFunction.hlsl)，索引见 [WeatherEffect.md](WeatherEffect.md)。

## 参数

| 分组 | 参数 | 作用 |
|---|---|---|
| 基础 | `_BaseColor` / `_MainTex` / `_Roughness` / `_Metallic` | 干表面输入 |
| 噪声 | `_WeatherNoise` | 积雪边界噪声；物体空间 XZ / XY 双轴投影后按法线权重混合 |
| 积雪 | `_SnowAmount` | 总雪量，0 关闭 |
| 积雪 | `_SnowColor` | 雪面反照率，默认 (0.92, 0.95, 1.0) |
| 积雪 | `_SnowThreshold` / `_SnowSoftness` | 朝上阈值与噪声抖动 / 过渡宽度；默认 0.25 / 0.3 |
| 积雪 | `_SnowRoughness` | 雪面粗糙度，默认 0.85 |
| 积雪 | `_SnowNormalBlend` | 雪面法线向世界上方混合的比例 |
| 积雪 | `_SnowNoiseScale` | 物体空间噪声频率；默认 1.0 |
| 积雪 | `_SnowSparkle` / `_SnowSparkleDensity` / `_SnowSparkleScale` | 闪烁强度、格点密度与尺度 |

## 合成顺序

```
干表面输入
  → 积雪遮罩：世界朝上分量 + 噪声抖动 → smoothstep(阈值 ± 过渡) × 总雪量
  → albedo / roughness / metallic 按遮罩覆盖，雪面 albedo 用同一噪声做轻微明暗起伏
  → 法线向世界上方混合
  → 基础光照 Lambert + GGX
  → 雪面闪烁：世界 XZ 格点 Hash 阈值 × NdotH 收束
  → 环境反射
  → 雾
```

积雪按「最外层表面」处理：先覆盖表面参数再进入同一套光照，因此环境光与高光都自然落在雪面上；
雪面法线向世界上方混合，避免雪沿着身体曲面出现与朝上程度不符的明暗带。

## 投影与空间

- 积雪噪声在**物体空间**采样：XZ 与 XY 两个投影按 `|normalOS.y|` / `|normalOS.z|` 加权平均，
  图案跟随网格，移动或旋转物体时不再相对表面漂移；早先世界空间 XZ 投影会在竖直面上被拉成竖条。
- 闪烁格点取世界 XZ，只对朝上的雪面成立，因此再乘 `saturate(normalWS.y²)`：
  侧壁上的格点会被拉成长竖条，压掉后才能只在上表面看到细闪。

## 调参经验

- 闪烁只乘稳定覆盖区域（`smoothstep(0.5, 0.9, snowMask)`）：过渡带里法线被混向天空，
  `NdotH` 会短暂抬高，若继续参与闪烁会在雪缘出现一圈细碎亮点。
- `_SnowNoiseScale` 推到 5 时，256² 噪声被严重缩小采样，雪缘出现网格状斑点；
  2.0–2.5 之间边界既有起伏又稳定。
- 雪面接近纯白，强方向光下很容易过曝；测试场景把方向光压到 1.05，正式使用需要配合曝光 / 后处理。
- `_SnowSoftness` 是遮罩过渡宽度，也决定了噪声抖动幅度：给它 0.3 而阈值只有 0.15 时，
  噪声能把遮罩顶过阈值，竖直面会长出斑块状积雪。

## 依据

- Ohlsson & Seipel, *Snow Accumulation*（NVIDIA 样例）：坡度系数 `N·up` 加噪声抖动，避免规则雪线。
  <http://download.nvidia.com/developer/SDK/Individual_Samples/DEMOS/Direct3D9/src/SnowAccumulation/Docs/SnowAccumulation.pdf>
- Godot Shaders, *Snow-Covered Surface*：`smoothstep(dot(worldNormal, up))` 遮罩并混合法线的最小实现。
  <https://godotshaders.com/shader/snow-covered-surface/>
- NVIDIA, *Real-time Rendering of Procedural Multiscale Materials*：雪面随机微表面闪烁的降级依据。
  <https://research.nvidia.com/sites/default/files/pubs/2016-02_Real-time-Rendering-of/ProceduralMultiscaleMaterialsFinal.pdf>

## 边界与近似

- 积雪遮罩只用物体空间噪声与朝上分量，没有天空可见度 / 遮挡图；垂直面可能出现少量薄雪。
- 雪面是纯反照率替换，没有厚度、位移或雪檐；真正的轮廓堆积需要几何或位移方案。
- 雪面闪烁是 Hash 格点阈值，不是随机微表面统计；近看呈离散点亮，远看可能闪烁。
- ShadowCaster / DepthOnly 不含积雪的几何差异。
- 独立 shader 的光照是 Lambert + GGX + 环境光，仅用于效果验证；并入 PBRToon 后这一段换成 Toon / SSS / Fabric 合成。

## 测试场景

`Test/SnowEffectTest.unity`：四个球并排——Dry / Snow Light / Snow Full / Snow Sparkle，
外加一块全覆雪的地面（观察大面积积雪与闪烁）。材质在 `Test/Materials/Snow_*.mat`，
噪声 `Test/Textures/WeatherNoise.asset`。打开场景直接调材质参数即可对照四态。
