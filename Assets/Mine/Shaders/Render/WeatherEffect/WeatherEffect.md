# WeatherEffect — 表面天气层（索引）

天气层已按雨 / 雪拆成两个独立 shader，共用同一份数学库：

| 文件 | 内容 |
|---|---|
| [RainEffect.shader](RainEffect.shader) / [RainEffect.md](RainEffect.md) | 雨天湿身：水膜、水滴、水痕、雨滴涟漪 |
| [SnowEffect.shader](SnowEffect.shader) / [SnowEffect.md](SnowEffect.md) | 雪天积雪：朝上覆盖、雪面法线、雪面闪烁 |
| [WeatherFunction.hlsl](WeatherFunction.hlsl) | 两个 shader 共用的函数，零 CBUFFER；调用方先 include Core.hlsl |
| `Test/RainEffectTest.unity` / `Test/SnowEffectTest.unity` | 状态对照场景，材质在 `Test/Materials/` |

2026-11-01 按用户要求先把天气效果从 `PBRToon` 的 `ENABLE_WEATHER` 变体分离成独立 shader 验证，
随后又拆成雨天与雪天两套，便于各自单独调参；确认后再拆分并入 `PBRToon`。

## 为什么拆分

- 湿身与积雪在同一材质里是互斥的天气状态，合并版要同时携带 30 多个参数，调雨天时积雪参数始终占位。
- 两者的合成顺序不同：湿身要在光照前改写 albedo / roughness / 法线并追加水膜高光瓣；
  积雪是「最外层表面」覆盖，覆盖后走同一套光照。分开后每个 shader 只保留自己那条路径。
- 共用部分（撤 albedo、投影、噪声）留在 `WeatherFunction.hlsl`，两边 include 同一份实现，不会各写一套。

## 共用约定

- 所有天气采样都在**物体空间**，图案跟随网格，移动或旋转物体时不相对表面滑动。
- 噪声贴图必须在 shader 的 `Properties` 中声明；只 declare 不声明时 `material.SetTexture` 会被静默忽略，
  绑定到默认纹理，表现为遮罩恒为常数（拆雨天时踩过这个坑，水痕一直不出现）。
- 物体空间法线主轴投影（`Weather_ProjectAxis`）供雨天与积雪共用，避免竖直面上的纹理被拉伸；
  主轴切换的 45° 带上会有图案跳变。
- 两个 shader 都只有 Forward / ShadowCaster / DepthOnly 三个 Pass，天气不改变几何。

## 边界

- 天气层是表面着色近似：没有湿身液体体积、雪层厚度或位移。
- 独立 shader 的光照是 Lambert + GGX + 环境光，仅用于效果验证；并入 PBRToon 后换成 Toon / SSS / Fabric 合成。
- 目前只作用于不透明表面，没有透明 / 半透明物体的湿身与积雪。

## 离线截图

`tmp/sessions/2026-11-01-weather-split/output/`：`rain-states.png`、`closeup-{wet,droplets,flow,ripples,full,puddle}.png`、
`snow-states.png`、`snow-close-{light,full,sparkle,ground}.png`。
