# GizmoRender — 统一 Gizmo 绘制框架

统一管理 Scene View 可视化的分组、显隐、颜色和公共图元。入口为 `Tools/GizmoRender`，代码命名空间为 `Mine.GizmoRendering`。

## 结构

```text
组件 IGizmoSource
  ├─ Member：成员标签；Form：形制
  ├─ OnDrawGizmos         → GizmoRender.Draw(this, false)
  └─ OnDrawGizmosSelected → GizmoRender.Draw(this, true)
          ↓
GizmoRender：成员解析 → Enabled × Presence × Policy → 绘制状态隔离
          ↓
GizmoCanvas：公共几何 + LocalSpace；GizmoTint：从成员基色派生颜色

GizmoMemberSet：GizmoRenderMembers.asset，持久化成员配置
  └─ GizmoMember：Label / Enabled / Presence / Color + 非序列化实时 Sources
GizmoRenderWindow：配置编辑、场景扫描、实时清单、全局覆盖
```

| 文件 | 职责 |
|---|---|
| `IGizmoSource.cs` | 组件声明成员、形制与绘制内容 |
| `GizmoRender.cs` | 唯一分派入口、覆盖策略、绘制状态恢复 |
| `GizmoCanvas.cs` | 盒、球、平面、半球、箭头、折线、管截面、线段 |
| `GizmoMemberSet.cs` | 加载、默认配置、统一成员键、增删改序、扫描注册 |
| `GizmoMember.cs` | 序列化配置与非序列化实时来源清单 |
| `GizmoPresence.cs` / `GizmoForm.cs` / `GizmoTint.cs` | 显示策略、形制分类、派生色 |
| `Editor/GizmoRenderWindow.cs` | Editor 窗口与操作入口 |

保留这几层：配置集合、绘制分派和图元有不同职责，实时清单足够由成员维护，无需再加 registry 或组件基类。`GizmoMemberSet` 留在根目录，因为运行程序集中的分派器需要引用它；其资产操作受 `UNITY_EDITOR` 保护，Player 中不执行 Gizmo 绘制。

## 成员与显隐

| 配置 | 默认值 / 含义 |
|---|---|
| `Label` | 成员键，区分大小写；统一去掉首尾空白，空来源键归为 `Unassigned` |
| `Enabled` | `true`；关闭后任何全局覆盖都不会绘制该成员 |
| `Presence` | `OnSelected`；可取 `Hidden` / `OnSelected` / `Always` |
| `Color` | 成员基色；未知标签用稳定标签 hash 生成颜色 |
| `Sources` | 非序列化清单；读取时剔除已销毁或已改组的来源 |

| 默认成员 | RGBA | Presence | 已接入组件 |
|---|---|---|---|
| `Wind` | 0.20, 0.80, 1.00, 0.85 | OnSelected | WindSource、WindFieldProvider |
| `Instance` | 0.30, 0.70, 1.00, 0.80 | OnSelected | FishSimulation、RainSimulation |
| `Interior` | 1.00, 0.75, 0.15, 0.95 | Always | InteriorMapBakeVolume、InteriorMapBakeDirection |
| `Terrain` | 0.60, 1.00, 0.60, 0.90 | OnSelected | 预留，GrassGenerator 尚未迁入 |

普通回调与选择回调是两个绘制通道：`Always` 仅走普通回调，`OnSelected` 仅走选择回调，`Hidden` 两者均跳过，因此同一来源不重复绘制。`Policy` 为 Editor 持久化覆盖：`Default` 沿用成员，其他值统一覆盖 Presence；仍受 Enabled 约束。更名后保留原偏好键 `Mine.SceneGizmo.Policy`，保留用户已有选择。

成员键在查找、临时回退、扫描和创建时共用 `NormalizeLabel`。`AddMember` 遇重名生成唯一后缀；显式改名通过 `TryRename` 拒绝空值和冲突。窗口采用延迟提交的标签编辑框，避免每次按键都落盘。

标签仍是成员身份：改名不修改组件的 `Member`，删除也不等于隐藏。仍被组件引用的旧标签在绘制时使用临时默认成员，下一次扫描会重新注册。需要隐藏一组时关闭 Enabled；需要不同颜色或显隐时拆成不同成员。

