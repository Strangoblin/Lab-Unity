# SSC 替换为 CelToon 密度与散射（2026-10-17）

## 结论
- 用户本意是让 CelToon 的新 SSC shader 取代 Lab 旧实现，而不是并存为可选分支。最终采用单循环：`DensityLOD`（低频 + 多层高频按 0.5 混合、逐层滚动加速）与 `DensityH`（以 `middle` 为峰值的分段线性剖面）直接替换 Lab 原密度场，光照改为 CelToon 的 HG + 高度环境光散射积分。
- 保留 Lab 的 URP 17 RenderGraph `SSCFeature`、三种噪声模式与 ray count 关键字；未复制 CelToon 的 `SSCFeature.cs`（脚本算法留在原项目）。
- 首版曾实现为「legacy + 可选散射」双循环，按用户澄清已删除可选分支与 `_ScatteringWeight`。

## 参数
- `_CloudMiddle`(0.5)、`_CloudThreshold`(0.2)、`_PhaseG`(0.8)、`_AmbientIntensity`(1.0)；C# `Settings` 同步。
- 旧的 `scale / speed / rotation / height / size / thickness / jitter / downsample` 语义保留；`rotation` 改为 CelToon 的度→弧度换算（旧实现乘 `180 / 3.14`，角度一大就混叠）；`size` 默认对齐 CelToon 的 `(100, 50, 100)`（旧为 `(100, 100, 10)`）。

## 行为变化
- 默认画面即新路径，不再提供与旧 shader 一致的输出，也没有散射开关。
- 云密度改为多层混合，阈值参数化；`DensityH` 由尖顶改为 middle 峰值分段剖面。
- 光照改为 `1 - lightDensity` 阴影 × HG 相位 + 高度插值环境光，光照步进取 `step * 20`。
- 保留 CelToon 的预乘累积 + `lerp(scene, cloud, alpha)` 合成，alpha 实际被乘两次（`radiance * a²`）；未修正以保持来源行为。

## 验证
- `check_norm.py`：shader、C# 均通过。
- Unity 6000.3.18f1 `-batchmode -nographics` 导入 + `ShaderUtil.GetShaderMessages`：`hasError=False`、`messageCount=0`；无 `error CS`。
- `Assets` / `ProjectSettings` / `Packages` 无 `SSCFeature` 序列化实例，未接入 Renderer，无法做运行时/画面对照。

## 风险
- 成本高于旧实现：旧 `DensityLOD` 只返回 `low`，高频层可能被编译器删除；新实现真正使用全部噪声层，且每步光照侧密度采样放大到 `step * 20`。
- 环境光取 `RenderSettings` 三段环境色；Ambient Mode 为 Skybox 时不等价于天空盒采样。

## 工具备注
- 本卷 `os.lstat` 与 `os.fstat` 的 `st_ctime_ns` 存在约 1ms 差异，`gate_cli delete` 会误报「文件在预检期间发生变化」（对已提交的 `SSC.shader` 同样复现）；`move` 的 GUID 检查也会把源 `.meta` 自身判为占用。本轮临时验证脚本按精确路径人工清理。
- Unity batchmode 导入补生成了 `Assets/Mine/Scripts/ProductionTools.meta`（仓库原先缺该目录 meta），与本次替换无关，未纳入提交。
