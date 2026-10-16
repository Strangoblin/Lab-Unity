# 生产类工具统一框架（Generator / Baker）

## 用户目标与范围

- 检查 Generator / Baker 等生产类工具，建立**统一标准框架**：参考 `InteractionManager` 的同构分工，固定 Save/LoadPath、统一 Bake/Load/Save 基础功能；其他独立功能（设置分区、预览）暂不整合，留待后续。
- 工作区 `Assets/Mine/Scripts`。未 Play、未保存场景 / Renderer、未提交、未推送。HEAD 基准 `f9b3ad8`，大量既有 dirty（`.agents` 体系迁移、`Debug/` → `Special/Debug/`、`SceneGizmo/` WIP）保持不动。
- **并行编辑**：用户在同一时段改动 `.agents/`、`SceneGizmo/SceneGizmos.cs`（19:05）、`InteriorMapBakeVolume.cs`（19:07）等；编译一度因其 WIP（`GizmoMemberSet` CS0103）中断，非本次改动引入，按要求未介入修复。

## 改造前状态 — 同一骨架的五份分叉

| 工具 | 路径默认 | 行动行 | 私有重复 |
|---|---|---|---|
| CurveGenerator | `Assets/Mine/Curves/NewCurve.asset` | Generate / — / Save | `EnsureFolders` |
| NoiseGenerator | `Assets/Mine/Noises/Noise2D(3D).asset` | Generate / — / Save | `EnsureFolders` |
| FGDLutBaker | `Assets/Textures/FGD_LUT.asset` | Bake / Load / Save | `EnsureFolders` `DrawPathField` |
| SSSLutBaker | **源码目录内**(错) | Bake / Load / Save | `EnsureFolders` |
| InteriorMapBaker | `Assets/Textures/InteriorMap.asset` | Bake / Load / Save | `EnsureFolders` `DrawPathField` |

实际烘焙资产全部落在 `Assets/Textures/`（FGD_LUT / SSS_LUT / Noise2D / InteriorMap），代码默认值已与事实漂移，SSS 默认指向源码目录。五个窗口各自实现保存，其中 Curve / Noise / FGD / InteriorMap 用 `DeleteAsset` + `CreateAsset`（**断 GUID 引用**）。项目 `templates/script/{window,baker,generator}` 早已声明期望形态，实码未跟进。

## 实现

- 新增 `Assets/Mine/Scripts/ProductionTools/Editor/`：`ProductionToolPaths.cs`（固定资产路径注册表）、`AssetFileUtility.cs`（路径校验 / 原位更新保存 / 回看 / 目录补齐）、`ProductionToolWindow.cs`（抽象 `EditorWindow` 骨架：`AssetPath` + `BakeLabel` + `BakeCore()` 三个契约成员，`DrawAssetPathRow()` / `DrawActionRow()` 共享 UI，自产资产所有权），加同目录 `README.md`。
- 五个窗口改为 `sealed ... : ProductionToolWindow`，只保留各自设置分区与预览分区；`BakeCore()` 调服务层（`FGDLutBaker` / `SSSLutBaker` / `NoiseGenerator` / `CurveBake` / `InteriorMapTextureBaker`），生成算法一行未动。
- 同构点：框架只管共享输入与流程，各工具自管输出 —— 与 `InteractionManager`（Manager 管共享输入、Processor 自管输出 RT）同形。

## 行为差异（有意）

| 差异 | 依据 |
|---|---|
| 保存改为原位更新（`CopySerialized`），保留 GUID | 旧 `DeleteAsset`+`CreateAsset` 会断材质 / 场景引用 |
| 改用 `AssetDatabase.SaveAssetIfDirty`，不再全局 `SaveAssets` | 避免顺带写回会话内存态（RendererData 前例） |
| 覆盖确认统一 | 仅覆盖**不同**资产时弹窗；Load 后直接 Save 不打扰 |
| 进度条统一 | Baker 型显示，Generator 型不显示（沿用模板分族） |
| Curve / Noise 新增 Load | 框架统一提供，两族原先无回看入口 |
| Noise 双模式只保留单一当前资产 | 切换模式会释放另一模式**未落盘**的生成结果（可再生） |

## 验证

- `unityctl -t 180 asset refresh` → `Asset refresh completed (compilation succeeded)`；`Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` 于 19:13:08 重建。
- `unityctl script eval "typeof(<Window>).BaseType.FullName"` 五个窗口全部返回 `Mine.Tools.ProductionToolWindow`（SSS 需全名 `Mine.SSSLutBaker.SSSLutBakerWindow`）。
- `python3 .mcp/validation/check_norm.py` 对 8 个改动文件全部通过。
- **未验证**：Bake / Load / Save 三按钮的实际点击路径（未在窗口内真实烘焙与落盘）、GUID 原位更新的实际效果、Noise 双模式切换行为。属人工验收项。

## 可复用经验（工具链）

- `unityctl asset refresh` 默认 60s 超时在 domain reload 时必返回 504，但刷新**实际已完成**；统一加全局 `-t 180`。上次编译有错时它直接返回 `Compilation errors exist from a previous build`，此时先查错再谈刷新。
- 编译真伪判定链（比 CLI 单次返回可靠）：`Editor.log` 的 `## Script Compilation Error for: Csc ... (+2 others)` / `*** Tundra build success` → `Library/ScriptAssemblies/<asm>.dll` mtime → `unityctl script eval typeof(X).BaseType.FullName`。
- `unityctl script lookup-type` 查不到本次的抽象基类 `ProductionToolWindow`（同批静态类 `ProductionToolPaths` / `AssetFileUtility` 能查到），但 `eval typeof(...)` 能取到 —— 判定「类型是否已编译加载」优先用 eval，不要据 lookup-type 未命中就判定未编译。
- `unityctl script eval` 中命名空间类型必须写全名；短名解析不到且不报错，只回一段 hint。
- 抽象 `EditorWindow` 基类里声明 `protected virtual void OnDisable()`，子类 `protected override void OnDisable()` + `base.OnDisable()` 可被 Unity 正常调用；子类若写 `private void OnDisable()` 会屏蔽基类导致自产资产泄漏。

## 收尾与后续

- **记录写入曾被门禁拦下**：`.agents/memory/` 不在 `.mcp` 作用域，`write_gated` 与 `gate_request_workspace` 均拒；通道规则与边界结论见 [meta 记录](../meta/2026-10-16-write-channel-rules.md)。当次草稿曾暂存 `tmp/sessions/2026-10-15-production-tool-framework/notes.md`，已随收尾删除。
- 本次改造净减 636 行重复（五个窗口）。同一会话把该框架总结进 skill：`templates/script/production-tool/`（新族 + 消费者骨架）、`templates/script/README.md`、`window/` `baker/` `generator/` README 与 `templates/README.md` 的路径规则修正、资源 ID `script.production-tools` 登记；见 [meta 记录](../meta/2026-10-16-write-channel-rules.md)。
- 仍待补：FGD / SSS / InteriorMap / Noise / Curve 缺同目录 `.md` 参数与使用文档。
- `Assets/Editor/GrassGeneratorEditor.cs` 是另一个遗留生产工具，位于 `Assets/Mine` 之外，未在本次授权范围；项目模板已把它标为「新工具勿仿」的反例。
- 待用户裁决：Noise 双模式是否恢复为「两个资产并存」（现为单一当前资产）；三按钮交互路径仍需人工验收。
