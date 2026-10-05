# Spatial Filter Sampling Budget

> 全屏空间滤波的质量档与半径契约。代码骨架见
> [spatial-filter-budget.hlsl](../../../templates/shader/postprocess/spatial-filter-budget.hlsl)。

## 核心约束

空间滤波必须把**采样预算**与**感受野**分开：

| 参数 | 决定什么 | 暴露位置 |
|---|---|---|
| Quality | 样本数、分布模板、可选下采样率 | `Performance` 枚举 |
| Radius | 采样分布在源纹理上的缩放范围 | `Artistic` 连续参数 |

`Radius` 不参与循环边界。禁止 `for (-radius ... radius)`；否则美术参数会同时改变成本，
同一质量档失去稳定预算。循环次数必须由档位在编译期或 C# 档位表中确定。

## 标准映射

| 算法 | Quality 控制 | Radius 控制 |
|---|---|---|
| Gaussian / Bilateral Blur | tap 数、核分布、pass 分辨率 | 核在像素/texel 空间的跨度 |
| SNN | 对称样本对数量与方向 | 每对样本距中心的最大距离 |
| Kuwahara | 扇区数、每扇区样本数、预滤波档 | 扇区覆盖范围 |
| SSGI / SSR Resolve | 邻域 tap 数、trace/resolve 分辨率 | 邻域搜索范围 |

## 实现规则

1. 档位先展开为固定的 `sampleCount/gridWidth/downsample`，Shader 循环只消费该结果。
2. offset 先归一化到 `[-1, 1]`，再乘 `radius * sourceTexelSize`；半分辨率 RT 必须使用自身 texel size。
3. `radius = 0` 若定义为关闭，应由 C# 跳过滤波 Pass；不得执行 N 次中心重复采样。
4. 默认档逐字段复现重构前的样本数、半径和分辨率，避免“优化”改变默认画面。
5. Quality 提升必须增加有效信息量；只提高 RT 分辨率却维持单样本会暴露更多噪声，不算高质量档。
6. 算法需要离散半径时，把离散值纳入档位；不要再暴露同名连续半径制造双重来源。

## 验收

- 固定 Quality 扫描 Radius：GPU 循环次数和 RT 数量保持不变，仅覆盖范围变化。
- 固定 Radius 切换 Quality：采样预算按档位单调变化。
- 检查 HLSL 循环边界中不存在 `_Radius`、`radius` 或由其转换得到的整数。
- Debug 视图分别验证采样分布和最终滤波，避免把下采样平滑误判为算法收敛。
