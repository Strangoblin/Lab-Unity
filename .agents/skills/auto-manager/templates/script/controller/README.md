# Controller Templates

**定位**:单物体/单系统行为控制。public API 极简(Move/Impulse/SetTarget 类命令),不跨场景管理生命周期;常被 Manager 多态引用,也可独立挂载。

**族内结构**:核心层 [controller-processor.cs](controller-processor.cs)(接口 + 实现双段骨架)。

**骨架要点**:
- 两种形态:纯 C# class(被 Manager 持有并轮询/驱动)或轻量 MonoBehaviour(挂在被控物体上)
- **接口化优先**:定义能力接口(如 `IInteractionProcessor`),Manager 只依赖接口 → 新行为 = 新实现,Manager 零改动
- 序列化参数 `[SerializeField]`,方法单一职责、以命令式 API 为主,尽量不暴露内部状态
- 更新循环选择显式声明:帧驱动(`Update`/`FixedUpdate`)或事件驱动(由 Manager/输入回调调用),避免两个都开
- 组合优于继承:行为拆分独立组件(Mover/Steerer),Controller 负责组装与对外 API

**历史项目范例（仅溯源，当前地址经资源接口解析）**:

| 范例 | 路径 | 学习点 |
|------|------|--------|
| WaterInteractionProcessor | `Assets/Mine/Scripts/InteractionManager/Water/WaterInteractionProcessor.cs` | `IInteractionProcessor` 接口实现,被 UniversalInteractionManager 驱动 |
| CamController | `Assets/Mine/Scripts/CamController/CamController.cs` | 单物体相机控制入口 |
| RigidbodyMover | `Assets/Mine/Scripts/CamController/RigidbodyMover.cs` | 与 Controller 组合的独立运动组件 |

**与相邻族边界**:
- vs Manager:Controller 不编排他人、不持有系统级生命周期;反向依赖(Manager → Controller 接口)
- 纯参数控制器(无行为逻辑)通常可直接用 Inspector 序列化字段,不需要额外类
