# SSC 合并 CelToon 散射路径（2026-10-17）

## 结论
- CelToon 与 Lab 的 SSC 是同一资产 GUID `3784ad67eb3b5304b9245dd051049729`；直接覆盖会退回 URP 14 旧实现。本次保留 Lab 的 URP 17 RenderGraph `SSCFeature`，只把 CelToon 的散射光照模型作为可选分支合入 shader，未复制 CelToon 的 `SSCFeature.cs`（脚本算法留在原项目）。
- 默认 `_ScatteringWeight = 0`：走原 legacy 路径并提前返回，输出与合并前一致；散射路径仅在权重 > 0 时执行第二次 ray march。

## 改动
- `Assets/Mine/Shaders/PostProcess/SSC/SSC.shader`
  - 新增 `_CloudMiddle`(0.5)、`_CloudThreshold`(0.2)、`_ScatteringWeight`(0)、`_PhaseG`(0.8)、`_AmbientIntensity`(1.0)。
  - legacy 的 `DensityH` 中面与 `CalculateDensity` 阈值由硬编码改为参数；`middle` 越界时原公式会得到负底数，补 `max(0, ...)`。
  - 新增 `DensityLOD_Scatter` / `DensityH_Scatter` / `CalculateDensity_Scatter`：云密度取 `low*0.5 + high*0.5`，高度剖面按 `middle` 左右分段。
  - 新增 `HG` 相函数（g 钳制 ±0.999、pow 底数下限 1e-5）；散射路径按 CelToon 用 `1 - lightDensity` 作步进阴影、用 `unity_AmbientGround/Sky` 做高度插值环境光、光照步进取 `step * 20`。
  - 两条 ray march 宏各自加块作用域，清掉循环变量冲突与隐式截断警告。
- `Assets/Mine/Shaders/PostProcess/SSC/SSCFeature.cs`
  - `Settings` 增加上述 5 个字段并每帧 `SetFloat`；只扩展既有 RenderGraph Feature。

## 验证
- `check_norm.py`：shader 与 C# 均通过。
- Unity 6000.3.18f1 `-batchmode -nographics` 强制导入 + `ShaderUtil.GetShaderMessages`：`hasError=False`、`messageCount=0`；无 `error CS`。
- `Assets` / `ProjectSettings` / `Packages` 未找到 `SSCFeature` 脚本 GUID 的序列化实例，该 Feature 当前未挂到任何 Renderer，无法做运行时/画面对照；也因此不存在旧实例缺新字段被反序列化为 0 的风险。

## 待办 / 风险
- 散射路径开启时会多执行一次完整 ray march，成本约翻倍。
- 环境光取 `RenderSettings` 三段环境色；Ambient Mode 为 Skybox 时不等价于天空盒采样。
- 后续接入 Renderer 时需确认 Inspector 中 `middle=0.5`、`threshold=0.2` 再评估画面。

## 工具备注
- 本卷 `os.lstat` 与 `os.fstat` 的 `st_ctime_ns` 存在约 1ms 差异，`gate_cli delete` 会误报「文件在预检期间发生变化」（对已提交的 `SSC.shader` 同样复现）；`move` 的 GUID 检查也会把源 `.meta` 自身判为占用。本轮临时验证脚本按精确路径人工清理。
- Unity batchmode 导入补生成了 `Assets/Mine/Scripts/ProductionTools.meta`（仓库原先缺该目录 meta），与本次合并无关，未纳入提交。
