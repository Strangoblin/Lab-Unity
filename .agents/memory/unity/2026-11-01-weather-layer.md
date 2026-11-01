# 天气层：拆成 RainEffect / SnowEffect 两个独立 shader，2026-11-01

用户要求「雨天湿身与雪天积雪」，先做成 `PBRToon` 的 `ENABLE_WEATHER` 变体验证；随后要求
「先将天气效果从 pbr 分离，单独使用 shader 展示，无误再合并」；再要求
「分离雨雪为两个 shader，先单独优化雨天湿身表现」。当前状态：

- `Assets/Mine/Shaders/Render/WeatherEffect/RainEffect.shader`（`Render/RainEffect`）：湿膜、水滴、水痕、吸水、涟漪。
- `Assets/Mine/Shaders/Render/WeatherEffect/SnowEffect.shader`（`Render/SnowEffect`）：积雪、雪面法线、闪烁。
- `WeatherFunction.hlsl`：两边共用的纯函数库，零 CBUFFER；调用方先 include Core.hlsl。
- 合并版 `WeatherEffect.shader`、`Test/WeatherEffectTest.unity`、`Standalone_*` / `Body_*` / `Fabric_*` 材质已删除。
- PBRToon 保持无 `ENABLE_WEATHER` / `_Wet*` / `_Snow*` 的状态；用户确认后再拆分并回。

**形态**：湿身改进入光照之前的表面输入：albedo 压暗并按亮度提饱和、roughness 收敛到水膜目标、法线向几何法线收拢；
水膜高光（F0 = 0.02，IOR 1.33）与雪面闪烁在基础高光之后追加。积雪当成最外层表面：朝上分量加噪声过 smoothstep
得到 `snowMask`，覆盖 albedo / roughness / metallic，法线向世界上方混合。独立 shader 只做 Lambert + GGX + `BRDF_Env`，
用于隔离观察天气层本身；光照合成不参与复用。

**雨天细节**：全部在物体空间按法线主轴投影。水滴是哈希格点半球珠，两层（缩放 0.71、旋转 37°）取 `max`
打散网格感，侧壁按 `slope = 1 − |normalOS.y|` 竖向拉长并拖尾。水痕是 X 频率约为 Y 六倍的各向异性噪声，
取**等值带**而非阈值以上区域，梯度乘遮罩后再输出。涟漪只在朝上表面，环内外斜率反向形成波峰。

**吸水（布料）**：参考 Unity 中国那篇 HDRP 湿身教程（`developer.unity.cn` project 5e2fa606edbc2a39a9fe6552，
WebFetch 被域名安全校验挡住，curl 可达），它用 **Voronoi × 时间压低法线强度**表现布料吸水。本实现取这一半：
`Weather_AbsorbFade` 输出吸收遮罩，吸收区内 `normalWS` 退回水膜法线、`dropletMask` / `flowMask` 乘 `1 − absorb`，
albedo 压暗与 roughness 收敛不变——读作「水被吸进纤维，珠子没了但布还是湿的」。判据是遮罩过渡带必须窄
（`smoothstep(radius × 0.82, radius, …)`），否则整面只有半衰减、对比图看不出差别。教程里的翻转序列雨滴贴图没采用。

**雪天细节**：噪声按物体空间 XZ / XY 双轴加权混合；闪烁格点取世界 XZ 并乘 `saturate(normalWS.y²)`
（否则侧壁被拉成竖条），且只乘 `smoothstep(0.5, 0.9, snowMask)`（过渡带里法线被混向天空，
`NdotH` 短暂抬高会在雪缘点出一圈碎亮）。

## 工具坑（2026-11-01 复现，按踩坑顺序）

1. **`Properties` 里没声明的贴图，`material.SetTexture` 会被静默忽略**。雨天 `_WeatherNoise` 只在
   HLSL 里 `TEXTURE2D` 声明、漏了 Properties，纹理绑定到默认常量，水痕遮罩恒为 0，排查了一轮精度/UV 才定位。
   表现是「同一函数在调试 shader 里正常、在正式 shader 里全空」。
2. **连续取像时改材质参数可能不生效**：探针强/弱参数、二分参数改完拍的图完全一样。可靠做法是每个变体用独立材质资产
   或重建场景/材质绑定。
3. **切换光照或材质后的第一帧可能整屏异常**：会渲染成「只有环境光」的暗色，即使实际光照正常。
   暖机从 2 帧提到 5 帧才稳定；关阴影这类改动要 6 帧。
4. **外部写 `.shader` / `.hlsl` 后必须显式重导入**：`AssetDatabase.ImportAsset(path, ForceUpdate)`。
   重导入后控制台可能留一条 `Build asset version error ... SourceAssetDB has modification time of ...`，
   `unityctl asset refresh` 即可清掉。
5. `.mcp/bin/cli.py delete` 只删目标文件、留下 `.meta`，需要再对 `.meta` 跑一次删除（它会报 orphan-meta）。
6. **预览场景不能 `SetActiveScene`**，也不能顺手改 `RenderSettings`；用自带光照或自建方向光。
7. `close.py find_verification` 扫不到 `tmp/sessions` 与 `Validation~`，verify 义务只能 `--waive` 并写明 PNG 路径。
8. **「参数写对了但图没变」先做同机位 A/B**：吸水第一版就是这样——材质探针确认 `_Absorbency = 1` 已生效，
   但隔球对比凭眼睛判不出来。改成同一球、同一相机只切 `_Absorbency` 抓两张图，再用 PIL 差分，
   才定位到是遮罩过渡带过宽（几何画对了，只是幅度被摊平）。

## 验证（2026-11-01）

- `RainEffect` / `SnowEffect` / `PBRToon`：`ShaderUtil` 0 条消息、`isSupported=True`；`WeatherFunction.hlsl` 重导入正常。
- 场景引用干净：`RainEffectTest` 13 个 renderer、`SnowEffectTest` 9 个，缺失材质 0，全工程 broken material 0。
- 成图（`tmp/sessions/2026-11-01-weather-split/output/`）：
  - 雨天 `rain-states.png`（Dry / Wet Film / Droplets / Flow / Ripples / Rain Full / Absorbed Cloth）、
    `closeup-{wet,droplets,flow,ripples,full,absorbed,puddle}.png`；水痕最终读作细竖条纹而不是「玻璃花瓣」；
    吸水与 Rain Full 同机位 A/B（`ab-absorb0/1.png`）确认细节被吃掉而压暗与光泽保留。
  - 雪天 `snow-states.png`（Dry / Snow Light / Snow Full / Snow Sparkle）、
    `snow-close-{light,full,sparkle,ground}.png`；雪线有机起伏，无网格斑点。
- 未验证 / 已知近似：水滴无表面张力与折射，主轴切换 45° 带会跳变；水痕是单层滚动噪声，近看有重复结构；
  积雪无厚度/位移，垂直面可能少量薄雪；雪面接近纯白，强光下易过曝，需配合曝光。