## 接入

```csharp
using UnityEngine;
using Mine.GizmoRendering;

public sealed class MyComponent : MonoBehaviour, IGizmoSource
{
    [SerializeField] private string _gizmoMember = "Terrain";
    public string Member { get => _gizmoMember; set => _gizmoMember = value; }
    public GizmoForm Form => GizmoForm.Volume;

    private void OnDrawGizmos() => GizmoRender.Draw(this, false);
    private void OnDrawGizmosSelected() => GizmoRender.Draw(this, true);

    public void DrawGizmos(GizmoCanvas canvas)
    {
        using (canvas.LocalSpace(transform.localToWorldMatrix))
        {
            canvas.WireBox(Vector3.zero, Vector3.one);
            canvas.Color = GizmoTint.Fade(canvas.BaseColor);
            canvas.WireSphere(Vector3.zero, 0.5f);
        }
    }
}
```

每次分派从世界矩阵和成员基色开始，局部几何显式使用 `LocalSpace`。分派在 `finally` 中恢复 `Gizmos.color`、`Gizmos.matrix` 和共享 `BaseColor`，嵌套或异常也不污染外层。组件不再自行保存全局绘制状态。

| API | 用途 |
|---|---|
| `GizmoRender.Draw(source, selected)` | 唯一绘制入口 |
| `ResolveMember(source)` / `IsVisible(source, selected)` | 无磁盘写入的成员解析、清单附加和显隐判断 |
| `Policy` / `Resolve(presence)` | 读取覆盖、计算实际显示情景 |
| `HideAll()` / `ShowAll()` / `ResetOverrides()` | 覆盖操作 |
| `GizmoMemberSet.Load()` / `Ensure()` | 只读加载 / UI 中按需创建配置资产 |
| `NormalizeLabel(label)` / `Find(label)` / `EnsureMember(label)` | 统一键、查找、注册 |
| `AddMember(label)` / `TryRename(member, label, out error)` | 唯一新增 / 校验后改名；配置变化即保存 |
| `RemoveMember(member)` / `MoveMember(member, offset)` | 移除 / 调整顺序；配置变化即保存 |
| `RosterFrom(sources)` | 重建实时清单；仅新增成员配置时保存一次 |
| `GizmoCanvas.LocalSpace(matrix)` | 可嵌套矩阵作用域 |
| `GizmoTint.Fade(color, alphaScale = 0.35)` | 次级范围色 |
| `GizmoTint.Emphasis(color, lighten = 0.35, alphaScale = 1)` | 强调方向或端点 |

Canvas 公共图元为 `WireBox`、`WireSphere`、`PlaneRect`、`WireHemisphere`、`Arrow`、`Line`、`Polyline`、`TubeFrames`。箭头共用四边箭头头部，省略头部参数时由长度推导；半球使用固定 6 环 / 24 段；管截面默认 32 区间 / 16 段，沿用固定参考轴构架，接近竖直切线时可能滚转。

## 范围与迁移

本次从 SceneGizmo 更名为 GizmoRender，目录、入口、窗口、配置和说明统一命名；`.meta` 随文件迁移，GUID 保留。六个接入组件只同步命名空间与入口，保留接手时的几何、默认组和模拟行为。配置的成员字段保持原序列化字段名。

框架建立时已统一各组件手写箭头、圆环和状态恢复，成员颜色替代各组件的独立色值；旧 `_drawGizmo` / `_showArea` / `_gizmoColor` 已由 `_gizmoMember` 取代。这属于接手前迁移，不应在下次保存场景时误以为仍有旧独立开关。

交互 `Handles` 继续留在工具侧；`Shaders/Render/SimpleGrass/GrassGenerator.cs` 在本轮 Scripts 范围之外，尚未迁入，不宣称全工程可视化已全部统一。

功能回归入口与验证边界见 [Validation~/README.md](Validation~/README.md)。项目脚本结构规范见 [script-structure](../../../../.agents/knowledge/unity/standard/script/script-structure.md)。
