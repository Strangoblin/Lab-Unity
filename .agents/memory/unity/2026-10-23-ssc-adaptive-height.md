# SSC 撤回整体重构，仅加入自适应高度（2026-10-23）

## 用户当前裁决

用户试用整体列密度重构后明确要求终止并还原，随后单独加入自适应高度。此前 [结构计划](2026-10-23-ssc-column-density-plan.md) 的整体实现、连续阈值、世界步长积分、光照距离解耦和sRGB噪声兼容均已撤回；后续不要把这些内容当作当前实现或继续自动落地。

## 已还原的范围

- 基线是整体重构前的 Circle 版本，Shader SHA256 为 `a0dc5ed0b48bf9a78e224cc8032fe944b9549aa7b06089aeb1b570217a748fb6`；保留更早的共享 SDF Box/Square 逐轴归一化。
- 还原 `Noise`、`DensityH`、完整 RAYMARCH / EvaluateCloud / 重建合成原代码，恢复原高频滚动、原硬阈值、原每步alpha和20×step光照探针；未保留额外颜色空间转换。
- `SSCFeature.cs` 去掉整体重构新增的 heightVariation/density/detailStrength、噪声格式标志及对应ID/接线，恢复旧threshold名称和语义。先重建并核对重构前完整Feature SHA `0e14dfa8e89af9a4450c7b35867a773b0922d49debdf437dde2eae722432bc8b`，再加入下述唯一参数。
- 恢复 Validation~/README.md；经门禁删除本轮自建的 `Validation~/ColumnDensityCheck.cs`。先前所有验证文件均保留。
- 场景和Renderer磁盘文件与整体重构前SHA一致，未保存或覆盖这些资产及Voronoi纹理；保留所有其他已有脏改动，未提交或推送。

## 当前唯一新增行为

- 参数 `adaptiveHeight` / `_AdaptiveHeight`，默认0.5，范围0..1；0保持原等高结果，1完全随低频密度改变云顶。
- 原 `DensityLOD` 增加 `out float columnDensity`，仅暴露已经采样的 `low` 值，原返回值及高频计算保留，不额外采纹理。low与detailLOD无关，避免ray的LOD分段造成云顶台阶。
- 原全局高度和Circle域早退保留；得到原noiseDensity后，仅在Adaptive Height>0时计算 `localTop=lerp(globalTop,bottom+(globalTop-bottom)*saturate(low),strength)`，再用原 `DensityH` 对局部区间求高度密度。
- 局部厚度为0或当前位置高于localTop时，高度权重归0，避免除零以及middle=1导致云顶以上残留。强度0直接沿用原高度计算结果，避免重算区间造成误差。
- 后续仍按原noiseDensity×heightDensity、原threshold与Circle权重输出。没有新增宏观重映射、细节强度或光学密度控制。

## 验证与限制

- Unity C#编译和规范检查通过；Metal 32/64/128/256四关键字×两个Pass共8组合，最终Shader消息0。
- 临时GPU脚本抽取真实当前与真实Circle基线函数，134项断言通过：强度0在固定输入的56组密度输出与基线浮点值完全一致；low跨LOD一致；低频.2/.8、全局40..60时强度.5的局部顶为52/58，强度1为44/56；局部顶部外、middle=1与零厚度边界验证通过。live默认值为.5，RT和场景dirty状态恢复。
- 初次测试发现Metal对CalculateDensity分支早退报一条潜在未初始化警告，已改为局部高度权重显式归0并统一返回，最终0消息。
- 没有保留上一轮整体重构的对照图作为当前效果证明，当前视觉效果待用户观察；未新增性能承诺。一次性验证输入与脚本收尾清理，结论留本记录。
