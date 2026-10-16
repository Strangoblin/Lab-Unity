# GizmoRender — 统一 Gizmo 框架接手与归档

## 结果

用户在本会话明确确认精确更名清单，已通过 move_gated 同核 CLI 完成迁移，随后 gated 写入最终源码。`Assets/Mine/Scripts/GizmoRender/` 为正式目录，入口 `GizmoRender`，菜单 `Tools/GizmoRender`，命名空间 `Mine.GizmoRendering`。旧目录已移除，13 份 `.meta` 逐字节保留，GUID 与配置字段不变；六个接入组件仅替换命名空间和调用入口。

功能说明：[GizmoRender.md](../../../Assets/Mine/Scripts/GizmoRender/GizmoRender.md)。可复现回归：[Validation~/README.md](../../../Assets/Mine/Scripts/GizmoRender/Validation~/README.md)。精选证据：[evidence/2026-10-16-gizmo-render](evidence/2026-10-16-gizmo-render/)。精确迁移映射随证据保存为 `rename-manifest.json`。

## 结构收敛

- 保留 Source / 分派 / Canvas / 成员集合 / 窗口分层；配置、几何和编辑各自单一职责，无需新增 registry 或组件基类。
- `NormalizeLabel` 收口所有键处理：null / 空白归为 `Unassigned`，其余 Trim。修复空键重复扫描反复新增成员且绘制无法找到配置的问题。
- `AddMember` 产生唯一后缀，`TryRename` 拒绝空值与重名；Label setter 为 internal，窗口用延迟提交避免每键保存和冲突组。
- 实时 Sources 不序列化，读取时清理 Unity 已销毁和已改组的来源；接口的普通 null 判断不能识别 Unity fake-null。
- 分派解析统一 Attach，包括隐藏、禁用及临时成员，删除重复 IsPersistent 分支。扫描仅在新增配置时保存一次，窗口不再无条件保存；使用 `SaveAssetIfDirty`，不保存整个会话。
- 每次 Draw 从世界矩阵 / 成员基色开始，finally 成对恢复 color / matrix / BaseColor；嵌套和异常都不污染外层。

保留标签作为成员身份。改名不改来源，删除后旧标签可能在扫描时重新注册，隐藏用 Enabled。旧 EditorPrefs key `Mine.SceneGizmo.Policy` 保留用户选择；文档明确它是持久化 Editor override。`Mine.GizmoRendering` 避免命名空间与 UnityEngine.Gizmos 或入口类型撞名。

## 验证

- 最终实际代码 `unityctl asset refresh`：compilation succeeded。
- 从正式 `GizmoRender/Validation~/GizmoRenderChecks.cs` 运行：67/67 通过，48 种 Enabled × Presence × Policy × callback pass，同时核对显隐查询与实际分派次数。
- 覆盖空键重复扫描 / 来源去重、唯一新增 / 校验改名、改组 / Unity 销毁清理、隐藏 / 禁用组清单、嵌套 / 异常状态恢复、六种实际组件接口与双回调、缓存 / 偏好 / 场景脏状态恢复；故意错误断言被捕获，避免 harness 空转。
- 首轮 2 项误报来自 Gizmos.color 的 Color32 量化；独立 SameStroke 比较量化后的值，BaseColor 保留浮点比较。最终回归使用修正后的断言。
- 最终新命名空间框架内存编译：Editor 9 文件 / Player guard 8 文件均 0 error。Player guard 是条件编译检查，不是 Player 构建。
- 13 份迁移 meta 完全相同；成员配置除 m_Name / m_EditorClassIdentifier 更名外逐字节相同；Wind / Instance / Interior / Terrain 四个成员成功加载；菜单路径已反射确认。
- 六个消费者的绘制体、默认组和模拟代码与接手基准相同。原 SampleScene / PC_Renderer 磁盘指纹保持；场景与配置均未脏，未 Play，未保存场景 / Renderer。
- 实际调用 OpenCode CLI `opencode-go/deepseek-v4.1-flash` 一次，只读检查六组件的调用、坐标与几何，确认无直接 Gizmos 残留；独立 explorer 复核最终源码、文档和相对链接通过。

回归不发出真实几何，不证明人工窗口点击或图元画面。交互 Handles 留在工具侧；授权 Scripts 范围外的 GrassGenerator 未迁入，不能称全工程 Gizmo 已全部统一。

## 归属与回退

接手时 HEAD `f9b3ad8`，SceneGizmo 全部 untracked，六组件为该任务既有 WIP，另有大量无关 dirty。以任务专属临时 Git index 建立两个阶段提交：先保存接手 WIP 作为 Git 回退点，再提交最终更名、结构修复和验证归档。只选择该框架及六组件；InteriorMapBaker 文档只提交 Gizmo 关联段落、组件字段和形制说明，其余编辑仍留在工作区。未 stash、未推送。

迁移前哈希检查拦住了并行会话对 InteriorMapBaker.md 的更新（生产工具说明改回独立 EditorWindow）；已合并保留该更新，仅替换 Gizmo 引用。没有覆盖 ProductionTools、场景、Renderer 或体系迁移的既有工作。MEMORY 索引在本工作区更新，未把整份既有未跟踪索引迁移混入自动提交。

任务延续使用 `tmp/sessions/2026-10-15-scene-gizmo/`。长期验证源码已进入功能 Validation~，必要结果已进入 memory/evidence。接手前的 `staging/gizmo-member-check.cs` 原件保持；本轮构造的可重建暂存 / fixture 不作为第二套功能源码。
