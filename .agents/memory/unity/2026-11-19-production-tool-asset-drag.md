# 生产工具窗口 Asset Path 拖拽载入

> 日期: 2026-11-19
> 范围: `Assets/Mine/Scripts/{NoiseGenerator,NormalSmoother,HDRMapBaker,HDRMapConverter,CurveGenerator,FGDLutBaker,FabricLutBaker,SSSLutBaker}`

## 起因（已验证）

`Noise Generator` 的 Load 对 `Assets/Textures/Noise2DRG.asset` 无效，不是资产问题：
`LoadFromAsset()` 只走 `AssetFiles.Load(AssetPath)`，而 `_assetPath2D` 默认值是
`Assets/Textures/Noise2D.asset`——该文件不存在。Editor.log 留证：
`Noise Generator: no asset at "Assets/Textures/Noise2D.asset".`（15:14:22）。
失败只 `Debug.LogWarning`，窗口无任何可见反馈，所以表现成「点了没反应」。
另一个静默分支：3D 模式下载入 Texture2D，`Current3D` 为 null，预览退回程序化噪声。

## 改动

- 新增共享落点 `Assets/Mine/Scripts/ProductionTools/Editor/AssetPathDropTarget.cs`：
  `TryConsumeWindowDrop(window, predicate, out asset)` 处理 `DragUpdated`/`DragPerform`，
  只接受 `AssetDatabase.GetAssetPath` 非空的资产，类型不符显示 `Rejected`。
- 8 个窗口在 `OnGUI` **第一行**调用各自的 `HandleAssetDrop()`（在任何控件、任何滚动视图之前），
  松手后写回 Asset Path 并直接调用各自的 `LoadFromAsset()`；Browse 与手工编辑路径行为不变。
- NoiseGenerator 额外按拖入类型切换输出模式（Texture3D / Texture2D），否则路径会落到另一条槽位。

## 第一版为什么无效（重要）

第一版把落点放在 `DrawAssetPathRow` 里，用 `GUILayoutUtility.GetLastRect()` 取路径框矩形，
在 `EndHorizontal()` 之后判定。实测（用户拖 `Noise2DRG.asset`）无反应，只留下 Load 的
`no asset at "Assets/Textures/Noise2D.asset"` 警告 —— 路径根本没被改写，说明 `TryConsume` 返回了 false。

探针实验确认：`EditorWindow.SendEvent` 能驱动 OnGUI，合成 `DragUpdated` 时
**Unity 会先补一次 Layout**，所以 `GetLastRect` 恰好是对的（rect 命中时 `inside=True`）；
真实拖拽不会补 Layout，`lastRect` 可能停留在别的控件上。此外路径框本身是
`EditorGUI.TextField`（自带对象拖放处理），又包在 `BeginScrollView` 里（会改写
`Event.current.mousePosition`）—— 三者叠加，row 级判定不可靠。
改为窗口级矩形（`position.size`）+ `OnGUI` 顶部处理，三处依赖全部消失。

## 可复用陷阱

- **枚举名**：Unity 6 是 `DragAndDropVisualMode.Rejected`，没有 `Reject`（CS0117）。
- **合成拖拽不可行**：`SendEvent(DragPerform)` 能把事件送进 OnGUI，但 Unity 会清空
  `DragAndDrop.objectReferences`（即使刚给它赋值），所以 accept / 写回分支只能靠人工验证；
  脚本能验的只有「事件到达、矩形命中、不抛异常」。
- 判定拖拽落点时不要依赖 `GUILayoutUtility.GetLastRect()`：真实拖拽没有 Layout 事件。

## 验证

- `unityctl asset refresh`：编译通过，`logs --level error` 无 error。
- `Validation~/ProductionToolChecks.cs`：PASS 46 checks。
- 8 个窗口 `SendEvent` Layout / Repaint / DragUpdated / DragPerform 全部 ok，无异常。
- **未验证**：真实鼠标拖拽松手后的载入（合成事件没有 objectReferences）。默认路径仍指向不存在的
  `Noise2D.asset` / `Noise3D.asset`，本轮未改。
