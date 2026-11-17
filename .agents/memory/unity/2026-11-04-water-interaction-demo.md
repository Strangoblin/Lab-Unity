# 水体互动 Demo 场景与整套交互配置验证（2026-11-04）

`Assets/Mine/Scripts/InteractionManager/WaterInteractionDemo.unity` 把「正交交互相机 + `IA_Renderer`(CustomFeature) + `UniversalInteractionManager` + `WaterInteractionProcessor`」整条链路落成一个可运行场景，可被叠加加载。

## 场景结构（层是关键）

| 物体 | 层 | 作用 |
|---|---|---|
| `WaterSurface` | 6 `Interaction` | 20×20 平面，`InteractionWater` 材质（Shader `Render/Water`）：交互 RT 在这里变成位移 + 法线。**同时是穿透深度的参照面**（`DepthOnly` Pass 不做位移，`TransparentDepthMergeFeature` 合进 `_CameraDepthTexture`） |
| `InteractorProbe` | 7 `Interactor` | 半径 0.75 的球 + `InteractorObject` 材质：一个物体两个 Pass，各由一个相机渲染 |
| `InteractionCamera` | 0 | 正交，Renderer Index=1，SolidColor 黑，mask=6\|7 |
| `SeaBed` | 0 | 60×60 海底，y = -5，`SeaBed` 材质（URP Lit 深青） |
| `Background/SeaWall_N·S·E·W` | 0 | 22 m 高的四面墙，围住水域挡地平线 |
| `FieldManager` + `FFT` | 0 | `Mine.Fields` 与 `WaveFieldProvider`，发布 `_WaveDisplacement0..2` 等全局量 |

- **一个物体两个 Pass 靠 LightMode 分流**：`LightMode=InteractionCapture` 只被 `CustomFeature` 的颜色捕获命中（该 Tag 必须排在 `ForwardTags` **最后**，见下方「左右过冲 / 上下无反应」），`LightMode=UniversalForward` 归主相机的标准 URP 前向。标准前向只认 `SRPDefaultUnlit/UniversalForward/UniversalForwardOnly`，两边天然互不可见，不用再挂可见外壳。
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

---

# 接到真正的水面 Shader（2026-11-05）

水面板从 `InteractionManager/InteractionDebug` 换成 `Render/Water`，交互 RT 成为位移与法线的输入，FFT 波浪同时由 `FieldManager` 发布。新增 `Shaders/InteractionField.hlsl`（材质侧采样契约）：

```hlsl
// 与 InteractionDebug.shader 完全一致的映射
float4 clip = mul(_InteractionOrthoP, mul(_InteractionOrthoV, float4(positionWS, 1.0)));
float2 uv   = clip.xy / clip.w * 0.5 + 0.5;
```

`Water.shader` 的两个挂点（`WaterFunction.hlsl`）：

| 位置 | 调用 | 说明 |
|------|------|------|
| Domain | `positionWS += ComputeInteractionWave(positionWS)` | `SampleInteractionHeight × _InteractionHeightScale` |
| Frag | `normalWS = ComputeInteractionNormal(positionWS, normalWS)` | 中心差分 `InteractionSlopeXZ` → `ApplyInteractionSlope` |

- `SampleInteractionHeight` 用 `step(abs(uv - 0.5), 0.5)` 遮掉域外：RT 是 Clamp 采样，不遮的话交互域外会拖出一整片同高的平台。
- 差分步长取一个 RT 纹素的世界尺寸（`_InteractionAreaSize / _InteractionResolution`），改分辨率时法线粗细自动跟着变。
- `ApplyInteractionSlope` 先把法线还原成坡度（`-normal.xz / |normal.y|`）再叠加，避免两个法线 `lerp` 掉长度。
- 水面放大到 20×20 后 `_TessellationFactor` 提到 32；`_InteractionHeightScale` = 2.5（必须小于海口深度，否则水面会穿到海底下面）。

## 新踩的坑

