# CelToon 资源搬运审计（2026-10-17）

## 结论
- 来源 `C:\Programs\Unity\Unity Project\CelToon\Assets`（Unity 2022.3.57f1c2），目标 Lab（Unity 6000.3.18f1）。以 `.meta` GUID 为主键核对，Lab 中多数同名资产是同一 GUID 的后续版本。
- 已搬运 `Assets/Mine/Special/HLSL/OutlineFunction.hlsl`，保留来源 GUID `fbbe23e43f1c4aa4f839119a10966dee`；补文件头横幅、函数装饰块与零 NDC 退化保护。`check_norm.py` 与 `check_api_refs.py` 通过。该库尚未被任何 shader include，未改变当前渲染行为。
- SSC：CelToon `Shaders/Volume/Cloud/SSC.shader` 与 Lab `Shaders/PostProcess/SSC/SSC.shader` 同 GUID `3784ad67eb3b5304b9245dd051049729`。Lab 版已是 URP 17 RenderGraph，支持 2D R / 2D RG / 3D XYZ 噪声模式；2026-10-17 已按用户批准合入 CelToon 的 HG 相函数与高度环境光，作为默认关闭的可选散射路径，详见 [合并记录](2026-10-17-ssc-celtoon-scattering-merge.md)。
- `ShadowFunction.hlsl` 未搬运：Lab 的 `LightFunction.hlsl` 已复用其 GUID `d7dce55e...`，且旧库含无前缀 `MainLight` 与 include 内 pragma。
- 跳过 RimToon / SSR（Lab 已退役）、Portal（第三方模板 + 12 个脚本）、WaterVolume(Simple)（ASE 生成大文件）、Tree LeafTest（GUID `0f96a293...` 已在 Lab 的 `LeafShader.unitypackage` 中）、TriplanarTest（测试 Shader）以及全部 C# 游戏逻辑模块。

## 证据
- 审计明细：`tmp/celtoon-audit/README.md`、`tmp/celtoon-audit/compare.csv`（tmp 被 .gitignore 忽略）。
- 新增资产：`Assets/Mine/Special/HLSL/OutlineFunction.hlsl` + `.meta`。

## 未验证
- OutlineFunction 未被消费方 include，未做运行时 / 画面验证；SSC 合并已在 Unity 6000.3.18f1 batchmode 完成导入与 shader 消息检查（0 条）。
