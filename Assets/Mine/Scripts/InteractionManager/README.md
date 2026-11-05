# InteractionManager — 正交交互系统

> 基于正交相机 + Compute Shader 的实时交互模拟系统。
> 支持水面波纹、雪地变形等效果，通过自定义渲染管线获取物体穿透深度作为输入。

---

## 架构

```
UniversalInteractionManager           ← 总控
  ├── 正交相机 (InteractionCapture)   ← 渲染交互物体深度到 originRT
  ├── _InteractionOriginTex           ← 共享输入 RT (Manager 管理)
  └── IUniversalInteractionProcessor  ← 处理器接口
        └── WaterInteractionProcessor  ← 水面波纹 (Verlet 波方程)
```

**关键设计**: Manager 只管理共享输入，Processor 自管输出 RT。

---

## 文件夹

| 路径 | 内容 |
|------|------|
| `InteractionManager.cs` | 总控：正交矩阵、相机跟随、originRT、全局 Shader 属性 |
| `IInteractionProcessor.cs` | 接口：Initialize / Process / BindGlobalTextures / Release |
| `Water/` | 水面交互：Compute Shader + Processor |
| `Shaders/` | 渲染 Shader：Debug 可视化 + InteractorObject |
| `WaterInteractionDemo.unity` | 整套配置的验证场景（相机 / 总控 / 处理器 / 探针 / 水面） |
| `Materials/` | Demo 用的两个材质（Debug 水面 / 交互物体） |
| `Validation~/` | `BuildWaterInteractionDemo.cs`：重建 Demo 场景的编辑器脚本（`~` 目录不进资产管线） |

---

## 设置步骤

1. `IA_Renderer`（`UniversalRendererData` + `InteractionCaptureFeature`，URP 自身层掩码全空）已在 `PC_RPAsset` 的 Renderer List 里
2. 场景中创建正交相机子物体，挂载 `UniversalAdditionalCameraData`，Renderer Index 指向该 Renderer；Clear Flags 用 SolidColor（黑），不要用 Skybox
3. 在 GameObject 上挂载 `UniversalInteractionManager`，拖入相机
4. 同一 GameObject 上挂载 `WaterInteractionProcessor`，拖入 WaterInteraction.compute
5. 场景中放置 debug 平面（材质用 `InteractionManager/InteractionDebug`）
6. 交互物体使用 `InteractionManager/InteractorObject` Shader，放在 layer 7；**主渲染器的 Opaque / Transparent Layer Mask 要包含 layer 7**（Demo 里 `PC_Renderer.asset` 已加）

---

## 水体互动 Demo 场景

`WaterInteractionDemo.unity` 把上述 6 步全部落地，用来验证「正交相机 → Feature → Manager → Compute → 全局纹理」整条链路：

| 物体 | 层 | 作用 |
|------|----|------|
| `WaterSurface` | 6 `Interaction` | 10×10 平面，材质 `InteractionManager/InteractionDebug`，只进深度捕获，主相机看到的是双色热力图 |
| `InteractorProbe` | 7 `Interactor` | 半径 0.75 的球，材质 `InteractionManager/InteractorObject`。**一个物体两个 Pass**，见下 |
| `InteractionManager` | 0 | `UniversalInteractionManager` + `WaterInteractionProcessor`，正交相机是它的子物体 |
| `InteractionCamera` | 0 | 正交，Renderer Index = 1（`IA_Renderer`），SolidColor 黑，culling mask = 6&#124;7 |
| `Main Camera` | 0 | Renderer Index = 0，斜俯视，culling mask = 全部（layer 7 也画） |

### 一个物体两个 Pass

`InteractorObject.shader` 用 LightMode Tag 把同一个球分流给两个相机，不需要再挂一个可见外壳：

| Pass | LightMode | 谁画 | 作用 |
|------|-----------|------|------|
| `InteractionCapture` | `InteractionCapture` | 交互相机（`InteractionCaptureFeature` 的颜色捕获列表把它排在最前） | `Cull Front` + `ZTest Always`，采样 `_CustomDepthTexture` 输出穿透深度 |
| `Forward` | `UniversalForward` | 主相机（标准 URP 前向） | 输出 `_BaseColor`，就是物体正常的样子 |

标准 URP 的前向 Pass 只认 `SRPDefaultUnlit / UniversalForward / UniversalForwardOnly`，所以主相机永远不会画到 `InteractionCapture`；反过来捕获列表找不到 `InteractionCapture` 时才会回退到这些标准 Tag。

**layer 7 必须进主渲染器**：`PC_Renderer.asset` 的不透明 / 透明层掩码原来只有 `119`（0,1,2,4,5,6），layer 7 被排除，主相机连 `UniversalForward` 都画不出来。现已改为 `247`（加上 7）。layer 7 现在的含义是「捕获相机要额外捕获它」，不再是「主相机看不见」。

- `InteractionDebug` 的 `_BaseColor` 调到 **8**：凹陷能到 -1，但向外扩散的涟漪只有 0.05 量级，增益低了 Scene 视图里几乎全黑。
- `InteractionCamera` 的 Target Texture **由 Manager 在运行时创建并赋值**，场景里存的是 `{fileID: 0}`：重开场景不会多出一份孤儿 RT。
- 场景默认可以被**叠加加载**（不挂 AudioListener），但要看得清请单独打开。
- 重建场景：`unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/BuildWaterInteractionDemo.cs"`（幂等，会覆盖场景与材质）。