- **`WaveFieldProvider` 会把自己关掉**：`FieldProvider.OnEnable` 里 `InitializeField()` 返回 false 就 `enabled = false`，场景一保存就永久停着。表现是水面平坦、**控制台没有任何 error**，只有 `_WaveFieldValid == 0`、`_WaveDisplacement0 == UnityBlack` 能看出来。重新勾上 `enabled` 后立刻发布（`patch0 = 40`，纹理 `Wave_C0_Disp`）。
- **没有海底时焦散整屏爆白**：`ComputeCaustics` 的 `sceneDepDf = 水面y - 场景y`；水面下方没有几何体时它等于到远裁剪面的距离，`eps = _CausticsScale*(1+sceneDepDf)` 与 `intensity *= sceneNorDf*sceneDepDf` 一起飞掉。Demo 加 `SeaBed`（y = -5）后 `center` 从 `(1.00,0.98,0.98)` 回到 `(0.18,0.45,0.55)`。
- **Plane 是水平的**：`GameObject.CreatePrimitive(Plane)` 的法线是 +Y，只设 yaw 的「四面墙」仍然是躺在地上的平板，会整屏糊住画面。立起来要用 `Quaternion.FromToRotation(Vector3.up, inward)`。
- **读 RT 别只读角落**：`Texture2D.ReadPixels(0,0,64,64)` 读的是左下角一块，不是整张缩略图；用它统计「全黑」会把正常画面误判成空的。要全量统计就按 `rt.width/height` 读满再抽样。
- **重建脚本的三条硬限制**（`BuildWaterInteractionDemo.cs`）：① `SaveScene(scene, path)` 不能覆盖**已加载**的场景（"Overwriting the same path as another open scene is not allowed"）；② 目标场景是唯一加载场景时 `CloseScene` 直接失败（"Unloading the last loaded scene is not supported"）；③ 已有未保存的未命名场景时 `NewScene(Additive)` 失败（"Cannot create a new scene additively with an untitled scene unsaved"）。可行顺序：先 `NewScene(EmptyScene, Additive)` 做垫片 → `CloseScene(目标)` → 用 `NewScene(EmptyScene, Single)` 替换垫片构建 → 保存 → `OpenScene(Single)` 重开目标。
- **`AddComponent` 会立刻触发 `OnEnable`**：给 `WaveFieldProvider` 先 `AddComponent` 再写 `_fftCS`，OnEnable 时 `_fftCS` 还是 null，它会报错并把自己 `enabled = false`。先 `SetActive(false)` 上物体、写序列化字段、再 `SetActive(true)`。
- ~~**主相机不要画 layer 6**~~（2026-11-05 失效）：以前 layer 6 是只给捕获相机看的 `DepthReference` 热力图平面，主相机得排除；现在 layer 6 是水面本身，主相机 `cullingMask = -1` 必须画它。
- **Caustics 全强度在平整海底上是噪声**：`_CausticsIntensity = 1` 会在海底织出一整片白色网格，把交互涟漪淹掉；0.25 只留几缕。交互涟漪真正看得见的是 `_InteractionNormalStrength`（1.0 几乎看不出，2.5 才明显）。

## 验证数据

```
_WaveFieldValid          : 1（FFT_C0..2_Disp 已发布）
_InteractionWaterTex     : min -1.000  max 0.019  非零框 (106,106)-(149,149)
探针移到 (2.5,-0.5,1.5)  : 凹陷质心 (63.5, 89.5)，成图上凹陷跟随，原位留一圈余波
四边遮挡                 : Main Camera 背景临时改洋红 → 洋红像素 0.00%
重建脚本                 : BuildWaterInteractionDemo.cs 就地重建（场景开着时也能跑）→ 10 个根物体，FFT enabled，waveValid 1.00
成片                     : mean 0.361，白色占比 0.50%，涟漪靠法线（_InteractionNormalStrength 2.5）在探针周围成同心环
```

---

# 交互改成帧间差值 + 水面粘稠度（2026-11-05 下午）

问题：物体只要泡在水里就一直把水面压出一个恒定的坑（`h_new = lerp(h_new, -depth*force, w)` 每帧把接触区钉住），静止时也在扰动水面。用户给的判定是**取 RT 差值**。

## 机制

`WaterInteraction.compute` 里去掉「绝对值钉住」，改成：

```hlsl
float depthPrev = _InteractionOriginPrevTex[id.xy];   // RWTexture2D，本线程读自己写自己，无竞争
float delta     = depth - depthPrev;
float contact   = saturate(max(depth, depthPrev) * 4.0);  // 抬离那帧 depth 已归零，只取本帧会漏掉吸起
h_new += -delta * _ObjectForce * contact;
_InteractionOriginPrevTex[id.xy] = depth;
h_new = clamp(h_new, -1.0, 1.0);
```

