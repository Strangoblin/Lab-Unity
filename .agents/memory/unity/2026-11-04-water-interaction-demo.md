# 水体互动 Demo 场景与整套交互配置验证（2026-11-04）

`Assets/Mine/Scripts/InteractionManager/WaterInteractionDemo.unity` 把「正交交互相机 + `IA_Renderer`(InteractionCaptureFeature) + `UniversalInteractionManager` + `WaterInteractionProcessor`」整条链路落成一个可运行场景，可被叠加加载。

## 场景结构（层是关键）

| 物体 | 层 | 作用 |
|---|---|---|
| `WaterSurface` | 6 `Interaction` | 10×10 平面，`InteractionDebug` 材质：只进深度捕获，主相机看热力图 |
| `InteractorProbe` | 7 `Interactor` | 半径 0.75 的球 + `InteractorObject` 材质：一个物体两个 Pass，各由一个相机渲染 |
| `InteractionCamera` | 0 | 正交，Renderer Index=1，SolidColor 黑，mask=6\|7 |

- **一个物体两个 Pass 靠 LightMode 分流**：`LightMode=InteractionCapture` 只被 `InteractionCaptureFeature` 的颜色捕获命中（该 Tag 排在 `ForwardTags` 最前），`LightMode=UniversalForward` 归主相机的标准 URP 前向。标准前向只认 `SRPDefaultUnlit/UniversalForward/UniversalForwardOnly`，两边天然互不可见，不用再挂可见外壳。
- **layer 7 必须进主渲染器的层掩码**：`PC_Renderer.asset` 的 `m_OpaqueLayerMask` / `m_TransparentLayerMask` 原来是 `119`（0,1,2,4,5,6），7 被排除，主相机连 `UniversalForward` 都画不出来；改成 `247` 才有画面。`IA_Renderer` 的 depth=64 / color=128 独立，不受影响。
- **改完 shader 源码要 `AssetDatabase.ImportAsset(..., ForceUpdate)`**：否则材质继续用旧编译版本（`HasProperty("_BaseColor")` 为 false、颜色读出 0），表现为物体渲染成黑色、在黑背景上根本看不见。
- **`InteractionDebug._BaseColor` 要给到 8**：调试 Shader 输出 `高度 × _BaseColor`，凹陷饱和到 -1，但向外扩散的涟漪只有 0.05 量级，2.5 倍增益在 Scene 视图里几乎全黑。

- **交互物体的外观 Pass 不能放 layer 6**：layer 6 是深度参照，物体的深度会被当成「场景表面」，把按压深度吃掉。
- `InteractionCamera` 的 Target Texture 存 `{fileID: 0}`，由 Manager 运行时创建 RT 并赋值；这样重开场景不会多一份孤儿 RT。
- 场景不挂 `AudioListener`：它经常和别的场景叠加加载，两套监听器只会刷警告。

## 验证数据（Unity 6000.3.14f1 / URP 17.3.0 / Metal，Play 模式）

暂停后逐帧 `play step`，比较「探针世界坐标 → 正交矩阵投影的像素」与「捕获/波面的加权质心」：

```
probe       : world(2.278, 0.092, -1.030) → pixel(186.3, 101.6)
capture     : max 0.657  centroid(185.8, 101.1)  非零 1108
water       : min -0.657 centroid(185.8, 101.0)
逐帧校验和  : -524.8 → -469.6 单调变化
```

质心与探针像素差 < 1px，说明世界→RT 的映射在「相机 / Feature / 全局矩阵 / Compute」四处是一致的；校验和逐帧变化说明 Manager 的 `Update` 确实每帧在跑 `Process`。退出 Play 无 error / warning。

## 踩到的坑

- **编辑模式下必须自己推帧、自己渲染**：`[ExecuteAlways]` 的 `Update` 在编辑器里只在场景变化/重绘时被调用，波方程不会自己推进；URP 也不会替带 Target Texture 的相机在编辑模式下自动渲染，`originRT` 恒空。修法是 `EditorApplication.update` 里按 30 Hz「`SubmitRenderRequest` 渲染一次 → `Process` → `SceneView.RepaintAll()`」。只 `Repaint()` `lastActiveSceneView` 不够，分屏场景会停在旧画面。**这条链路本身在拖动时是工作的**——模拟「拖 gizmo」只需 `transform.position = …`，实测水体质心会跟着走。
- **`GameObject.Find` 找不到未激活的对象**：调试脚本里用它找被 `SetActive(false)` 的水面会返回 null，得遍历 `scene.GetRootGameObjects()`。
- **`screenshot window Scene` 拿到的是缓存帧**：Scene 视图窗口截图会滞后于实际画面（改了 camera pivot 后图还是旧的），判断「Scene 视图里到底有没有效果」要用 `RenderPipeline.SubmitRenderRequest(SceneView.camera, …)` 直接渲一张。
- **`ComputeShader.SetTexture(kernel, id, null)` 会抛 `ArgumentNullException`**：`WaterInteractionProcessor.Release()` 原用它解绑，异常发生在 `ReleaseRT` 之前，于是每次退出 Play 模式都报错并泄漏两张 RT。改为只释放 RT（下次 `Initialize` 会重新绑定，中间没有 Dispatch）。
- **Editor 在后台时 Play 循环会被节流**：`Time.frameCount` 卡住不动、`unityctl script execute` 仍能返回；`play status` 显示 playing 但帧不前进。判定：连续两次读 `Time.frameCount` 不变。解法是 `play pause` + 反复 `play step` 手动推帧，测量才是确定性的。（这条也解释了「暂停后隔几秒再测，波纹已经衰减」的现象。）
- **叠加加载时别的场景会盖住画面**：GrassManagerDemo 的绿色地形铺在同一片 y=0 区域，主相机渲染出来是一整片橄榄绿。测量前遍历非目标场景的根物体 `SetActive(false)`（Play 模式下的改动退出即还原，不会脏化用户场景）。

## 复现方式

```
unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/BuildWaterInteractionDemo.cs"
```

幂等：复用/更新 `Materials/` 下三个材质，用 Additive 场景搭建后 `SaveScene` 再 `CloseScene`，不触碰当前打开的场景与未保存修改。
