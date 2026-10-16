---
name: interior-mapping-archive
description: InteriorMapping render effect and InteriorMap baker production handoff.
date: 2026-09-04
---

# InteriorMapping 归档（2026-09-04）

## 完成状态

InteriorMapping 小项已完成开发并进入归档。正式链路由材质 Shader、私有 HLSL、InteriorMap 烘焙工具、生成纹理与 SampleScene 验证对象组成；本次收尾不修改渲染或烘焙逻辑。

## 正式产物

- `Assets/Mine/Shaders/Render/InteriorMapping/`：统一 Box / Hemisphere 的 `InteriorMapping.shader`、私有 `InteriorMappingFunction.hlsl` 与示例材质。
- `Assets/Mine/Scripts/InteriorMapBaker/`：场景框架、体积/方向标记、六面渲染与投影转换、Inspector/Editor 工具及完整使用文档。
- `Assets/Textures/InteriorMap.asset`：当前示例材质引用的烘焙纹理。
- `Assets/Scenes/SampleScene.unity`：包含 InteriorMapBaker 框架与使用示例；场景通过 GUID 引用材质和 Baker 组件。

## 架构结论

- Shader 与 Baker 共享方向约定；Box 使用 2:1 经纬展开，Hemisphere 使用方形圆盘投影。
- 单效果专用数学保留在 Shader 同目录私有 HLSL，不提升到 `Assets/Mine/Special/HLSL/`。
- `DebugOutputFeature` 与 `InteriorMappingScreenDebug.shader` 作为停用诊断链保留；`PC_Renderer.asset` 中 Feature `m_Active: 0`、`settings.debug: 0`。
- Baker 文档是功能说明的权威正文，Memory 只保留完成状态、产物边界和架构决策。

## 验证证据

- 相关 10 个 `.cs/.shader/.hlsl` 文件经 `.mcp/validation/check_norm.py` 全量检查，全部 exit 0。
- 未发现 `TODO` / `FIXME` / `HACK`；EditorWindow 中的日志均为保存、加载和异常处理的有条件用户反馈。
- 材质 GUID 正确指向统一 Shader 与 `InteriorMap.asset`；SampleScene 正确引用材质及 3 个 Baker 组件。
- UnityCtl bridge 必须与 Editor 位于同一执行环境；沙盒与真实宿主不互通，因此跨边界结果不作为编译/运行证据。

## 整理结果

- `InteriorMapBakerWindow.cs` 及其 `.meta` 已迁入 `Assets/Mine/Scripts/InteriorMapBaker/Editor/`，原 GUID `3252e5149276147ec9b205e27442c2d7` 保持不变。
- 已清理 `tmp/InteriorMapping-pre-opaque-2026-08-27/` 的 6 文件旧快照，以及 2026-08-27 的 6 张专项调试截图。
- 清理前回退包位于 `.codex/tmp/backups/InteriorMapping-cleanup-2026-09-04.tar.gz`，SHA-256 为 `3b2cf1eb8e975a4b623aaaf9f87e35d0bfc7209bab77108375a729a334a05b6b`。