- 静止 → `delta == 0` → 不注入能量；拖动 → 前缘下压、后缘吸起，形成尾迹；停下后按阻尼衰减回平。
- `_StaticPress`（0–1）保留旧行为的开关，默认 0。
- 上一帧深度 RT 由 Processor 建（`_originPrevTex`）并在 `Initialize` 绑定、`Release` 释放。
- **必须钳位**：捕获出来的深度是**二值盘**（球背面的穿透都 ≥0.5 m，`saturate(diff*20)` 全饱和到 1），移动时边缘整帧跳 0↔1，冲量会累积到 ±6；钳位到 ±1 后峰值稳定在 ±1。

## 粘稠度

原实现只有 `_Damping`（每帧乘整个场）和 `_WaveSpeed`，两个都只会缩短传播范围。新增两项：

| 参数 | 项 | 效果 |
|------|----|------|
| `_Viscosity` | `h_new -= γ·(h_curr − h_prev)` | 速度阻尼：振幅快速衰减但传播速度不变 |
| `_Smoothing` | `h_new = lerp(h_new, 邻域平均, s)` | 表面张力：波峰被抹宽抹圆 |

实测（30 Hz，拖 2.5 m，拖完静置）：
```
γ=0     : t+1s max 0.989 非零 15.8% → t+5s max 0.080 非零 19.0%（波纹铺满）
γ=0.08  : t+1s max 0.326 非零 13.4% → t+5s 全零
γ=0.25  : t+1s max 0.611 非零 11.6% → t+5s max 0.041 非零 8.7%
```

## 验证（`tmp/VerifyStaticVsMoving.cs`）

```
静止 120 帧   : min 0.000 max 0.000 meanAbs 0.0001 非零 0.0%   ← 球泡在水里，水面全平
拖动 30 帧    : min -1.000 max 1.000 meanAbs 0.0487 非零 9.3%
停下 90 帧    : min -0.005 max 0.011 meanAbs 0.0004 非零 0.0%
```

## 新坑

- **静止物体「水面一直抽搐」= 两条驱动 + 首帧假差值**：编辑模式下 `EditorTick`（渲染后 Process）和 `[ExecuteAlways].Update`（不渲染直接 Process）都会推进模拟；`Update` 读到被清空的 originRT 就把深度当成「球突然消失/出现」，正负冲量交替打进水里。另外 `Initialize` 后第一帧 `_InteractionOriginPrevTex` 是空的，静止的球算出 `delta = 1`，每次域重载 / 重导入 / 进出 Play 都放一记 0.5 的凹陷。修法：`Update` 在编辑模式 `return`、`EditorTick` 渲染失败就跳过 `Tick`、`Tick` 不再清空 originRT、Processor 用 `_primed` 让首帧只记录不注入。
- **compute 资产重新导入会让纹理绑定静默失效**：shader 实例内部重建，`SetTexture` 的旧绑定没了，Dispatch 不报错但 RT 全零（日志偶发 `Property (_InteractionWaterPTex) ... is not set`）。已改成 `Process` 每帧重绑，改完 compute 不再需要 `enabled=false/true` 或重开场景。
- **域重载后残留的编辑模式 tick 会在 `Release()` 之后 Dispatch**：`Process` 里加了 RT 空检查。
- 捕获输入的深度在球体范围内基本饱和成 1（背面穿透 ≥ 0.5 m），所以差值在边缘是**阶跃**，冲量很硬；要更柔和需要在 `InteractorObject` 的 `depthScale` / `saturate` 上做软化。

## 参数收敛（2026-11-05 晚）

`_Smoothing` / `_StaticPress` 删掉，Processor 只剩四个：`_WaveSpeed` / `_Damping` / `_ObjectForce` / `_Viscosity`。前三个是旧的一组、后三个是新增的一组，叠在一起是「两套参数」，谁也不知道该调哪个；`_StaticPress` 还是旧「钉住深度」行为的残骸。

验证（`tmp/VerifyNoTwitch.cs` / `tmp/DiagTrajectory.cs` / 实机 `tmp/LiveMove.cs`）：

