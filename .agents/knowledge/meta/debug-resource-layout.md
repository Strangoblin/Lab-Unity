# Debug 资源集中布局

状态：用户确认后已完成迁移与 Editor 验证。调试流程见 [unity-render-debug](../../skills/unity-render-debug/SKILL.md)，执行证据见 [迁移记录](../../memory/unity/2026-10-10-debug-resource-migration.md)。

2026-10-12：用户要求不保留专用验证，InteriorMappingScreenDebug.shader 及 meta 已删除；当前通用区仅保留 DebugOutputFeature、DebugOutput.shader 与 README。下方是 2026-10-10 迁移历史，不代表所有目标仍保留；见 [退役记录](../../memory/unity/2026-10-12-interior-screen-debug-retired.md)。

## 归属

通用画面诊断工具集中到 `Assets/Mine/Special/Debug/`：Feature、调试 Shader 与 README 共处，避免把工具归为正式 PostProcess 效果。迁移前来源与目标见下表。现有受管落点允许 Special，不需要增加根目录或放宽门禁。

功能专属的 Debug 分支、辅助输出或 `Validation~/` 留在各功能目录；自动化取像脚本仍归 unity-developer 的 scripts，skill 仍归共享 skills，不全部搬入 Assets。

## 2026-10-10 已确认迁移清单

以下保留已执行的迁移前来源，供回退核对；不是当前运行资源入口。9 项移动、1 项旧目录 meta 删除及两个空旧目录清理均已完成。

| 迁移前来源 | 目标 / 动作 |
|---|---|
| `Assets/Mine/Scripts/Debug/DebugOutputFeature.cs` | `Assets/Mine/Special/Debug/DebugOutputFeature.cs` |
| `Assets/Mine/Scripts/Debug/DebugOutputFeature.cs.meta` | `Assets/Mine/Special/Debug/DebugOutputFeature.cs.meta` |
| `Assets/Mine/Scripts/Debug/DebugOutputFeature.md` | `Assets/Mine/Special/Debug/README.md` |
| `Assets/Mine/Scripts/Debug/DebugOutputFeature.md.meta` | `Assets/Mine/Special/Debug/README.md.meta` |
| `Assets/Mine/Shaders/PostProcess/Debug/DebugOutput.shader` | `Assets/Mine/Special/Debug/DebugOutput.shader` |
| `Assets/Mine/Shaders/PostProcess/Debug/DebugOutput.shader.meta` | `Assets/Mine/Special/Debug/DebugOutput.shader.meta` |
| `Assets/Mine/Shaders/PostProcess/Debug/InteriorMappingScreenDebug.shader` | `Assets/Mine/Special/Debug/InteriorMappingScreenDebug.shader` |
| `Assets/Mine/Shaders/PostProcess/Debug/InteriorMappingScreenDebug.shader.meta` | `Assets/Mine/Special/Debug/InteriorMappingScreenDebug.shader.meta` |
| `Assets/Mine/Scripts/Debug.meta` | `Assets/Mine/Special/Debug.meta`，保留目录 GUID |
| `Assets/Mine/Shaders/PostProcess/Debug.meta` | 已在确认目录空 / GUID 无引用后删除；不复用第二个目录 GUID |

## 同步与验证范围

- 所有文件 `.meta` GUID 唯一且保持原值，C# 逐字节未改。`PC_Renderer.asset` 的脚本绑定保持 GUID `b3dcbe7b74f544b58bb86d2e2f92a478`，磁盘哈希未变。
- 迁移时两个 Shader 菜单已改为 Debug 前缀；当前只保留 `Debug/DebugOutput`，专用验证 Shader 已按上方退役记录删除。
- skill / Roslyn CLI 活动入口已同步，历史 dated memory 保持原快照；资源 README 指向统一调试 skill。
- Assets 经 move / write / delete 门禁执行；自动导入在成对迁移期间暂缓后恢复，无门禁判定或策略改动。
- Unity C# 编译通过；两个 Shader 默认无关键字 Pass 0 经真实 PC Renderer 输出完整 640×360 PNG，编译消息为空。Feature 设置与 Active 已恢复，原 Edit Mode、场景 dirty=false / Renderer dirty=true 保持。
