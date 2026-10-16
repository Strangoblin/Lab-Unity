# Window Templates

**定位**:编辑器工具的 GUI 窗口形态。窗口只做设置、行动与预览；生成算法放在 baker service / generator facade 等服务层。
窗口不是单一职责族的专有物，归属裁定见 `.agents/memory/unity/2026-09-03-standard-code-window-family.md`。

## 按窗口职责取用

| 形态 | 入口 | 落地要求 |
|---|---|---|
| 纯 GUI，不产出资产 | [editor-window.cs](editor-window.cs) 的菜单 / 设置 / 预览段 | 直接继承 `EditorWindow`，只取需要的 GUI 部分 |
| 生成或烘焙资产 | [production-tool/](../production-tool/README.md) | 复制完整独立 `EditorWindow` 模板，在 staging 替换业务占位符，审核后经现有门禁合入工具目录 |

`editor-window.cs` 保留历史落盘示例；新资产产出窗口统一使用 production-tool 完整模板，采用原位更新与目标资产保存契约。
项目无需引用 agent 中的框架，也无需安装共享基类或路径注册表。

## 共用 GUI 要点

- 菜单入口 + `GetWindow<T>(false, title, true)`；类 `sealed` + `: EditorWindow`。
- 窗口脊柱：标题 → 设置分区 → 行动行 → Preview / Info；资产产出窗口在行动行前增加路径行与 Browse。
- 设置按 boldLabel / box 分块，序列化字段用 `_` 前缀；UI 文案英文。
- 行动按钮统一 `Height(30)`，禁用态通过 `DisabledScope` 表达。
- 归位 `category.Script` 解析且已授权的 `<Tool>/Editor/`；窗口不承担服务层算法。

## 资产产出窗口的消费差异

以下行为由 production-tool 完整模板带入具体窗口内部，路径由窗口本地字段持有，Save 与 Load 共用同一来源。

| 族 | 行动行 | 特有行为 | 服务层 |
|---|---|---|---|
| Baker | `[Bake][Load][Save]` | Bake 带进度条与 try/finally；场景输入未就绪时禁用 Bake | [baker/baker-service.cs](../baker/baker-service.cs) |
| Generator | `[Generate][Load][Save]` | 不显示生成进度条；预览可用 hash 缓存，切换预览不改写已载入资产 | [generator/generator-facade.cs](../generator/generator-facade.cs) |

保存前覆盖确认，同类型已有资产原位更新以保留 GUID，只保存目标资产。
窗口拥有自己生成的对象，换源或关闭时释放；`AssetDatabase` 载入或落盘的资产由资产系统管理。
完整保存与所有权契约见 [production-tool/](../production-tool/README.md)。

## 历史项目范例

以下地址仅供溯源，当前地址经资源接口解析。

| 范例 | 路径 | 学习点 |
|---|---|---|
| NoiseGenerator Window | `Assets/Mine/Scripts/NoiseGenerator/Editor/` | 模式 toolbar + 设置分区 + hash 缓存预览 |
| CurveGenerator Window | `Assets/Mine/Scripts/CurveGenerator/Editor/` | Scene 预览形态 |
| InteriorMap Baker Window | `Assets/Mine/Scripts/InteriorMapBaker/Editor/InteriorMapBakerWindow.cs` | Bake / Load / Save + 状态 HelpBox + 2:1 预览 |
| FGD LUT Baker Window | `Assets/Mine/Scripts/FGDLutBaker/Editor/FGDLutBakerWindow.cs` | 纯参数烘焙形态 |

新窗口归位 `<Tool>/Editor/`。纯 GUI 窗口无需 URP 或运行时组件依赖；具体工具按服务层需求取用依赖。