```
静止 120 帧（单驱动）        : max 0.0000 meanAbs 0.00000 非零 0.00%
静止 120 帧（每帧多一次无渲染 Process，旧 bug 形状）: max 0.0000 非零 0.00%
拖动 30 帧                   : -1.0000 ~ 1.0000 meanAbs 0.0483 非零 9.63%
停下 120 帧                  : max 0.0000 非零 0.00%
编辑模式实机（5 s，静止）    : origin 恒 1.000，water 单调衰减 → 4e-5
编辑模式实机（拖 1 s 后松手）: 静止 0.000 → 拖动 0.955~1.000 → 松手后 0.905→0.664 持续衰减
```

---

# 「左右过冲、上下没反应」的根因链（2026-11-05 晚）

三个原因叠加，前两个让输入根本不是穿透深度，第三个让横向与纵向的量级天生差一个数量级。

## ① `ForwardTags` 里**靠后的 Tag 优先**（与文档相反）

URP 文档说 `DrawingSettings` 用「第一个匹配的 ShaderTag」，Unity 6.3 / URP 17 实测是**最后一个**命中。`CustomFeature.ColorCapturePass.ForwardTags` 原本把 `InteractionCapture` 排在最前，于是带两个 Pass 的互动物体永远被 `UniversalForward` 命中，捕获 RT 里存的是 `_BaseColor`（球是红色 → 恒 1.0）而不是穿透深度。

判别实验（都是决定性的）：

| 改动 | 结果 |
|------|------|
| `_BaseColor` 0.3 → 0.6 | `originRT` 读数 0.073 → 0.319（正好是 sRGB 线性化值，说明画的是颜色） |
| 把 `UniversalForward` 的 LightMode 改成不存在的名字 | `originRT` 变成捕获 Pass 的常量输出 |
| 在捕获 Pass 里直接 `return 0.25;` | `{"max":0.250}` —— 说明 Pass 本身是能画到的，只是没被选中 |
| `InteractionCapture` 移到表尾 | 剖面变成连续斜坡 |

症状：**二值圆盘**。上下移动时整块盘 0↔1 一起跳、被 `saturate` 吃掉 → 没反应；左右移动时只有边缘翻转 → 过冲。

## ② 正交相机不能用 `LinearEyeDepth`

`LinearEyeDepth(raw, _ZBufferParams)` 反解的是透视的 `1/(z·raw + w)`。这台正交相机 `near = -10`，实测**恒解到远裁剪面 10 m**（`sceneRaw = 0` 时是如此，`sceneRaw = 0.25` 时按该公式应得 13.3、实际几何距离是 5）。结果 `fragEye - sceneEye` 恒为负，整张图输出 0。

改用视锥线性关系（`_ProjectionParams.y/z` 就是相机自己的 near/far）：

```hlsl
#if UNITY_REVERSED_Z
    float sceneEye = _ProjectionParams.y + (1.0 - sceneRaw) * (_ProjectionParams.z - _ProjectionParams.y);
#else
    float sceneEye = _ProjectionParams.y + sceneRaw * (_ProjectionParams.z - _ProjectionParams.y);
#endif
```

验证（`tmp/DiagAxis.cs`，球半径 0.75、参照面 y = 0、相机 y = 5，看中心像素）：

```
球心 y = +0.3 → 0.446     球心 y = +0.1 → 0.646
球心 y = -0.1 → 0.846     球心 y = -0.3 → 1.000（≥1 m 已饱和）
```

`0.446/0.646/0.846` 与几何值 `0.45/0.65/0.85` 逐一对上，且每 0.2 m 恰好变化 0.2 —— 线性，说明换算正确。

## ③ 水线是硬边，单帧 `delta` 与速度无关

深度盘内部是连续的（水线 0.5 m → 球底 1.25 m），但**水线处从「水面上的 0」一步跳到「水面下 0.5 m」**。球横滑 0.03 m，那一圈像素整块翻转，`delta` 一步到 **0.7**（实测 `dmax 0.697`，每帧约 580 个像素参与），与拖动速度无关；纵向移动只是把内部深度整体抬降（每帧 0.03 量级）。

修法：Compute 里给单帧差值封顶，取与纵向平滑变化同量级的 0.03：

```hlsl
const float kMaxDelta = 0.03;
delta = clamp(delta, -kMaxDelta, kMaxDelta);
```

实测（30 Hz，`_ObjectForce` 0.5 / `_Viscosity` 0.08，峰值 = `_InteractionWaterTex` 绝对值最大；左列是 `kMaxDelta = 0.25` 时，阶跃几乎不被削）：

