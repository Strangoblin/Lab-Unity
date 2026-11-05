# Manager Templates

**定位**:运行时场景编排组件(MonoBehaviour)。持有并驱动多个组件/处理器,跨帧协调一个完整系统;不做资产产出、不建窗口。

**族内结构**:核心层 [runtime-manager.cs](runtime-manager.cs)(MonoBehaviour 编排骨架)。

**骨架要点**:
- `MonoBehaviour` + `[SerializeField]` 场景引用(组件、Transform、材质);帧循环在 OnEnable/OnDisable 对称注册/注销
- `[ExecuteAlways]` 若需编辑模式运行 → 对象销毁必须按模式分支:**播放模式 `Destroy` / 编辑模式 `DestroyImmediate`**(2026-08-27 POSSManager 实坑)
- 错误处理遵循项目约定:同一错误 3 次后兜底退出,不无限重试
- 自定义 Inspector 两种先例并存:
  - 同文件嵌套 `class XxxEditor : Editor`(`InteractionManager.cs` 内 `UniversalInteractionManagerEditor`,L230 起)
  - `Editor/` 独立文件(`IK/Editor/ActiveRagdollManagerEditor.cs`);交互式场景搭建走 Editor 工具类(如 InteriorMapBaker 的 `Editor/InteriorMapBakerEditorUtility.cs`)
- 命名统一 `<Name>Manager`;单例化与否按实际需要,勿默认单例

**历史项目范例（仅溯源，当前地址经资源接口解析）**:

| 范例 | 路径 | 学习点 |
|------|------|--------|
| UniversalInteractionManager | `Assets/Mine/Scripts/InteractionManager/InteractionManager.cs` | Manager/Processor 分离 + 嵌套 Editor + 处理器接口驱动 |
| UniversalInstanceManager | `Assets/Mine/Scripts/InstanceManager/UniversalInstanceManager.cs` | 同文件嵌套 Editor(L169 起) |
| FGDLutManager | `Assets/Mine/Scripts/FGDLutBaker/FGDLutManager.cs` | sealed 精简单组件形态 |
| ActiveRagdollManager | `Assets/Mine/Scripts/IK/` + `Editor/ActiveRagdollManagerEditor.cs` | Editor/ 独立 Inspector 文件先例 |

**与相邻族边界**:
- vs Controller:一个系统 = 一个 Manager 编排,多个被控物体各挂/各持 Controller(或纯 C# processor)
- vs Baker/Generator:Manager 是运行时组件,烘焙/生成类工具见各自家族 README