### 编辑模式预览（不进 Play 就能看）

`UniversalInteractionManager` 在 `#if UNITY_EDITOR` 下挂了 `EditorApplication.update`，按 30 Hz 做三件事：**提交一次正交相机渲染 → 跑一次 `Process` → `SceneView.RepaintAll()`**。不这么做的话，`[ExecuteAlways]` 的 `Update` 在编辑器里只在场景变化时才被调用，波方程不会自己推进；而且 URP 不会替带 Target Texture 的相机在编辑模式下自动渲染，`originRT` 会一直是空的。

- Inspector 上的 **编辑模式预览**（`_editorPreview`，默认开）可以整体关掉——它会持续占用一点 GPU，并且强制 Scene 视图按 30 Hz 重绘。
- 观察方式：选中 `InteractorProbe`，用 Move 工具拖它，蓝色凹陷会跟着走并向外甩出一圈波纹。
- 主相机和 Scene 视图里它就是那个橙色球；捕获相机单独看它时走的是另一个 Pass，两边互不干扰。
- **水面板静止时是纯黑的**（调试 Shader 输出的是 `高度 × _BaseColor`，零高度就是黑），只有探针脚下和波纹处才有颜色——所以别把 Scene 视图拉太远。
- 编辑模式下的模拟和 Play 模式用的是同一套参数，调 `Water Speed / Damping / Object Force` 可以实时看到区别。


### 验证记录（2026-11-04，Unity 6000.3.14f1 / URP 17.3.0 / Metal）

Play 模式（与 GrassManagerDemo 叠加加载，测量时临时禁用另一场景的根物体），暂停后逐帧 `play step`：

```
capture     : max 0.657  非零像素 1108  centroid(185.8, 101.1)
probe       : world(2.278, 0.092, -1.030) → 像素(186.3, 101.6)
water       : min -0.657  centroid(185.8, 101.0)
逐帧校验和  : -524.8 → -522.4 → -518.3 → -512.1 → -504.0 → -494.1 → -482.7 → -469.6
```

- 捕获质心与探针像素坐标一致（差 < 1px）：正交相机 → `InteractionCaptureFeature` → `_InteractionOriginTex` 的空间映射正确。
- 波面凹陷质心与探针像素一致：`_InteractionOriginTex → Compute → _InteractionWaterTex` 的映射正确。
- 校验和逐帧单调变化：`UniversalInteractionManager.Update` 每帧在跑 `Process`。
- 主相机成图上能看到探针拖出的红/蓝同心波纹环，探针正下方是蓝色凹陷。
- 退出 Play 模式无 error / warning。

### 本次验证修掉的问题

`WaterInteractionProcessor.Release()` 原来用 `ComputeShader.SetTexture(kernel, id, null)` 解绑，而该 API 拒绝 null，会抛 `ArgumentNullException`，导致后面的 `ReleaseRT` 一条都执行不到 —— 每次退出 Play 模式都会报错并泄漏两张 RT。现改为只释放 RT（下次 `Initialize` 重新绑定，中间没有 Dispatch）。

### 未覆盖

- 移动模拟域（`_followTarget` 非空时 `_DeltaPixels` 重投影）没有在 Demo 里跑，场景中 `_followTarget` 为空。
- 只在 Editor 内验证，未跑构建包。

---

## Inspector 参数

### UniversalInteractionManager

| 参数 | 说明 |
|------|------|
| Ortho Camera | 正交相机引用 |
| Follow Target | 跟随物体（空=静态） |
| Area Size | 交互区域边长 (m) |
| Ortho Height | 相机 Y 高度 |
| Ortho Near/Far | 深度裁剪范围 |
| Resolution | RT 分辨率 (px) |

### WaterInteractionProcessor

| 参数 | 默认值 | 说明 |
|------|--------|------|
| Wave Speed | 0.15 | 像素空间波速，已归一化到 areaSize=10 |
| Damping | 0.995 | 每帧能量衰减 |
| Object Force | 1.0 | 物体下压力度 |

---

## 纹理命名

| 纹理 | 管理者 | 用途 |
|------|--------|------|
| `_InteractionOriginTex` | Manager | 相机渲染的交互输入 |
| `_InteractionWaterTex` | WaterProcessor | 波方程输出（debug shader 采样） |
| `_InteractionWaterPTex` | WaterProcessor | Verlet h_prev（内部） |
| `_CustomDepthTexture` | InteractionCaptureFeature | 正交深度纹理 |

---

## 扩展

新增交互类型只需实现 `IUniversalInteractionProcessor`：

```csharp
public class SnowInteractionProcessor : MonoBehaviour, IUniversalInteractionProcessor
{
    public void Initialize(int resolution, RenderTexture sourceRT) { }
    public void Process(float deltaTime, Vector2 worldDelta) { }
    public void BindGlobalTextures() { }
    public void Release() { }
}
```

然后挂载到 Manager 同一 GameObject 即可，Manager 通过 `GetComponent<IUniversalInteractionProcessor>()` 自动发现。
