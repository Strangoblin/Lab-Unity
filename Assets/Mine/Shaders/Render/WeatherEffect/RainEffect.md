# RainEffect — 雨天湿身

独立 shader `Render/RainEffect`：水膜、水滴、水痕与雨滴涟漪叠加在同一材质上，不新增物体、不切材质。
2026-11-01 从合并版 `WeatherEffect` 拆出，只保留雨天相关的参数与代码；积雪见 [SnowEffect.md](SnowEffect.md)，
两者共用的数学在 [WeatherFunction.hlsl](WeatherFunction.hlsl)，索引见 [WeatherEffect.md](WeatherEffect.md)。

## 参数

| 分组 | 参数 | 作用 |
|---|---|---|
| 基础 | `_BaseColor` / `_MainTex` / `_Roughness` / `_Metallic` | 干表面输入 |
| 遮罩 | `_WetMask` | 湿度遮罩，乘进 `_Wetness`；用来把同一材质做出局部干湿 |
| 噪声 | `_WeatherNoise` | 湿膜分布与水流梯度噪声（256²、可平铺、带 mip）；**必须在 Properties 中声明**，否则 `SetTexture` 会被静默忽略 |
| 湿身 | `_Wetness` | 总湿度，0 关闭 |
| 湿身 | `_WetDarken` / `_WetSaturation` | albedo 压暗与提饱和；默认 0.45 / 1.25 |
| 湿身 | `_WetRoughness` / `_WetSpecular` | 粗糙度收敛目标与水膜高光强度；默认 0.15 / 1.0 |
| 湿身 | `_WetNormalFlatten` | 法线向几何法线收拢的比例，削弱水膜下的细起伏 |
| 湿身 | `_WetVariation` / `_WetVariationScale` | 低频噪声把均匀湿度打散成水膜与半干斑块；默认 0.18 / 0.5 |
| 湿身 | `_WaterSheen` | 宽瓣柔和高光，补湿面整体的镜面感；默认 0.35 |
| 水滴 | `_DropletAmount` | 珠格出现比例，0 关闭；默认 0.5 |
| 水滴 | `_DropletScale` / `_DropletNormal` | 珠格密度与圆顶法线强度；默认 24 / 0.85 |
| 水滴 | `_DropletSpecular` / `_DropletGloss` | 珠面高光加权与锐利闪点；默认 2.0 / 0.6 |
| 水滴 | `_DropletDarken` | 珠内 albedo 压暗，模拟折射后的吸收；默认 0.15 |
| 水痕 | `_FlowAmount` | 水痕强度，0 关闭 |
| 水痕 | `_FlowScale` / `_FlowSpeed` | 条纹频率与沿物体 −Y 的滚动速度；默认 2.2 / 0.4 |
| 水痕 | `_FlowNormal` / `_FlowSpecular` | 条纹法线扰动与高光加权；默认 0.45 / 0.6 |
| 吸水 | `_Absorbency` | 布料吸水强度，0 关闭；吸收区内水滴与水痕细节退回水膜法线，压暗与光滑度保留 |
| 吸水 | `_AbsorbScale` / `_AbsorbSpeed` | 吸收块密度与呼吸速度；默认 10 / 0.5，布料建议 4–8 |
| 涟漪 | `_RippleAmount` | 雨滴涟漪强度，0 关闭 |
| 涟漪 | `_RippleScale` / `_RippleSpeed` | 涟漪格密度与扩散速度；默认 12 / 0.6 |
| 涟漪 | `_RippleNormal` / `_RippleSpecular` | 环面法线扰动与高光加权；默认 0.25 / 1.0 |

## 合成顺序

```
干表面输入
  → 湿度：_Wetness × _WetMask，再经低频噪声打散成水膜 / 半干斑块
  → albedo 压暗提饱和、roughness 收敛到 _WetRoughness、法线向几何法线收拢
  → 水滴：哈希半球珠的两层旋转叠加 + 侧壁拖尾，输出 dropletMask
  → 水痕：等值带噪声梯度，输出 flowMask（只在侧壁生效）
  → 吸水：格点吸收区内正常细节退回水膜法线，dropletMask / flowMask 同比例衰减
  → 涟漪：朝上表面的扩散环，输出 rippleMask
  → 珠内 albedo 压暗
  → 基础光照 Lambert + GGX
  → 水膜高光：主瓣用 _WetRoughness，宽瓣用 _WaterSheen，珠面另加锐利闪点（F0 = 0.02）
  → 环境反射 BRDF_Env + 珠面镜像反射（按水菲涅耳加权）
  → 雾
```

水膜高光的加权是 `wetness × _WetSpecular × (1 + 水滴 + 水痕 + 涟漪)`：细节遮罩不改变反照率，
只放大各自区域的镜面响应，因此干区不会被水滴参数点亮。

吸水是**细节衰减**而不是另一层水：它只把法线退回水膜法线、把 `dropletMask` / `flowMask` 按 `1 − absorb` 缩小，
所以吸收区仍然暗、仍然湿亮，只是珠子与条纹消失——这是「布料把水吸进纤维」的读法。
吸水落在涟漪之前，朝上表面的涟漪不受影响；珠内压暗乘的是衰减后的 `dropletMask`，吸收区不会残留暗斑。

## 投影与空间

- 湿度、水滴、水痕、涟漪全部在**物体空间**采样，图案跟随网格，移动或旋转物体时不再相对表面滑动。
  叠 UV 会在接缝和拉伸处破坏珠格与条纹，物体空间主轴投影对任意网格更稳。