```
横向 1.5 m / 15 帧 : 1.00（钳位）→ 0.36
纵向 0.7 m / 15 帧 : 0.62        → 0.50
横向 1.0 m / 30 帧 : 1.00（钳位）→ 0.88
纵向 0.7 m / 30 帧 : 0.78        → 0.83
静止 120 帧        : 0           → 0（不受影响）
```

## 新坑（这一轮）

- **`DepthReference` 在保存的场景里是 `m_IsActive: 0`**（该物体已于 2026-11-05 删除，这条只作为「未激活对象 `GameObject.Find` 找不到」的例子保留）：`_CustomDepthTexture` 全空 → `sceneRaw = 0` → 参照面被算到远裁剪面，穿透恒 0。`GameObject.Find` 还会因为对象未激活直接返回 null，`tmp/DiagScene.cs` 遍历根物体才看出 `active: 0`。
- **shader 编译失败不会有任何可见反馈，继续用旧二进制**：`float4 positionVS = TransformWorldToView(...)` 报 `cannot implicitly convert from 'const float3' to 'float4'`，编辑器照常渲染旧版本，两轮测量全白做。**每次改完 shader 先 `unityctl logs -l error`**。
- **判据：同一读数反复出现同一个「好看」的值（如恒 0.250）就是用了旧二进制**，不是物理。
- **`Shader.GetGlobalTexture("_InteractionWaterTex")` 重初始化后可能仍指向旧 RT**：扫参数时四个不同 `_ObjectForce` 得到四个几乎一样的「衰减后」读数（0.862/0.873/0.866/0.860），是因为读的是上一轮的旧纹理。**改成用反射读 Processor 自己的 `_waterTex` 字段**，数值立刻随参数单调变化。
- **探针瞬移会产生真实冲量**：诊断脚本里「先渲染几次但不 `Tick`」再回到原位，下一次 `Process` 会把这一大段位移当成一次运动打进水场，污染后续所有测量。要干净起点就 `enabled=false/true` 重初始化。


## 参照面改用水面自身（2026-11-05 深夜）

`DepthReference` 这个 10×10 代理平面已经删除，参照面换成水面本身 —— 也就是「水面大，直接用位移前的网格做深度差」（用户提的）。链路：

| 环节 | 改动 |
|------|------|
| `Render/Water` | 新增 `LightMode=DepthOnly` Pass：`Cull Back` / `ZWrite On` / `ColorMask 0`，只写 `TransformObjectToHClip(positionOS)`，**不细分、不做 `ComputeInteractionWave`** |
| `IA_Renderer` | 新增 `TransparentDepthMergeFeature` 子资产：`layerMask = 1<<6`、`shaderTags = ["DepthOnly"]`、`passEvent = AfterRenderingOpaques(300)`；`m_CopyDepthMode` 由 1（AfterTransparents）改为 **2（ForcePrepass）** |
| `CustomFeature` | `copyDepth = false`；颜色捕获 `renderPassEvent` 改为 `AfterRenderingTransparents`（要等透明深度合并完）；`copyDepth` 关掉时**不再**把 `_CameraDepthTexture` 挂成深度附件（同一资源不能既当附件又当全局采样） |
| `InteractorObject` | 捕获 Pass 采样 `_CameraDepthTexture`（全局），按 `_ProjectionParams.y/z` 换算视空间距离 |

关键结论：

- **DepthOnly 必须排除交互位移**。参照面若跟着位移一起下沉 → 位移越深参照越深 → 穿透越大 → 位移更深，水面自激。位移前的平面网格正好就是「静止水面」。
- **`CopyDepthMode` 1 ≠ 2**：`AfterTransparents` 产出的 `_CameraDepthTexture` 是 `R32_SFloat` **颜色拷贝**，`SetRenderAttachmentDepth` 会直接抛 `has a color format R32_SFloat`（TransparentDepthMerge 在交互相机上崩）。`ForcePrepass` 才是真正的 D32 深度附件。`PC_Renderer` 本来就是 2，只有 `IA_Renderer` 是 1。
- **颜色捕获要排在 `AfterRenderingTransparents`**：合并发生在 `AfterRenderingOpaques`，排在前面就读到没合并的深度。
- 验证（`tmp/VerifyWaterRef.cs`，探针 y=-0.2、球底 -0.95）：水面 y=0 → 中心 **0.945**；水面 y=-1（球在水面上方）→ **0.000**；水面 y=+1 → **1.000**。三个值只有在「参照 = 水面」时才成立，之前 DeepReference 存在时挪水面不会变。
- 回归：静止 120 帧 `max 0.0000`；X 1.5 m/15 帧 0.36、Y 0.7 m/15 帧 0.50（与参照面是同一张 y=0 平面时完全一致）。

