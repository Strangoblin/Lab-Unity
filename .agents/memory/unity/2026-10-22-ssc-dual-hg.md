# SSC 加性双 HG（2026-10-22）

用户确认归一化混合虽然抬高了外圈，但视觉亮度仍不足；目标改为此前有效的 `HG0.8 + HG0`。本次只替换相位组合，不改云密度、积分布长、环境项或颜色映射。

## 最终相位

```hlsl
hgPhase = HG(g, cosTheta) / HG(g, 1.0);
isotropicHG = HG(0, cosTheta) / HG(0, 1.0);
phase = hgPhase + isotropicHG * SecondaryScattering;
```

- `Phase G` 默认 0.8，控制前向亮斑。
- `Secondary Scattering` 默认 1.0、范围 0..2；它真正加到主 lobe 上，不再归一化或压回峰值 1。
- `Secondary Scattering=0` 恢复纯单 HG；默认 1.0 复现 `HG0.8 + HG0`。
- HG0 是宽角多次散射的轻量近似，不替代环境光，也不改变原有天空/地面环境项。

## A/B 结果

M2/Metal 隔离主光、960x540、固定视角和当前噪声/云配置：

- g=0.8、cosine=1：单 HG 1.000000 → 双 HG 2.000000。
- cosine=0：0.003809 → 1.003809，侧向散射能量被 HG0 显式补足。
- 暗部云像素均值：0.03251 → 0.89975，约 27.67 倍。
- 亮部云像素均值：0.35610 → 1.17086，约 3.29 倍。
- 单 HG 重复渲染逐像素差 0；518,407 断言通过，设置与 scene dirty 状态完整恢复。

这是隔离光源下的定量与配对渲染证据，最终画面观感仍需用户确认。中心亮度提高约 2 倍，如果出现过亮，应优先降低 `Secondary Scattering` 或主光/颜色强度，而不是重新做归一化混合。

## 三档与验证

- Low/Medium/High 仍为 32步降3、64步降2、128步降1；本轮没有改变档位预算或执行额外计时。
- `TierCheck.cs` 最终 6,239,008 断言通过，三档关键词、实际缓冲尺寸、隐藏字段和完整恢复均正确。
- 编译通过；仅剩其他文件既有 warning。
- 生产 Shader hash `dab6e5e1b083142de3cdfbc79dbcd1842bc8223b8bcb0d2e734c9a8518698bc5`；Feature hash `0e14dfa8e89af9a4450c7b35867a773b0922d49debdf437dde2eae722432bc8b`。
- 证据：`tmp/sessions/2026-10-20-ssc-tiers/scattering-20261022-084203-5dc55fa2/`，含 `stats.csv`、`phase.csv`、`single-hg.png`、`dual-hg.png` 和重复控制。

## 尚未解决

双 HG 只解决相位造成的亮度和外圈能量问题。当前 alpha 不含物理步长、太阳透射仍用线性密度近似、光照探针距离仍随档位变化；32/64/128 步因此仍可能改变厚度和阴影。云内多次散射、云团黏连和抗锯齿重建仍待后续。变更尚未提交。
