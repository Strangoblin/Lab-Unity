# SSC 结构、性能与参数重设计建议（2026-10-20）

本轮为诊断、有限计时和具体设计，**未修改生产 Shader/Feature**；上一轮质量修复仍留在工作区。
用户报告云团黏连，当前设备必须 downsample=2 才流畅，并要求重新精炼面板与档位。

## 当前事实

- Apple M2 / Metal；用户已打开 RenderScene，当前 SceneView 位于 (26.29,8.36,-2.08)，欧拉角 (297.82,270.13,356.60)。
- 实时 SSC 为 COUNT64/downsample2，磁盘 Renderer 为 COUNT128/downsample2；保护用户内存64步，未保存或覆盖。
- 噪声只依赖 XZ，密度为 F(x,z)×H(y)。thickness 实际控制高度剖面幂，趋近1会压平成厚板层；不是云盒厚度（size.y）。
- fBm 为正值加权和，LOD4/3/2/1均值分别是原噪声μ的0.96875/0.9375/0.875/0.75，LOD会改变基础密度。
- stepAlpha=ρ(1-A)不含步长；threshold=.2意味着每个非零采样至少贡献局部密度.2，21个连续有效样本即可使A>.99。步数不是纯精度参数。
- 光照在每步重新采完整密度，空密度也不跳过；探针距离20×step随步数变化。无透射率早停。
- 完整ray名义噪声fetch上限7N，即64/128/256为448/896/1792。未考虑几何结束/缓存/分支，不能换算成GPU毫秒。
- 上轮合成固定16云+16邻深度+中心深度+场景色采样；降低积分尺寸并不降低这部分全分辨率成本，边缘回退还能再触发完整ray。

## 有界实测

使用临时Game相机复制当前SceneView的pose/FOV，1920×1080、ARGBHalf、Renderer0、无PP、speed/jitter=0。
其余使用当前RenderScene；其它场景材质时间未冻结。每配置每轮8预热+12样本，共3轮×6配置=216样本，轮间反向顺序。
口径为SubmitRenderRequest+1×1 ReadPixels的GPU同步墙钟，不是纯GPU/Pass时间或Game View FPS。

| 配置 | 原版三轮块中位ms | .995早停三轮块中位ms | pooled中位ms：原→早停 |
|---|---|---|---|
| 1/4宽高、64步 | 12.851 / 12.509 / 12.356 | 12.530 / 11.213 / 11.208 | 12.567→11.339 |
| 1/2宽高、64步 | 20.350 / 20.355 / 20.044 | 19.065 / 18.074 / 18.989 | 20.350→18.788 |
| 1/4宽高、128步 | 18.895 / 18.355 / 18.412 | 14.993 / 15.254 / 15.833 | 18.435→15.299 |

同预算早停中位改善方向一致；64步1/4为2.50–10.36%，128步1/4为14.01–20.65%。q64/q128的pooled P95反而变差，不能承诺尖峰改善。
固定PNG显示RGB MAE=.00131369、P95通道差1/255、P99为2/255，但最大局部差50/255；不是严格等价，阈值需再用float/边缘对照确定，未合入生产。

校验216唯一样本键、每块0..11完整、所有耗时有限正值且sync>=submit；源码hash匹配；settings恢复、sceneDirty前后false。
首次metadata的List<Block>未序列化，不能作为完整报告，补CSV后重新实跑并保留所有216样本；不把第一次空报告当通过。

- [原始元数据](evidence/2026-10-20-ssc-quality/cost-profile.json)
- [完整逐样本CSV](evidence/2026-10-20-ssc-quality/cost-samples.csv)
- 可复跑源码：Assets/Mine/Shaders/PostProcess/SSC/Validation~/PerformanceProbe.cs，归档后已实际复跑。

## 建议实施顺序（未实施）

1. 先解耦积分：可见区间[max(tNear,0),min(tFar,tScene)]，alpha=1-exp(-extinction×density×stepLength)；光照探针用独立世界尺度；LOD做均值保持。以当前64步画面校准，允许有意修正外观，不承诺逐像素保旧。
2. 先做透射率早停、粗形状筛空域/局部空密度跳光照；光照独立粗LOD。对照误差与成本后再选阈值，不直接把.995当通用默认。
3. 重建分普通区域快速路径和深度边缘精细路径：均匀天空可用Core四次bilinear B-spline，混合深度保引导与有界回退。需要薄前景/云内几何/halo控制，不能直接替换成无条件bilinear。
4. 密度拆低频Coverage→团块形状/高度变化→高频减法侵蚀。覆盖决定分布，侵蚀切断连接，密度决定透过率；不同octave的高度UV错位打破严格F×H结构。先保留2D循环R纹理，新增3D纹理并非当前预算下的前置条件。视觉收益需逐项A/B。
5. 以上稳定后建立质量档位与新面板，验证档位不再改变覆盖/宏观厚度/光照探针位置。时域重建留后续，需同时处理云风速和相机运动，不能直接套静态场景历史。

## 面板方案

| 分组 | 常规字段 | 语义 |
|---|---|---|
| Technical · Resources | Shader、Noise（折叠） | 资源引用 |
| Technical · Volume | Altitude、Bounds Size | 中心高度、实际体积尺寸（Y才是层厚） |
| Performance | Quality | 一个枚举展开分辨率、步数、噪声/光照/重建预算 |
| Artistic · Shape | Coverage、Cloud Size、Erosion、Density | 云量、世界尺度、边缘侵蚀/分离、光学浓度 |
| Artistic · Motion | Wind Vector2 | 世界XZ方向和速度；统一可见运动语义 |
| Artistic · Color | Lit Color、Shadow Color | 当前两色 |
| Artistic · Advanced（折叠） | Vertical Fullness、Vertical Peak、Ambient、Forward Scattering | 保留独立效果控制，范围重新校准 |
| Debug | Off / Density / Transmittance / Lighting / Cloud Buffer | 观察中间数据 |

正常面板移除原始RayCount/downsample/jitter/threshold/middle/thickness命名；jitter内化，threshold改Coverage须版本化换算，thickness与物理Density不可简单改名沿用。风向原采样position+offset造成可见方向相反，迁移需明确。

候选档位以积分解耦后为前提，**不是已测定良态**：

| 候选 | 积分宽高比例 | 步数 | 目标 |
|---|---|---|---|
| Low | 1/4 | 32 | 优先减积分/光照，保留轮廓分辨率 |
| Balanced（默认） | 1/4 | 64 | 当前设备实时配置作为迁移起点 |
| High | 1/2 | 64 | 优化后的画质目标，当前原版墙钟约20ms，不可宣称流畅 |

档位用不可变Tier和纯GetTier统一展开，所有Pass单源接线。旧任意组合不能静默就近映射；记录旧字段和版本，再校准迁移画面，特别保留用户实时64步而非以磁盘128步覆盖。

原始报告所有者SSC重设计诊断；依赖对应源码hash、当前RenderScene/噪声/光照与协议。后续正式优化测评取代时可人工确认清理。未测连续FPS、运动拖影、其它平台、GPU逐Pass时间；本轮未调用OpenCode。