## 模块改名 InteractionCapture → Custom（2026-11-05）

- 目录 `Assets/Mine/Special/Renderer/InteractionCapture/` → `Custom/`；`InteractionCaptureFeature` → `CustomFeature`（`CustomFeature.cs`）；`Validation~/CaptureSmokeTest.cs` → `CustomSmokeTest.cs`；`Validation~/MigrateInteractionRenderer.cs` → `MigrateCustomRenderer.cs`；`Shaders/Custom.shader` 的 Shader 名 `CustomRenderer/Custom` → `Custom/Custom`。
- **C# 类名必须和文件名一起改**，否则 Unity 报「脚本里没有同名类」。`.cs.meta` 靠 `git mv` 保住 GUID，`IA_Renderer.asset` 里子资产的 `m_Script` 仍指同一个 MonoScript，只需同步 `m_EditorClassIdentifier` 文本。
- `Validation~/` 目录**没有 `.meta`**（`~` 后缀 Unity 忽略），`git mv` 那个目录里的文件时不要再配 `.meta` 移动，否则 `fatal: bad source` 中断整条链。
- 改名后 `IA_Renderer.asset` 仍报 `features[0].type = Mine.Rendering.CustomFeature`、`missingScript = false`，子资产名一并改成 `Custom Capture`。

## 水体表现收敛成一个美术参数（2026-11-06）

`_waveSpeed` / `_damping` / `_viscosity` / `_objectForce` 四个 Inspector 参数全部删除，只留 `_clarity`（0 = 浑浊，1 = 清澈）。0.5 复现原来手调的一组：波速 0.15 / 每秒衰减 0.14 / 粘稠度 0.08 / 冲量 0.5。

| 派生量 | 浑浊 0 | 清澈 1 | 插值 |
|---|---|---|---|
| 波速 `c` | 0.08 | 0.22 | 线性（中点 0.15） |
| 每秒衰减率 | 0.80 | 0.025 | 几何（中点 √0.02 ≈ 0.141） |
| 粘稠度 γ | 0.40 | 0.016 | 几何（中点 √0.0064 = 0.08） |
| 冲量增益 | 0.25 | 0.75 | 线性（中点 0.5） |

- **衰减改成按秒**：`retention = pow(1 − decay, deltaTime)` 在 CPU 算好再传给 compute。旧实现直接把「每帧保留率」乘进场里，同一组值在 144 Hz 下比 30 Hz 死得快几倍，编辑器 30 Hz 预览与真机手感对不上。`Process(deltaTime, …)` 的 `deltaTime` 以前收而不用，现在用上了。
- **为什么衰减/粘稠度用几何插值**：两者都是「小值才有意义」的量，线性插值会让滑条中段衰减过猛，观感变化集中在浑浊端。
- 端点写成 `WaterInteractionProcessor` 里的 `const`，想试极端观感直接改 const，不再暴露给美术。
- 迁移：两个场景里旧的 `_damping: 0.995` / `0.95` 等字段失去对应字段后被 Unity 忽略，`_clarity` 未序列化 → 取默认 0.5。`RenderScene.unity` 原来是 `_damping 0.95`（≈ 每秒衰减 0.79），换成 clarity 0.5 后明显更「清水」，需要时把该场景的 clarity 调到 0.1~0.2。
- 注：`BuildWaterInteractionDemo.cs` 同步改成只 `SetSerializedFloat(processor, "_clarity", 0.5f)`。

## 传播速度的天花板在步进频率，不在波速（2026-11-06）

现象：`_clarity` 拉到 1 仍觉得「波纹跑得慢」。原因不是参数没给够，是显式五点拉普拉斯的物理限制：

```
传播速度 = √c × (面积 / 分辨率) × 步进频率
         = √0.22 × (10/256 m/px) × 30 Hz ≈ 0.55 m/s   （跨 10 m 要 18 s）
```