- 按物体空间法线主轴（X / Y / Z）选投影面与两条切向轴，避免竖直面上的纹理被拉伸；
  主轴切换的 45° 带上会有图案跳变。
- 水滴每格一个哈希半球珠：珠心切向斜率为 0、边缘趋近 1，乘圆顶遮罩得到凸起而不是环形凹陷；
  侧壁按 `slope = 1 − |normalOS.y|` 把珠体竖向拉长并拖出水痕，朝上表面不出尾。
  两层珠格（第二层缩放 0.71、旋转 37°）取 `max` 叠加，打散单一网格的横平竖直。
- 水痕是各向异性噪声：X 方向频率约为 Y 的 6 倍，噪声被拉成细长竖直条纹；取噪声的等值带
  （`smoothstep(0.52, 0.66)` × `1 − smoothstep(0.74, 0.9)`）而不是阈值以上的大块区域，得到窄水痕。
  梯度乘遮罩后再输出，避免整片法线被抹平。
- 时间先 `frac(time × speed)` 再进 UV：长会话下 `_Time.y` 很大，直接把大数乘进 UV 会让有限差分被精度抹平。

## 调参经验

- 水痕一开始按 `阈值以上的噪声区域` 实现，梯度放大 46 倍，结果是覆盖整个侧面的“玻璃花瓣”。
  改成等值带 + 梯度 ×8 后才读作细流水痕。
- 吸水第一版把 `smoothstep(radius × 0.4, radius, dist)` 再乘 `lerp(0.55, 1.0, pulse)`，
  整个表面拿到的都是 0.3–0.6 的半衰减，切图对比几乎看不出差别——遮罩过渡带必须先收窄
  （`smoothstep(radius × 0.82, radius, …)`），块状吸收区才读得出来。
- 吸收块尺度按物体空间 UV 定：0.5 半径的球上 `_AbsorbScale = 10` 只有 5% 球径的碎点，
  量感像噪点；4–5 才是「一块布吸透」的尺度。
- 珠面环境反射按 `waterF`（水菲涅耳）加权而不是直接乘遮罩，否则珠子会整颗泛白。
- 湿膜与水滴都会抬高法线扰动，`_WetNormalFlatten` 与 `_DropletNormal` 需要一起调，
  单把其中一个推到 1 以上会出现碎裂感。

## 依据

- Lagarde / fxguide, *Making Wet Environments*：湿表面不是简单压暗 + 提高高光；暗化与 IOR、孔隙率相关，粗糙度和法线同样变化。
  <https://www.fxguide.com/fxfeatured/game-environments-partc/>
- CraftPBR, *Wet Surface Materials: PBR Rain Shader Guide*：单张湿度驱动 albedo 压暗、roughness 收敛到水膜目标、细法线衰减。
  <https://craftpbr.com/guides/wet-surface-materials>
- Yonsei, *Real-time Animation of Rain-wet Cloth Material*：湿布三特征为变暗、反射增强、高光更亮。
  <https://fastly.jsdelivr.net/gh/YaelCassini/Papers/TA/Real-time%20Animation%20of%20Rain-wet%20Cloth%20Material.pdf>
- Unity, *Production Ready Shaders*：官方 Wet 子图以「压暗并提饱和 + 提升光滑度」实现低成本湿润感。
  <https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Shader-Graph-Sample-Production-Ready.html>
- Tatarchuk, *Artist-Directable Real-Time Rain Rendering in City Environments*（SIGGRAPH 2006）：
  湿路面用沿重力方向的各向异性条纹表现雨水流动，是水痕滚动噪声与法线扰动的依据。
- Unity 中国, *Unity 湿身效果*（HDRP Shader Graph 教程）：布料用 **Voronoi × 时间**降低法线强度表现「吸水」，
  雨滴用翻转序列贴图叠加到法线与光滑度上。
  <https://developer.unity.cn/projects/5e2fa606edbc2a39a9fe6552>
  本实现只取 Voronoi 吸水衰减这一半，没有引入序列贴图（需要美术出 479 帧 4K 图，且要求 UV 朝向一致）。

## 边界与近似

- 湿身是与湿度相关的经验模型，不做孔隙率 / 吸收扩散模拟；水膜只作为附加高光瓣，不改变介质 IOR 的完整 Fresnel 分配。
- 吸水只衰减水滴 / 水痕的细节，不模拟水在纤维内的扩散、不改变 albedo 压暗量，也没有「吸饱后变厚」的体积感。
- 水滴是哈希格点上的半球近似，没有真实表面张力、体积或折射；珠格在主轴切换的 45° 带上会有图案跳变。
- 水痕是单层滚动噪声，不是基于高度场的流动模拟；近看能看到噪声纹理的重复结构。
- 涟漪只用程序化环，不做雨滴撞击的位置 / 时间累积，也不会被物体遮挡。
- ShadowCaster / DepthOnly 不含湿身与涟漪的几何差异。
- 独立 shader 的光照是 Lambert + GGX + 环境光，仅用于效果验证；并入 PBRToon 后这一段换成 Toon / SSS / Fabric 合成。

## 测试场景

`Test/RainEffectTest.unity`：七个球并排——Dry / Wet Film / Droplets / Flow / Ripples / Rain Full / Absorbed Cloth，
外加一块俯视用的湿地面（只开涟漪）。材质在 `Test/Materials/Rain_*.mat`，
噪声 `Test/Textures/WeatherNoise.asset`。打开场景直接调材质参数即可对照七态；`Rain_Absorbed.mat` 用
`_Absorbency = 1`、`_AbsorbScale = 4.5`，与 `Rain_RainFull` 是同一套水滴 / 水痕参数，只差吸水一项。
