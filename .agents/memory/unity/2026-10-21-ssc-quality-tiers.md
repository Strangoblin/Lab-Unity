# SSC 三档性能面板与前向散射（2026-10-21）

> 2026-10-22：HG 部分已由加性双 HG 取代，见 [2026-10-22-ssc-dual-hg.md](2026-10-22-ssc-dual-hg.md)。本文保留档位与前一版混合方案的诊断记录。

用户要求把“步数 + 降采样”合并为只显示 Low / Medium / High 的三档面板，并最终指定步数为 32/64/128。用户同时报告 `PhaseG=0.8` 时 HG 把主要亮度压在中心，导致外圈过黑。

## 最终档位

| 面板 | 射线步数 | 降采样 | 1920x1080 同步请求墙钟中位 |
|---|---:|---:|---:|
| Low | 32 | 3（1/8 宽高） | 3.44ms |
| Medium（默认） | 64 | 2（1/4 宽高） | 9.19ms |
| High | 128 | 1（1/2 宽高） | 40.24ms |

最近一次三档各 36 样本（6 预热、每轮 12、3 轮且轮间反转）通过。口径包含 SubmitRenderRequest 与 1x1 ReadPixels 同步，不是纯 GPU/Pass 时间或 FPS。High 在当前 M2 视角是画质档，不能视为实时档。

面板枚举只有三项，raw `rayCount/downsample`、custom 覆盖和迁移 sentinel 均隐藏；三档没有额外预算 tooltip。

## 档位接线与迁移

- `SSCFeature.GetTier(Quality)` 是唯一预算来源；RenderGraph 每帧只解析一次 `Tier`，缓冲宽高、`_SSCTextureSize`、`_SSCDownsample` 和 `SSC_RAY_COUNT_*` 关键词消费同一结果。
- 新增显式 32-step 关键词；默认无关键词仍为 32，但运行期三档都会开启唯一对应关键词。保留 256 兼容变体供历史验证。
- 旧 serialized 资产按旧 downsample 迁移：>=3 为 Low，<=1 为 High，其余为 Medium；旧 ray count 不参与选择。当前实时 64/2 迁移为 Medium，预算保持 64/2。
- 旧脚本 API `rayCount/downsample` 保留；精确命中三档时正常选档，无法匹配的组合用隐藏 custom 值支持历史性能/验证脚本。用户选择正常档位会清除 custom 覆盖。

## HG 外圈过暗

原实现把峰值归一化 HG 直接乘到全部主光项。`g=0.8` 时 HG 的能量合法地集中到前向，但侧向只剩峰值的约 0.38%，因此体积外圈接近黑。单纯增加环境光会把云整体抬灰，不能保留前向亮斑。

最终实现把 HG 降为直接光的前向分量，并增加 `Forward Scattering`：

```hlsl
hgPhase = HG(g, dot(rayDirWS, lightDirWS)) / HG(g, 1.0);
phase = lerp(1.0, hgPhase, saturate(_ForwardScattering));
```

- 默认 `forwardScattering=0.65`；0=各向同性填充，1=纯 HG。该各向同性项是多次散射的轻量近似，不改变环境光项或颜色映射。
- `g=0.8` 时正前向仍为 1；侧向从 0.003809 提到 0.352476。M2/Metal 隔离主光 A/B 中，暗部云像素平均亮度提升 13.07 倍，亮部仅 1.47 倍；重复单 HG 输出逐像素差 0。
- 这是定量与配对渲染证据，未代替用户对最终美术观感的人工确认。若外圈仍暗可继续降低该值；过亮或过平则提高它。

## 验证与证据

- `TierCheck.cs`：6,239,008 断言通过；L/M/H 缓冲 120x67、240x135、480x270，实际步数/关键词/降采样一致，Inspector 只有三项，设置与 scene dirty 状态完整恢复。
- `ScatteringCheck.cs`：518,407 断言通过，统计暗/亮区域、重复控制和相位表，设置完整恢复。
- `TierBenchmark.cs`：108 个计时样本通过，三档预算与实际设置恢复；场景和资产均未保存。
- 编译通过；仅剩其他文件既有 warning。生产 Shader hash `58fc91d41ff35bc6c9a230e1930564425eafdee08b4759b5d60d97a28cfd471e`，Feature hash `7bd6e073e782a622b1c52e8849bd99bf2021fe2e3aa9786667b81f9e1390fff1`。
- 证据目录：`tmp/sessions/2026-10-20-ssc-tiers/`，含 `output/report.json`、`scattering-*/stats.csv|phase.csv|PNG`、`benchmark-*/samples.csv|summary.csv`。

## 尚未解决

本轮按用户指定改变了步数，所以原算法仍有步长与 alpha/光照探针耦合；High 的厚度和阴影可与 Medium 不同。Low 的 32 步会放大这种耦合。云团黏连、XZ×高度剖面、物理步长 alpha、3D 形状侵蚀、低分辨率锯齿重建/AA 和面板完整语义化仍未解决。变更尚未提交。