- `c` 受 CFL 限制 `c ≤ 0.25`，清澈端 0.22 已在 88%，再抬就直接发散，没有余量。
- 唯一能乘的杠杆是**步进频率**：清澈端每帧拆子步跑到 120 步/s。
- 实测（`tmp/PropagationSpeed.cs`，拖 0.25 m 圆，每 1/3 s 采样阈值 0.01 的最大半径）：清澈端 `radiiPx [19.4,23.9,28.0,32.2] → 13 px/s`，加子步后 `[47.6,65.9,83.9,102.0] → 54 px/s（2.12 m/s，4.2×）`；中段 0 → 17 px/s。跨完 10 m 从 18 s 到 ~4.7 s。

实现（`WaterInteractionProcessor`）：

- `stepsPerSecond = lerp(30, 120, clarity)`，`substeps = clamp(round(stepsPerSecond × dt), 1, 8)`，`subDt = dt / substeps`。等分帧长 → 每秒步数等于标称值，**传播速度与帧率无关**（编辑 30 Hz 拆 4 步、Play 60 fps 拆 2 步，观感一致）。
- **三个「每步」系数必须随子步折算**，否则子步一多就被重复施加：`_Damping = pow(1−decay, subDt)`；`_Viscosity = 1 − (1−γ)^(30·subDt)`（以 30 Hz 标定）；边界吸收带 `_Sponge = 12 × subDt`（11-06 起改用真正的能量耗散 `exp(-_Sponge·(1-window))`，不再是 `pow(edge, 1/substeps)`）。漏掉粘稠度折算时实测只有 3.5× 而不是 4.2×，且中段的波「一动就没了」。
- 输入只在第一子步消费：`_DeltaPixels` 用完清零；冲量不会重复注入，因为 kernel 自己把 `_InteractionOriginPrevTex` 更新成当帧深度，后续子步 `delta` 恒为 0。
- 稳定性：最清澈（4 子步、最弱阻尼）静止 120 帧仍 `max 0.0000`，没有自激。

配套：`UniversalInteractionManager` 的 `EditorTick` 从「下次允许时间」限流改成**固定步长累加器**（累加 `timeSinceStartup` 差值，够 1/30 s 才出步；单次最多补 0.25 s；渲染失败清空累加器）。`EditorApplication.update` 的间隔在几十到几百 Hz 间跳，限流器会让仿真实际推进速度 = min(update 频率, 30 Hz) 且步长抖动。

## 物体互动强度与水体参数解耦（2026-11-06）

原来 `_clarity` 把冲量增益（0.25→0.75）也捆着，等于「水的性格」替所有物体统一调音量。现在 **`_ObjectForce` 恒为 1**，物体强弱归物体自己的材质 `InteractorObject.shader` 的 `_Intensity`——它缩放捕获 Pass 输出的穿透深度（`saturate(穿透米数 × _Intensity)`），天然是每个物体一份。

链路：`InteractorObject` Pass 0（LightMode `InteractionCapture`，只被 `CustomFeature` 的正交相机命中）→ `_originRT` → `CSWave` 读 `depth` 做帧间差 → `h += −clamp(delta, ±0.03) × 1 × contact`。主相机看到的是同一个材质的 Pass 1（`UniversalForward`），两边互不干扰。

实测（`tmp/IntensitySweep.cs`，探针 scale 0.5，clarity 0.5，15 帧拖动）：

| `_Intensity` | 0.25 | 0.30 | 0.35 | 0.40 | 0.50 | 1.00 | 2.00 |
|---|---|---|---|---|---|---|---|
| 下压峰值 | 0.359 | 0.516 | 0.701 | 0.895 | 0.998（触顶） | 0.998 | 0.874 |
| 横划峰值 | 0.248 | 0.309 | 0.367 | 0.392 | 0.402 | 0.415 | 0.288 |

- **有效区间只有 0.05–0.45**：捕获值 `saturate` 到 0..1、水面输出 `clamp ±1`，1 以上就是削顶，2 过冲后反而更弱。
- 横划比下压更早饱和：水线是硬边，边沿像素 `delta` 本来就等于整个穿透深度，`kMaxDelta = 0.03` 一削，`_Intensity` 只剩 `contact = saturate(depth × 4)` 这一个（0.25 m 满）还在变。
- 材质默认值从 1 改成 **0.4**（未饱和的最大值）；改完 `moving` 峰值从触顶的 ±1.0 回到 −0.55 / +0.26，静止态（含 clarity 1）仍全零。



