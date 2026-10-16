---
name: debug-resource-migration
description: 通用 Debug 工具迁入 Special/Debug，保留 GUID 并完成真实相机 PNG 验证
date: 2026-10-10
---

# Debug 资源集中

用户在本会话确认 [精确迁移清单](../../meta-developer/references/debug-resource-layout.md) 后执行。通用诊断工具与正式后处理分开归属，功能自己的 Debug 分支 / Validation 保留原功能目录。

## 结果与回退

- DebugOutputFeature.cs、两个 Shader、README 及文件 meta 集中到 `Assets/Mine/Special/Debug/`；9 项移动保留文件及原 Scripts/Debug 目录 GUID。
- 旧 PostProcess/Debug 目录 GUID 确认无引用后删除 meta；两个旧空目录已移除，避免导入时重新生成 meta。
- C# 内容、类型和 namespace 未改；Shader 只改菜单为 `Debug/DebugOutput`、`Debug/InteriorMappingScreenDebug`。README 说明工具归属并引用共享 [unity-render-debug](../../../skills/unity-render-debug/SKILL.md)。
- move / write / delete 经 Unity 门禁执行。迁移期间仅暂缓自动导入，finally 恢复 StopAssetEditing / AllowAutoRefresh；未保存场景或资产。
- 基准 HEAD 与逐文件哈希 / GUID 保存在 [迁移基线](../../../../tmp/sessions/2026-10-10-render-debug-move-8df2/evidence/migration-baseline.json)，回退按清单精确反向移动，不整份还原脏工作区。

## 验证

- 9 个目标逐字节搬运核验通过；之后修改范围为两个 Shader 菜单及 README。文件 / 目录 meta GUID 保持且唯一，旧路径不存在。
- Unity asset refresh 返回 compilation succeeded；两个 Shader 默认无关键字 Pass 0 同步编译，ShaderUtil.GetShaderMessages 均为空，isSupported=true。
- 在 Edit Mode 临时预览场景的 Game/Base 相机中，使用当前 URP 的真实 PC Renderer（index=0）和迁入的 DebugOutputFeature，执行共享 OffscreenCameraCapture helper。
- [DebugOutput UV](../../../../tmp/sessions/2026-10-10-render-debug-move-8df2/output/DebugOutput.png) 与 [InteriorMapping 默认输出](../../../../tmp/sessions/2026-10-10-render-debug-move-8df2/output/InteriorMappingScreenDebug.png) 均为完整 640×360 PNG；真实 IHDR 尺寸与整图方向检查通过。
- [状态证据](../../../../tmp/sessions/2026-10-10-render-debug-move-8df2/evidence/editor-validation.json)：原 Feature settings 引用及 Active 恢复，预览场景 / 临时材质释放；原 Edit Mode、SampleScene dirty=false、Renderer dirty=true 保持。
- PC_Renderer.asset 磁盘哈希前后不变，Editor 的 MonoScript 路径解析为新位置、脚本 GUID 仍为 `b3dcbe7b74f544b58bb86d2e2f92a478`。没有回写其它 Feature 或 Renderer 设置。
- 独立只读复核：活动文档链接与资源存在，整个 Assets 无旧 Shader 菜单名或完整旧资源路径引用。

本轮覆盖迁移、默认 Pass 0 与相机输出冒烟验证；未穷举材质关键字、双 Pass / 深度路径或 InteriorMapping 投影质量。Editor 编译日志有其它现存脚本 warning，本轮不改那些功能。

任务脚本与输出保留在 `tmp/sessions/2026-10-10-render-debug-move-8df2/` 供本轮复现；通用 helper 仍只在 agent scripts 维护。未提交，其他并发改动未回退。
