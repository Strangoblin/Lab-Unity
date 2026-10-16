# 生产工具框架归入 agent 模板

## 用户裁决与来源

- 用户明确指出 `Assets/Mine/Scripts/ProductionTools` 不属于项目内容，应搬入 agent 内容；本次据此修正归属。
- 原目录创建于 **2026-10-15 19:03:20（北京时间）**。路径表 19:03:20、IO 工具 19:03:30、抽象窗口 19:04:08，来自统一 Generator / Baker 历史会话；上一轮接手在 2026-10-16 补了验证与文档。
- 旧记录保留原文：[原始实现](../unity/2026-10-15-production-tool-framework.md)、[上一轮接手](../unity/2026-10-16-production-tool-handover.md)。当前事实以本记录与活动索引为准。

## 当前落点

- 通用实现是 skill 中的一份[完整独立窗口模板](../../skills/auto-manager/templates/script/production-tool/production-tool-window.cs)，由 [README](../../skills/auto-manager/templates/script/production-tool/README.md) 说明路径、保存与所有权契约。具体项目窗口独立继承 `EditorWindow`，不依赖 agent 目录或项目级生产工具基类。
- 五窗口（Curve / Noise / FGD / SSS / InteriorMap）保留原类名、文件位置、序列化字段和 GUID；Bake / Load / Save、路径字段、资产所有权和私有 IO 在各自类内实现。生成算法、设置及预览保持上一轮行为。
- 通用模板继续使用新路径克隆保存、已有同类型资产 `CopySerialized` 原位更新、只 `SaveAssetIfDirty`、只销毁未落盘自产对象的规则。
- Lab 专属 [42 项验证适配器](../../skills/auto-manager/project-adapters/production-tools/README.md) 归 skill 的 project-adapters。原始三份框架源码、README 与 `.meta` 保留在本记录的 `evidence/.../source/`，作为不可变来源，未作为第二套活动模板。
- 已移除 `.mcp/project-resources.json` 的 `script.production-tools` 与知识合同中对应语义。生产代码和活动模板不再指向旧目录。

## 精确迁移与回退

- 迁移 12 文件（含 `.meta`）；源、目标、SHA-256、Git blob 与五消费者的输入基准见[清单](evidence/2026-10-16-production-tools-relocation/manifest.json)。原始 source 证据保存原字节；适配器迁入后更新为独立窗口反射调用。
- Unity 资产先通过 `move_gated` 导出到专属任务暂存，再按体系库通道归位；空源目录经 `delete_gated` 清理。没有放宽 `ALLOWED_PREFIXES`、policy 或工作区边界。
- 为避免遗失原本未跟踪的内容，输入保存为 Git 对象并在清单登记 blob，另建独立 checkpoint ref 固定原始输入；未改当前分支或工作区索引，未推送。
- 输入 checkpoint：`refs/codex/checkpoints/2026-10-16-production-tools-before-1668548508`（`cf43bb6f53aa643603286e56e80496f0c1c823d5`）；独立窗口与完整模板另有同日期 `production-tools-after-*` checkpoint。
- OpenCode `opencode-go/deepseek-v4.1-flash` 一次产生临时转换器，主 agent 审核并经门禁合入五份输出。转换器只用于本次迁移，不新增为长期 skill API。

## 验证

- 原目录移除后 `unityctl -t 180 asset refresh`：compilation succeeded。
- Editor 适配器：**PASS 42 checks**，包括五窗口直接继承 EditorWindow，以及上一轮的 Load / self-Save GUID、对象身份、所有权、Noise 2D 保持与三轴 3D 切片验证。
- 完整 skill 模板替换业务占位符后，在独立 Roslyn 程序中验证编译、Bake 回调和关窗释放自产资产通过。
- 并行只读复核对照五消费者输入 blob 与公共源，未发现原算法、字段、预览或保存流程的实质变化。
- 旧目录及同级 `.meta` 均不存在；42 项检查的 fixture 剩余 0。五窗口 `.meta` 与 HEAD 一致，场景与 Renderer 文件 SHA-256 与迁移前相同。
- 本次源码、模板、适配器与工具文档的规范检查通过；模板 API 检查、架构 strict 与四套隔离 MCP 契约测试通过，隔离 runner 确认真实 state/audit 指纹未变。
- skill-creator 通用 quick_validate 不识别本项目既有的 `user-invocable` / `argument-hint` frontmatter 扩展，未为通过该检查改动原平台元数据；项目架构检查与实际模板编译另已通过。

## 边界

- 回归仍未覆盖不同资产覆盖确认/取消的物理点击、场景依赖的 InteriorMap Bake，以及 FGD / SSS GPU 画质。
- 保持现有 Noise 单一当前资产行为；Load 按消费者校验错误资产类型仍是后续问题。
- 本次归属裁决针对这套生产工具公共骨架；项目已有 InteractionManager 等业务框架未改。
