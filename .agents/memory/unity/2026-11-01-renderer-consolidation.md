---
name: 渲染管线模块归并到 Special/Renderer
description: CustomRenderer / Mask / ScreenSpaceTrace 三条管线归入 Special/Renderer，Unity 2022 版 PreparationRenderer 删除；GUID 与引用未变。
date: 2026-11-01
---

# 渲染管线模块归并（2026-11-01）

用户要求把「所有渲染管线」整合到 `Assets/Mine/Special/Renderer`，来源是 `Special/Rendering`、`Special/UniversalFunction`、`Scripts/CustomRenderer`，并指明重复实现里保留 Unity 6 半适配版。

## 结果

| 结果位置 | 原位置 |
|---|---|
| `Special/Renderer/CustomRenderer/` | `Scripts/CustomRenderer/`（Unity 6 版，保留） |
| `Special/Renderer/Mask/` | `Special/Rendering/Mask/` |
| `Special/Renderer/ScreenSpaceTrace/` | `Special/Rendering/ScreenSpaceTrace/` |
| — | `Special/UniversalFunction/PreparationRenderer/`（删除） |

判定依据不是目录名：`PreparationRenderer`、`PreparationRendererData`、`PreparationFeature` 三个类自己带 `[Obsolete("Use Mine.CustomRenderer …")]`，且 `grep` 全仓 Guid 确认没有任何 `.unity` / `.prefab` / `.asset` 引用它们，`Preparation.shader` 也只有自身 meta。删除进垃圾桶（`20261101-205904-b3e301` 等三个桶），可 `cli.py restore` 取回。

命名空间**没有**跟着改（`Mine.Rendering`、`Mine.ScreenSpaceTrace`、全局 `CustomRenderer`）：改名的收益是目录与命名空间对齐，代价是 MaskBlur / MaskRBR / SSGI 消费方与 Validation 脚本一起动，本轮按「只做结构归并」处理。父目录补了 [Renderer README](../../../../Assets/Mine/Special/Renderer/README.md)。

## 必须同步的写死路径

仓库里有三类地方硬编码 `Assets/Mine/Special/Rendering/...`，移动后不改会静默失败或指向空：

- SSGI 的 `Validation~/{HiZChecks,SharedTraceChecks,SpecularHiZChecks}.cs` 用 `AssetDatabase.LoadAssetAtPath<ComputeShader>(...)` 取金字塔 Compute；
- `Special/Renderer/ScreenSpaceTrace/Validation~/{GpuChecks.cs,run.sh}`、`RuntimeFixture/TraceConsumers.shader`、`TraceChecks.compute`（`Root` 常量与 include 路径）；
- 文档：`MaskBlur/README.md`、`MaskRBR/README.md`、`SSGI.md`、两份模块 README 的复跑命令、`Special/HLSL/RayStepFunction.hlsl` 的注释。

`.meta` 成对搬移后 GUID 不变，所以 `IA_Renderer.asset`（`CustomRendererData`）与 `PC_Renderer.asset`（金字塔 Compute）里按 GUID 的引用无需改。

## 工具事实

`cli.py move` 只处理**文件**：传目录会在 `open(src)` 处得到 `[Errno 13] Permission denied`，报「写阶段失败，已回滚」，目录本身其实完好。目录搬移走原生 `mv`，并手动把同名 `.meta` 一起搬。

## 验证

搬移后 `unityctl asset refresh` 报 `compilation succeeded`；`unityctl logs --full -l warning` 中只有既有的 `UniversalInteractionManager._rendererIndex` 未使用警告，无 `error CS` / 缺失脚本。画面未单独取像——本轮不动渲染行为。
