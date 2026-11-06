# InteractionManager — 正交交互系统

> 基于正交相机 + Compute Shader 的实时交互模拟系统。
> 一套共享捕获输入，按语义通道挂多个**交互子集**（水面 / 草地 / 雪地 / 沙地 …），
> 通过自定义渲染管线获取物体穿透深度作为输入。

---

## 架构

```
UniversalInteractionManager              ← 总控：只管理共享输入与调度
  ├── 正交相机 (InteractionCapture)      ← 渲染交互物体深度到 originRT
  ├── _InteractionOriginTex              ← 共享输入 RT
  ├── 正交矩阵 / 交互域全局量
  └── InteractionProcessor 注册表（静态，与 FieldManager / InstanceManager 同构）
        ├── WaterInteractionProcessor    ← 水面：帧间差值 → Verlet 波方程
        ├── GrassInteractionProcessor    ← 草地：压弯量 + 回弹
        ├── SnowInteractionProcessor     ← 雪地：向下压实 + 蠕变摊圆 + 沉降消失
        └── SandInteractionProcessor     ← 沙地：带符号高度 + 坑沿堆脊 + 休止角塌落
```

**关键设计**

- Manager 只管理**共享输入**（`_InteractionOriginTex`、正交矩阵、交互域参数），并按 `InteractionOrder` 顺序驱动所有已注册处理器；它不关心任何具体表面的物理。
- 每个 Processor 自管**输出 RT** 与全局纹理，互不引用。它们共用同一台交互相机、同一个 `_followTarget` 和同一块交互域。
- 注册表是静态的：`OnEnable` 自动 `Register`，`OnDisable` 自动 `Unregister`；语义键（`InteractionKey`）重复会被拒绝，并保持注册顺序稳定。
- 同一时刻只允许一个**活动** Manager；叠加加载两个带 Manager 的场景会报错并禁用后来者。

> `InteractionProcessor` 带 `[ExecuteAlways]`。没有它，编辑器加载场景时普通 MonoBehaviour 不会触发 `OnEnable`，处理器不会注册，Manager 的编辑模式预览永远是空的。

---

## 文件夹

| 路径 | 内容 |
|------|------|
| `InteractionManager.cs` | 总控：正交矩阵、相机跟随、originRT、全局 Shader 属性、处理器注册表 |
| `IInteractionProcessor.cs` | `IUniversalInteractionProcessor` 接口（Initialize / Process / BindGlobalTextures / ResetGlobals / Release） |
| `InteractionProcessor.cs` | 处理器抽象基类：注册、生命周期、RT 创建 / 释放、世界位移 → 像素偏移 |
| `Water/` | 水面交互：Compute Shader + Processor |
| `Grass/` | 草地交互：压弯量 compute + Processor + 渲染侧 `GrassInteraction.hlsl` + `GrassGround.shader` |
| `Snow/` | 雪地交互：踩印 compute + Processor + `SnowInteraction.hlsl` + `SnowGround.shader` |
| `Sand/` | 沙地交互：带符号高度 compute + Processor + `SandInteraction.hlsl` + `SandGround.shader` |
| `Shaders/` | `InteractionField.hlsl`（世界 → UV / 采样 / 梯度）、`InteractionGroundLighting.hlsl`、`InteractorObject.shader` |
| `Demo/` | `FlatGrassTerrain`（平坦草地来源）、`InteractionProbeDriver`（编辑 / Play 都能跑的往返探针） |
| `WaterInteractionDemo.unity` | 水面 Demo（相机 / 总控 / 处理器 / 探针 / 水面） |
| `InteractionSurfacesDemo.unity` | 三表面 Demo：草地 / 雪地 / 沙地共用一套捕获与一个探针 |
| `Materials/` | 水面 Demo 用的两个材质（Debug 水面 / 交互物体） |
| `Validation~/` | `BuildWaterInteractionDemo.cs`、`BuildInteractionSurfacesDemo.cs`、`CheckJitter.cs`、`CheckDomainFade.cs`、`CheckTraceSeam.cs`、`CheckSurfaceDynamics.cs`、`CheckWaterFade.cs`（`~` 目录不进资产管线） |

---

## 设置步骤

1. `IA_Renderer`（`UniversalRendererData` + `CustomFeature` + `TransparentDepthMergeFeature`，URP 自身层掩码全空，`CopyDepthMode = ForcePrepass`）已在 `PC_RPAsset` 的 Renderer List 里
2. 场景中创建正交相机子物体，挂载 `UniversalAdditionalCameraData`，Renderer Index 指向该 Renderer；Clear Flags 用 SolidColor（黑），不要用 Skybox
3. 在 GameObject 上挂载 `UniversalInteractionManager`，拖入相机与 `_followTarget`
4. **同一 GameObject 上挂载需要的每个子集**（`WaterInteractionProcessor` / `GrassInteractionProcessor` / …），拖入各自的 Compute Shader。它们会在 `OnEnable` 自动注册，不需要在 Manager 上逐个拖引用
5. 场景中放一个**透明表面**（水面 / 草地 / 雪地 / 沙地）到 layer 6，并在它的 Shader 里提供 `LightMode = DepthOnly` 且**不做位移**的 Pass —— 它就是穿透深度的参照面，由 `TransparentDepthMergeFeature` 合并进 `_CameraDepthTexture`
6. 交互物体使用 `InteractionManager/InteractorObject` Shader，放在 layer 7；**主渲染器的 Opaque / Transparent Layer Mask 要包含 layer 7**

---

## 多子集协议

```csharp
public interface IUniversalInteractionProcessor
{
    string InteractionKey { get; }   // 语义通道名，重复会被拒绝
    int InteractionOrder { get; }    // 越小越先处理；相同值保持注册顺序
    bool IsReady { get; }
    void Initialize(int resolution, RenderTexture sourceRT);
    void Process(float deltaTime, Vector2 worldDelta);
    void BindGlobalTextures();
    void ResetGlobals();             // 没有子集时把全局量复位，避免未绑定的纹理默认成白
    void Release();
}
```

实际写新子集时继承 `InteractionProcessor`，只实现 6 个钩子：

| 钩子 | 职责 |
|------|------|
| `CreateResources()` | 查 kernel、建输出 RT；返回 false 表示本帧不参与 |
| `BindTextures()` | 每帧把 compute 的纹理槽重绑到当前 RT（shader 重新导入后旧绑定会静默失效） |
| `ProcessStep(dt, worldDelta)` | 推进一帧模拟 |
| `BindGlobals()` | 发布全局纹理与有效性标记 |
| `ResetGlobals()` | 复位全局量 |
| `ReleaseResources()` | 释放自管 RT，必须可重复调用 |

### 移动交互域：帧间重投影

Manager 每帧把交互域中心的位移换算成像素偏移，所有子集都用同一套“上一帧在 `id + _DeltaPixels`”的约定：

```
_DeltaPixels = round(worldDelta × resolution / areaSize)
prev = InBounds(id + _DeltaPixels) ? _PrevTex[id + _DeltaPixels] : 0
```

**交互域中心先吸附到纹素网格**（`SnappedCenter`，`x/z` 各自 `round(c / texel) * texel`，`texel = areaSize / resolution`），相机、`_InteractionAreaPos`、位移全部以吸附后的中心为准。因此 `worldDelta` 恒为 `texel` 的整数倍，`_DeltaPixels` 是**精确**整数，没有亚纹素残差。

不吸附会抖：键位网格固定在世界坐标上，相机中心带着小数纹素滑动时，`round` 每帧丢掉的残差会让压痕相对地面慢慢漂；慢速摆动（位移 < 半个纹素）时 `round` 在 0 / ±1 之间来回跳，压痕整体一帧一格地闪——也就是“球动的时候痕迹抖”。吸附之后单帧位移只可能是 `0` 或 `n` 个纹素，重投影是纯整数搬移，不存在抖动。

同样的配套约束：**捕获用的位姿和算 delta 用的位姿必须是同一次采样**。`Tick` 之前先 `SyncMatrices()` 再 `RenderOrigin()`，编辑模式和 Play 模式都走这条路径（`Update` 里自己提交渲染请求，交互相机的组件是关着的，不会在帧末再渲一次）。

草 / 雪 / 沙都使用 ping-pong RT（`_front` / `_back`），避免同帧读写与邻域采样竞争；Swap 之后立刻 `BindGlobals()`，保证这一帧渲染读到的是新的 front。

### 有效性标记

`_InteractionGrassValid` / `_InteractionSnowValid` / `_InteractionSandValid` 在绑定时为 1、复位时为 0。渲染侧所有采样都乘以这个标记：没有草地子集时草不会被“整片压弯”，因为未绑定的全局纹理默认是白色。

### 域边界淡出（统一一条，别各调各的）

边界淡出是**数据级**约定，写在 `Shaders/InteractionDomainEdge.hlsl` 里，宽度只有一个常量，两个动作：

```hlsl
#define INTERACTION_DOMAIN_FADE 0.08   // 占 RT 边长的比例；12 m / 256 上约 0.96 m、20 纹素

float InteractionDomainWindow(uv)                 // 域心 1，边界 0（两轴 smoothstep 相乘）
float InteractionDomainFade  (value, pixel, res)  // ① 窗口化**输入**：value * window
float InteractionDomainAbsorb(pixel, res, absorbPerBand)  // ② 按位移吸收已存的痕迹
```

**① 乘在输入上。** 压在域边上的探针拿到的接触深度先乘窗口，所以压出来的痕迹本身就带淡出形状（20 纹素渐变、边界纹素贴着 0）。

**② 按位移吸收。** `absorbPerBand` = 这一步域移动的距离 ÷ 淡出带宽度 × 12（`InteractionProcessor.EdgeAbsorbPerStep`）。域不动时它恒为 1，痕迹一点不变；域把旧痕迹扫向边界时按**穿过带子的距离**累积，穿过整条约 `e^-6 ≈ 0.25%` —— 数据是真的被吃掉了。所以域再扫回来时不会露出「满强度挨着 0」的断口。

为什么按位移而不是按时间：按时间的话，球停下来不动的那些秒里痕迹还在继续掉；更糟的是它会把带内吃空、只剩内沿 `w = 1` 那道坎（实测雪地静置 3 s 带内相邻纹素台阶 11% → 48%），软边自己变硬边。按位移时「域不动 ⇒ 不变」，这个退化根本不会发生。

**为什么不能「读时除、写时乘」的往返**：那样存进去的原始值一直是满的，淡出只是显示遮罩。域把痕迹扫出 RT 时数据没被消耗，走回来就恢复成满强度，紧挨着真正掉出 RT 的那一格（0）—— 域中间凭空多出一条 **97.5%** 的硬断口（`Validation~/CheckTraceSeam.cs` 复现，用户看到的就是这个）。吸收必须是真的消耗。

水面额外按时间上一条海绵带：波是动的，必须真的耗能，否则会从 0 墙反射回来。每子步乘 `exp(-_Sponge·(1-window))`，`_Sponge = 12 × subDt`；再叠同样的位移吸收。注入的冲量也乘窗口。

**横向搬运在淡出带里关掉**（沙的休止角塌落、雪的蠕变都要乘窗口）：扩散就是把坡摊平，贴着边界摊平等于把 0 抬起来，边界纹素会被邻域填满（沙实测最外圈残留 1.3%，关掉后 0.23%；雪的蠕变漏乘窗口时 1.11% → 0.11%）。乘的是**速率**不是存值，所以静置时不会有累积衰减。

`InteractionField.hlsl` 的 `InteractionDomainMask` 因此只剩一个**硬的内外判断**（`step(abs(uv-0.5), 0.5)`）：淡出已经在数据里做过了，再叠一层 smoothstep 会和数据里的窗口相乘，把淡出带压窄一半。

---

## 三个表面子集

| 子集 | 键 | 输入语义 | 输出 | 每帧更新 | 美术参数 |
|------|----|----------|------|----------|----------|
| 草地 | `grass` | 接触深度 × 5 → 压弯量 0..1 | `_InteractionGrassTex` | `max(prev − recovery·dt, 0)` 与当前接触取大 | `_springBack` 0..1：0 = 很慢回弹，1 = 抬脚即弹回 |
| 雪地 | `snow` | 接触深度 × 5 → 压实量 0..1 | `_InteractionSnowTex` | 压实 `max` → 蠕变扩散摊圆坑壁 → 乘性沉降 | `_creep`（坑壁摊圆）、`_settle`（痕迹变浅消失） |
| 沙地 | `sand` | 接触深度取负 → 高度 −1..0.25（负 = 坑，正 = 脊） | `_InteractionSandTex` | 挖坑 → 挤出堆脊 → 休止角塌落 → 乘性回平 | `_rim`（坑沿堆脊）、`_repose`（休止角 20°–40°）、`_settle` |

**两种材料的力学不同，这是这一层的核心**：

- **沙 = 小形变、不可压实的颗粒材料**：体积不会消失，受压只能被挤到旁边，于是坑的外沿堆起一圈脊
  （`h = max(h, k·relu(boxAvg₅ₓ₅(press) − press))`，压差场就是"被挤出去的那部分"）。
  抬离后高过休止角 `tanθ` 的坡按 `h += Σ ±(相邻高差 − L)` 塌落（纯 gather，无写竞争；
  `L = tanθ · texelWorld / surfaceScale`，由 Processor 预先换算），最后整体 `h *= exp(−k·dt)` 回平。
- **雪 = 大形变、可压实的材料**：晶格压塌、体积被吃掉，只有"往下压实"，**没有**侧向堆脊。
  痕迹靠蠕变（向邻域扩散，把坑壁从一道直壁摊成缓坡）和乘性沉降变浅消失。

乘性衰减（`h *= exp(−k·dt)`）而不是线性回填（`h − k·dt`）：线性会把坑底磨平、形状失真；
乘性保持形状、整体等比变浅，这才是"痕迹随时间消失"该有的样子。

**两个 `min/max` 守卫，少一个就抹掉痕迹**（都实测踩过）：

- `if (press > 0) h = min(h, −press)` —— 抬离后 `press == 0`，无条件的 `min` 会把坑沿所有**正值**压成 0，脊一离地就没了。
- `if (rim > 0 && h >= −press) h = max(h, rim)` —— 无条件的 `max` 会在 `rim == 0` 处把**负的坑** max 成 0（坑 −0.79 → 0）；
  只看 `rim > 0` 也不够：球走过之后身后的旧沟比本帧压入更深，`max` 会把整条沟换成一点点正堆脊（−0.70 → −0.02）。

渲染侧：

- **草地**：`GrassWind.shader` 采样 `_InteractionGrassTex` 的梯度，取负梯度方向作为“远离压点”的弯曲方向，叶尖位移最大；同时叠加一点向下的 press。没有草地子集时偏移为 0。
- **雪地**：`SnowGround.shader` 用压实量做垂直下沉 + 坡度法线，并按压实量变暗。
- **沙地**：`SandGround.shader` 用带符号高度做垂直位移（负值下陷、正值隆起）+ 坡度法线，叠加程序化沙纹。

三种地面都放在**透明队列**，并各带一个不做位移的 `DepthOnly` Pass —— 这样现有的 `TransparentDepthMergeFeature` 会把它们当参照面合并进 `_CameraDepthTexture`，不需要改渲染器资产。

---

## 三表面 Demo 场景

`InteractionSurfacesDemo.unity` 用来验证「一套共享捕获 + 多个子集」：

| 物体 | 层 | 作用 |
|------|----|------|
| `Interaction Manager` | 0 | `UniversalInteractionManager` + 三个 Processor + 正交相机子物体；`_followTarget` = 探针，`_areaSize` 12，分辨率 256 |
| `Interaction Camera` | 0 | 正交，Renderer Index = 1（`IA_Renderer`），culling mask = 6&#124;7，跟随探针 |
| `Main Camera` | 0 | Renderer Index = 0，18° 斜俯视；位置必须落在草地 chunk 窗口内（见下） |
| `InteractorProbe` | 7 | 半径 0.7 的球，`InteractionManager/InteractorObject`（`_Intensity` 0.4），`InteractionProbeDriver` 在 x = −12…12 往返 |
| `SnowPatch` | 6 | 12×12 平面，`InteractionManager/SnowGround`，中心 x = −12 |
| `GrassPatch` | 6 | `FlatGrassTerrain`（12×12、单 chunk）+ `GrassChunkManager`（461 株草）+ `InteractionManager/GrassGround` |
| `SandPatch` | 6 | 12×12 平面，`InteractionManager/SandGround`，中心 x = +12 |
| `Field Manager` / `Wind` | 0 | 共享风场，跟随主相机；草地用 `_WindResponse` 消费 |

三块地面**相邻拼接**（−18…−6 / −6…6 / 6…18），探针全程都有参照面，不会经过空隙。

> **主相机为什么在 z = −12**：`GrassChunkManager` 只生成相机所在 3×3 chunk 邻域内的草。草地地形被做成**单个 12 m chunk**，所以相机站在地形外一个 chunk 以内时仍会生成整块草地；再远就只有空地。

重建场景（幂等，会覆盖场景和 Demo 材质）：

```
unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/BuildInteractionSurfacesDemo.cs"
```

构建脚本会临时挂起其他场景里的 Manager / Processor / FieldManager / Provider，避免静态注册表的「唯一活动 Manager」与同名 producer 冲突，保存后原样恢复。

### 验证记录（2026-11-06，Unity 6000.3.14f1 / URP 17.3.0 / Metal）

编辑模式（30 Hz 预览）与 Play 模式采样 RT：

```
origin : max 0.158  非零 556 px      ← 探针半径 0.7、压入 0.4 m × _Intensity 0.4
grass  : max 0.790  非零 556 px      ← 当前接触
snow   : max 0.787  0.79 → 0.67 → 0.56 → 0.48（抬离后 6 s）
sand   : min -0.79 / max +0.11       ← 坑 + 坑沿堆脊；0.79 → 0.61 → 0.48 → 0.37（抬离后 6 s）
草地 chunk : 1 个，461 株，可见 222 株
```

- 捕获峰值与理论值一致（0.4 m × 0.4 = 0.16），三个子集都拿到同一个共享输入。
- 沙地的坑沿在坑外侧（r=13，坑沿 r=12）出现 +0.11 的堆脊，雪地同位置只有 6.6% 的蠕变晕、没有脊 —— 材料差异落到实处。
- 主相机成图上：雪面有蓝色踩印、草地有被压弯的草丛、沙面有探针拖出的沙沟 + 坑沿堆起的沙。
- Play 模式进出与构建脚本执行均无 error / warning。

> **编辑器失焦时 Play 模式会停帧**：`Time.frameCount` 停在进入 Play 后的第 2 帧，探针不动。把 Unity 窗口切到前台后恢复正常（macOS 会节流后台窗口）。排查模拟“不跑”时先确认 `Time.time` 在推进。


---

## 水体互动 Demo 场景

`WaterInteractionDemo.unity` 把上述 6 步全部落地，用来验证「正交相机 → Feature → Manager → Compute → 全局纹理」整条链路：

| 物体 | 层 | 作用 |
|------|----|------|
| `WaterSurface` | 6 `Interaction` | 20×20 平面，材质 `InteractionWater`（Shader `Render/Water`）。交互 RT 在这里变成位移 + 法线。**同时是穿透深度的参照面**：`DepthOnly` Pass 不做位移，`TransparentDepthMergeFeature` 在 `AfterRenderingOpaques` 把它重画进 `_CameraDepthTexture` |
| `InteractorProbe` | 7 `Interactor` | 半径 0.75 的球，材质 `InteractionManager/InteractorObject`。**一个物体两个 Pass**，见下 |
| `InteractionManager` | 0 | `UniversalInteractionManager` + `WaterInteractionProcessor`，正交相机是它的子物体 |
| `InteractionCamera` | 0 | 正交，Renderer Index = 1（`IA_Renderer`），SolidColor 黑，culling mask = 6&#124;7 |
| `Main Camera` | 0 | Renderer Index = 0，斜俯视，culling mask = `-1`（layer 6 是水面本身，必须画；参照面已不外露） |
| `FieldManager` | 0 | `Mine.Fields` 预制体实例，负责调度场生产者（这里只有一个 `Wave`） |
| `Wave` | 0 | `WaveFieldProvider`，3 级 cascade 位移/法线/泡沫，发布 `_WaveDisplacement0..2 / _WaveNormal0..2 / _WaveFieldValid` |
| `SeaBed` | 0 | 60×60 海底，y = -5，`SeaBed` 材质（URP Lit，深青） |
| `Background` / `SeaWall_N·S·E·W` | 0 | 22 m 高的四堵墙，围住 22×22 的水域，挡住地平线和天空 |

> 水面用真正的 `Render/Water` 之后，观察方式从「看热力图」变成「看水面」。水深必须存在：水面下方没有几何体时 `sceneDepDf` 飞到远裁剪面，焦散项会整屏爆白。Demo 因此加了 `SeaBed`。

### 一个物体两个 Pass

`InteractorObject.shader` 用 LightMode Tag 把同一个球分流给两个相机，不需要再挂一个可见外壳：

| Pass | LightMode | 谁画 | 作用 |
|------|-----------|------|------|
| `InteractionCapture` | `InteractionCapture` | 交互相机（`CustomFeature` 的颜色捕获列表，**排在最后**） | `Cull Front` + `ZTest Always`，采样 `_CameraDepthTexture` 输出穿透深度 |
| `Forward` | `UniversalForward` | 主相机（标准 URP 前向） | 输出 `_BaseColor`，就是物体正常的样子 |

标准 URP 的前向 Pass 只认 `SRPDefaultUnlit / UniversalForward / UniversalForwardOnly`，所以主相机永远不会画到 `InteractionCapture`。

**`ForwardTags` 里靠后的 Tag 优先，不是靠前的**（Unity 6.3 / URP 17 实测；文档写的是「第一个匹配的 Tag」）。把 `InteractionCapture` 排在最前时，带两个 Pass 的互动物体永远被 `UniversalForward` 命中，捕获 RT 里存的是 `_BaseColor` 的一整块颜色（球是红的 → 恒 1.0）。后果是把穿透深度退化成**二值圆盘**：上下移动时整片深度一起跳、被 `saturate` 吃掉（水面没反应），左右移动时只剩边缘 0↔1 的满幅阶跃（水面过冲）。现在的顺序是 `SRPDefaultUnlit → UniversalForward → UniversalForwardOnly → InteractionCapture`；没有该 Pass 的普通物体会落到前面几个标准 Tag，不受影响。

**layer 7 必须进主渲染器**：`PC_Renderer.asset` 的不透明 / 透明层掩码原来只有 `119`（0,1,2,4,5,6），layer 7 被排除，主相机连 `UniversalForward` 都画不出来。现已改为 `247`（加上 7）。layer 7 现在的含义是「捕获相机要额外捕获它」，不再是「主相机看不见」。

- `InteractionDebug` 材质只在临时调试平面上用（`_BaseColor` 要调到 8 才看得清涟漪）；Demo 场景已不再摆这个平面。
- `InteractionCamera` 的 Target Texture **由 Manager 在运行时创建并赋值**，场景里存的是 `{fileID: 0}`：重开场景不会多出一份孤儿 RT。
- 场景默认可以被**叠加加载**（不挂 AudioListener），但要看得清请单独打开。
- 重建场景：`unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/BuildWaterInteractionDemo.cs"`（幂等，会覆盖场景与材质）。

### 参照面：水面自己的 DepthOnly Pass

穿透深度 = 交互物背面到「参照面」的距离。参照面曾经是一个专门的 `DepthReference` 平面（layer 6，主相机不画），现在直接用水面本身：

| 环节 | 做法 |
|------|------|
| `Render/Water` | 新增 `LightMode = DepthOnly` 的 Pass，**不做细分、不做位移**，只写 `TransformObjectToHClip(positionOS)` |
| `IA_Renderer` | `TransparentDepthMergeFeature`（layer 6、`shaderTags: [DepthOnly]`、`AfterRenderingOpaques`）把透明表面重画进 URP 的 `_CameraDepthTexture` |
| `InteractorObject` | 捕获 Pass 采样 `_CameraDepthTexture`（已合并过水面），按 `_ProjectionParams.y/z` 线性换到视空间再相减 |

为什么 DepthOnly Pass 必须**排除交互位移**：若参照面跟着位移一起下沉，就会形成「位移越深 → 参照越深 → 穿透越大 → 位移更深」的正反馈，水面会自激。用位移前的平面网格当参照，物理上也正好是「静止水面」这个定义。

前置条件：`IA_Renderer` 的 `CopyDepthMode` 必须是 **ForcePrepass（2）**。`AfterTransparents`（1）只做颜色格式（`R32_SFloat`）的深度拷贝，不是深度附件，`SetRenderAttachmentDepth` 会直接报 `has a color format R32_SFloat`。另外 `CustomFeature` 的颜色捕获排在 `AfterRenderingTransparents`，且 `copyDepth` 关闭时**不能**把 `_CameraDepthTexture` 同时挂成附件再采样。

### 水面接入交互 RT（Shader `Render/Water`）

交互 RT 的材质侧采样统一放在 `Shaders/InteractionField.hlsl`，由 `Water.shader` include 后交给 `WaterFunction.hlsl` 的两个函数消费：

| 位置 | 代码 | 作用 |
|------|------|------|
| Domain | `positionWS += ComputeInteractionWave(positionWS)` | 高度场 → 垂直位移，`height × _InteractionHeightScale` |
| Frag | `normalWS = ComputeInteractionNormal(positionWS, normalWS)` | 中心差分求梯度，把坡度叠到 FFT 法线上 |

`InteractionField.hlsl` 的世界 → UV 映射与 `InteractionDebug.shader` 完全一致：

```hlsl
float4 clip = mul(_InteractionOrthoP, mul(_InteractionOrthoV, float4(positionWS, 1.0)));
float2 uv   = clip.xy / clip.w * 0.5 + 0.5;
```

- `SampleInteractionHeight` 用 `step(abs(uv - 0.5), 0.5)` 把域外压回 0，否则 RT 的 Clamp 采样会在交互域外拖出一整片平台。
- `InteractionSlopeXZ` 的差分步长是一个 RT 纹素的世界尺寸：`_InteractionAreaSize / _InteractionResolution`，不写死，改分辨率时法线粗细自动跟着变。
- `ApplyInteractionSlope` 先把水面法线还原成坡度（`-normal.xz / |normal.y|`）再加交互坡度，避免 `lerp` 两个法线丢长度。

材质参数（`InteractionWater.mat`）：

| 参数 | 值 | 说明 |
|------|----|------|
| `_InteractionHeightScale` | 2.5 | RT 里的 ±1 换算成 ±2.5 m 起伏。超过海底深度会穿帮 |
| `_InteractionNormalStrength` | 2.5 | 交互坡度叠加权重。1.0 时涟漪只靠位移几乎看不出来，法线是主要可见信号 |
| `_TessellationFactor` | 32 | 水面放大到 20×20 后仍要有足够的顶点承载凹陷 |
| `_NormalIntensity` / `_Alpha` / `_FoamIntensity` | 1 / 0.85 / 0.9 | 泡沫阈值调高，减少细碎白点 |
| `_CausticsIntensity` | 0.25 | 全强度会在平整海底上织出整片白网格，0.25 只留几缕 |

**FFT 通道走 `FieldManager`**：`Water.shader` 同时 include `WaveField.hlsl`，波浪数据由 `WaveFieldProvider` 通过 `FieldManager` 发布成全局纹理。`WaveFieldProvider.InitializeField()` 失败时会自己 `enabled = false`，此时 `_WaveFieldValid` 停在 0、`_WaveDisplacement0` 退回 `UnityBlack`，水面是平的但不会报错——排查时先看这两个全局量。

### 物体交互只认帧间差值（静止的物体不扰动水面）

穿透深度**不再**直接当水量：Compute 每帧把当前 `_InteractionOriginTex` 和上一帧的 `_InteractionOriginPrevTex` 做差，只用差值当冲量。

```hlsl
float depthPrev = _InteractionOriginPrevTex[id.xy];     // 上一帧深度（本 kernel 顺便写入，无跨线程读写）
float delta     = depth - depthPrev;                    // 静止 = 0 → 不注入能量
h_new += -delta * _ObjectForce * contact;               // 压入 → 下压，抬离 → 吸起
```

- **静止的球深度恒定，`delta == 0`，完全不产生波**：水不会因为物体一直泡在里面就留下一个恒定的坑，松开后水面自己恢复平静。实测静止 240 帧后 `_InteractionWaterTex` 全零。
- **拖动时**只在接触边缘（深度正在变化的那一圈）打冲量，前缘下压、后缘吸起，形成尾迹；停下后 3 s 内衰减回 `|h| < 0.01`。
- **输出钳位 ±1**：RT 的约定范围（位移由 `_InteractionHeightScale` 换算成米）。连续拖拽时同一片像素会被反复打冲量，不钳位会推到 ±6 以上。
- **`originRT` 不再在每帧处理后清空**：差值机制下，某次没渲染成功时保留旧内容得到的 `delta == 0` 是正确的「没有变化」，而清空会伪造出「球突然消失」的一记冲量。

### 「左右移动过冲、上下移动没反应」的三个原因

这三个是叠加的，缺一个都还是歪的：

**① 捕获 Pass 根本没被画到（见上表：`ForwardTags` 靠后的 Tag 优先）。** 捕获 RT 里存的是 `_BaseColor` 而不是穿透深度。上下移动时整块盘一起跳、又被 `saturate` 压成 0/1，差值近似恒 0（没反应）；左右移动时盘边缘在 0↔1 之间整帧翻转（过冲）。判别方法：给 `_BaseColor` 换个值看 `originRT` 读数跟不跟着变——跟着变就说明画的是 Forward Pass。

**② 正交相机不能用 `LinearEyeDepth` 反解。** `LinearEyeDepth` 是按透视的 `1/(z·raw + w)` 反解的；这台正交相机 `near = -10`，实测恒解到远裁剪面（10 m），于是 `fragEye - sceneEye` 恒为负、整张图输出 0。正交投影是线性的，直接按视锥换算：

```hlsl
#if UNITY_REVERSED_Z
    float sceneEye = _ProjectionParams.y + (1.0 - sceneRaw) * (_ProjectionParams.z - _ProjectionParams.y);
#else
    float sceneEye = _ProjectionParams.y + sceneRaw * (_ProjectionParams.z - _ProjectionParams.y);
#endif
```

`_ProjectionParams.y/z` 是相机自己的 near/far，换了相机参数也不用改代码。

**③ 水线在深度图上是一条硬边。** 球泡在水里时，深度从「水面上的 0」在水线处一步跳到「水面下 0.5 m」，而盘内部是连续的（水线 0.5 m → 球底 1.25 m）。球横向滑 0.03 m，水线那圈像素就整块翻转，`delta` 一步到 **0.7**，与移动速度无关；纵向移动只是把内部深度整体抬降（每帧 0.03 量级）。同一套增益下必然是「左右砸满、上下无感」。Compute 里给单帧差值封了顶：

```hlsl
const float kMaxDelta = 0.03;   // 与纵向移动每帧的平滑变化同一量级
delta = clamp(delta, -kMaxDelta, kMaxDelta);
```

实测（30 Hz，`_ObjectForce` 0.5 / `_Viscosity` 0.08，峰值取 `_InteractionWaterTex` 绝对值最大）：

| 动作 | `kMaxDelta = 0.25`（阶跃几乎不被削） | `kMaxDelta = 0.03` |
|------|--------|--------|
| 横向 1.5 m / 15 帧 | 1.00（钳位） | 0.36 |
| 纵向 0.7 m / 15 帧 | 0.62 | 0.50 |
| 横向 1.0 m / 30 帧 | 1.00（钳位） | 0.88 |
| 纵向 0.7 m / 30 帧 | 0.78 | 0.83 |
| 静止 120 帧 | 0 | 0（不受影响） |

`kMaxDelta` 只削横向的水线阶跃，纵向的平滑变化本来就低于这个值，所以纵向手感不变——这正是要的效果。

### 水面抽搐（静止物体持续扰动）的两个成因

编辑模式下有两条驱动：`EditorTick`（30 Hz，先 `SubmitRenderRequest` 渲染 originRT 再 `Process`）和 `[ExecuteAlways]` 的 `Update`（场景重绘 / 选中变化 / 鼠标移动时都会被调用）。后者不渲染就直接 `Process`，读到的是刚被清空的 originRT：深度 0 → `delta = 0 − 1` 打进一记上吸，下一个 `EditorTick` 又 `delta = 1 − 0` 压下去。一正一负交替注入能量，物体一动不动水面却一直抽搐，且只在编辑模式出现（Play 模式相机每帧自动渲染，不存在这条路径）。

第二个成因是**首帧**：`Process` 第一次跑时 `_InteractionOriginPrevTex` 还是空的，静止泡在水里的球会算出 `delta = 1`，打出一记 0.5 的凹陷加一圈涟漪。每次 `OnEnable` / 域重载（改脚本、重新导入 compute、进出 Play）都会重演，看起来就像「水面隔一会儿自己抽一下」。

三条修法：

1. `Update` 在编辑模式直接 `return`，模拟只由 `EditorTick` 驱动；`EditorTick` 在渲染失败时跳过 `Tick`。
2. `Tick` 不再清空 originRT——没渲染成功时保留旧内容得到的 `delta == 0` 才是对的。
3. Processor 记住 `_primed`：新 RT 的第一帧只记录深度、不注入冲量。

实测（静止探针，30 Hz）：单驱动 120 帧、以及「每帧多来一次不渲染的 `Process`」的旧 bug 形状 120 帧，`_InteractionWaterTex` 都是 `max 0.0000 / meanAbs 0.00000 / 非零 0.00%`；拖动 30 帧仍有 `-1.0000 ~ 1.0000`、`meanAbs 0.0483`、非零 9.63%。编辑模式实机采样 5 s：`origin` 恒为 1（捕获正常），`water` 单调衰减到 4e-5 后不再变化。

### 水体表现：只暴露一个 `_clarity`

美术只调 `_clarity`（0 = 浑浊，1 = 清澈），其余三个物理量按曲线派生。0.5 复现原来手调的那一组（波速 0.15 / 每秒衰减 0.14 / 粘稠度 0.08），所以旧场景不填这个字段也是原来的手感。

| 派生量 | 浑浊 0 | 清澈 1 | 插值 | 作用 |
|---|---|---|---|---|
| `c`（波速） | 0.08 | 0.22 | 线性 | 传播速度：清澈的波跑得远、来得快 |
| `每秒衰减率` | 0.80 | 0.025 | 几何 | 每秒振幅衰减比例（0.14 ≈ 1 s 后剩 86%，与旧 `_Damping 0.995` @30 Hz 等价）；浑浊的余波一秒就散 |
| `γ`（粘稠度） | 0.40 | 0.016 | 几何 | 速度阻尼 `h_new -= γ·(h_curr − h_prev)`：振幅快衰而传播速度不变 |

两个插值方式不同是有意的：衰减和粘稠度都是「小值才有意义」的量，线性插值会让中间段衰减过猛；几何插值（中点取 √(ab)）让滑条中段的观感变化均匀。

**衰减按秒算，不按帧算**：CPU 把「每秒衰减率」换算成本步保留率 `pow(1 − d, dt)` 再交给 compute。旧实现直接乘每帧保留率，同样一组值在 144 Hz 下比 30 Hz 衰减快好几倍，编辑器预览和真机手感对不上。

想临时试极端观感（比如纯糖浆或纯清水）不要改派生式，直接改 `WaterInteractionProcessor` 里那对 `const` 端点。

### 物体互动强度：在物体自己的材质上

水体只管「怎么传」，不管「谁推得多重」。compute 里的 `_ObjectForce` 固定为 **1**，物体强弱由互动物体材质的 `_Intensity`（`InteractionManager/InteractorObject` 的 `Properties`）控制：

```
InteractorObject 捕获 Pass：penetration = 穿透米数 × _Intensity → saturate → _originRT
CSWave：delta = 本帧 − 上帧 → clamp(±0.03) → h += −delta × 1 × contact
```

好处是每个物体各自一份：同一片水，铁球 `_Intensity` 0.5、树叶 0.05，不需要给每个物体配一个「水体」。

实测（`tmp/IntensitySweep.cs`，探针 scale 0.5，`_clarity` 0.5；下压 = y 0.3→−0.5 用 15 帧，横划 = x −1→1 用 15 帧，峰值取 `_InteractionWaterTex` 绝对值最大）：

| `_Intensity` | 0.25 | 0.30 | 0.35 | 0.40 | 0.50 | 1.00 | 2.00 |
|---|---|---|---|---|---|---|---|
| 下压峰值 | 0.359 | 0.516 | 0.701 | 0.895 | **0.998（触顶）** | 0.998 | 0.874 |
| 横划峰值 | 0.248 | 0.309 | 0.367 | 0.392 | 0.402 | 0.415 | 0.288 |

**有效区间约 0.05–0.45**，材质默认 0.4（未饱和的最大值）。再往上被两处钳位吃掉：捕获值 `saturate` 到 0..1、水面输出 `clamp ±1`；超过 1 反而因为过冲失真。横划比下压更早饱和，因为水线是硬边——边沿像素的 `delta` 本来就等于整个穿透深度，`kMaxDelta = 0.03` 一削，`_Intensity` 只再影响 `contact`（`saturate(depth × 4)`，0.25 m 就满）。

想调「这个物体推水有多重」就改它材质的 `_Intensity`，不要动 `_clarity`（那是水的性格）。

#### 传播速度：能调的只有「步进频率」

波速 `c` 受显式五点拉普拉斯的 CFL 条件限制（`c ≤ 0.25`，再大直接发散），清澈端 0.22 已在 88% 上，没有余量。真正决定「波纹多久跑到对面」的是步进频率：

```
传播速度 = √c × (面积 / 分辨率) × 步进频率
```

30 Hz / c=0.22 / 256 px @10 m ≈ **0.55 m/s**（跨 10 m 要 18 s）——这就是「`_clarity` 拉满仍然觉得慢」的原因，不是参数没给够。所以清澈端每帧拆成多个子步、把步进频率提到 120 步/s：

| | 浑浊 0 | 清澈 1 |
|---|---|---|
| 步进频率 | 30 步/s | 120 步/s |

子步数 = `round(步进频率 × 帧长)`，最少 1、最多 8，各子步等分帧长（`subDt = dt / substeps`）。所以每秒步数等于标称值，**传播速度与帧率无关**：编辑模式 30 Hz 拆 4 步，运行时 60 fps 拆 2 步，效果一致。实测清澈端 54 px/s（2.12 m/s，原来 13 px/s），中段 17 px/s。

三个「每步」系数必须随子步数折算，否则子步一多就被重复施加：

| 系数 | 折算方式 |
|---|---|
| `_Damping` | `pow(1 − 每秒衰减率, subDt)`，各子步连乘 = 整帧 |
| `_Viscosity` | `1 − (1 − γ)^(30·subDt)`，以 30 Hz 为标定基准 |
| 边界淡出 | 每步 `pow(窗口, 1/子步数)`，连乘等于整帧乘一次窗口 |

输入只在第一子步消费：`_DeltaPixels`（帧间位移）用完清零，冲量也只注入一次（kernel 自己会把 `_InteractionOriginPrevTex` 更新成当帧深度，后续子步 `delta` 恒为 0）。

代价是每帧多几次 256² 的 dispatch，可以忽略；真要更快就继续抬 `kClearStepsPerSecond`（最高受 `kMaxSubsteps × 帧率` 限制），或者降 `_resolution`（每像素代表更多米，速度按比例上升但涟漪变粗）。

### 编辑模式预览（不进 Play 就能看）

`UniversalInteractionManager` 在 `#if UNITY_EDITOR` 下挂了 `EditorApplication.update`，按固定步长 1/30 s 做三件事：**提交一次正交相机渲染 → 跑一次 `Process` → `SceneView.RepaintAll()`**。不这么做的话，`[ExecuteAlways]` 的 `Update` 在编辑器里只在场景变化时才被调用，波方程不会自己推进；而且 URP 不会替带 Target Texture 的相机在编辑模式下自动渲染，`originRT` 会一直是空的。

`EditorApplication.update` 的调用间隔并不恒定（随编辑器负载在几十到几百 Hz 之间跳），所以这里用**累加器**而不是「下次允许时间」限流：累加真实流逝时间，够一个步长才出一步，慢一拍补一步、快一拍攒着。单次 update 最多补 0.25 s，防止卡顿后雪崩；`RenderOrigin()` 失败时清空累加器（攒下的时间不该用旧深度补）。

每个步长内的顺序固定为 `SyncMatrices() → RenderOrigin() → Tick(dt)`：相机先搬到本步的（吸附后）位置再拍，`Tick` 里消费的 originRT 就是那个位姿的结果。反过来会让压痕滞后一步，PingPong 折返时滞后翻到另一侧，看起来像折返点一顿。

- Inspector 上的 **编辑模式预览**（`_editorPreview`，默认开）可以整体关掉——它会持续占用一点 GPU，并且强制 Scene 视图按 30 Hz 重绘。
- 观察方式：选中 `InteractorProbe`，用 Move 工具拖它，蓝色凹陷会跟着走并向外甩出一圈波纹。
- 主相机和 Scene 视图里它就是那个橙色球；捕获相机单独看它时走的是另一个 Pass，两边互不干扰。
- **水面板静止时是纯黑的**（调试 Shader 输出的是 `高度 × _BaseColor`，零高度就是黑），只有探针脚下和波纹处才有颜色——所以别把 Scene 视图拉太远。
- 编辑模式下的模拟和 Play 模式用的是同一套参数，调 `_clarity` 可以实时看到区别。


### 验证记录（2026-11-04，Unity 6000.3.14f1 / URP 17.3.0 / Metal）

Play 模式（与 GrassManagerDemo 叠加加载，测量时临时禁用另一场景的根物体），暂停后逐帧 `play step`：

```
capture     : max 0.657  非零像素 1108  centroid(185.8, 101.1)
probe       : world(2.278, 0.092, -1.030) → 像素(186.3, 101.6)
water       : min -0.657  centroid(185.8, 101.0)
逐帧校验和  : -524.8 → -522.4 → -518.3 → -512.1 → -504.0 → -494.1 → -482.7 → -469.6
```

- 捕获质心与探针像素坐标一致（差 < 1px）：正交相机 → `CustomFeature` → `_InteractionOriginTex` 的空间映射正确。
- 波面凹陷质心与探针像素一致：`_InteractionOriginTex → Compute → _InteractionWaterTex` 的映射正确。
- 校验和逐帧单调变化：`UniversalInteractionManager.Update` 每帧在跑 `Process`。
- 主相机成图上能看到探针拖出的红/蓝同心波纹环，探针正下方是蓝色凹陷。
- 退出 Play 模式无 error / warning。

### 本次验证修掉的问题

`WaterInteractionProcessor.Release()` 原来用 `ComputeShader.SetTexture(kernel, id, null)` 解绑，而该 API 拒绝 null，会抛 `ArgumentNullException`，导致后面的 `ReleaseRT` 一条都执行不到 —— 每次退出 Play 模式都会报错并泄漏两张 RT。现改为只释放 RT（下次 `Initialize` 重新绑定，中间没有 Dispatch）。

### 未覆盖

- 移动模拟域的重投影已在 `InteractionSurfacesDemo` 里跑通（草 / 雪 / 沙都用 `_DeltaPixels`），水面 Demo 本身仍是静态域。
- 只在 Editor 内验证，未跑构建包。

### 抖动修复的验证（2026-11-06，`Validation~/CheckJitter.cs`）

编辑模式手动步进 40 步（探针沿 x 正弦往返，单步位移 0–1.9 纹素），10 项全过：

```
域中心网格残差           : 0.00000 texel
每步位移整数纹素残差     : 0.00000 texel
_InteractionAreaPos      : 与吸附中心逐帧一致
origin（相机组件已禁用） : max 0.158 / 65536 px
捕获质心 vs (探针−域中心) : 39/39 步，最大误差 0.008 px
grass / snow / sand      : 0.791 / 0.793 / −0.787，非零 656 / 1176 / 1608
```

Play 模式另测：`cameraEnabled = false`、`origin max 0.158 / 551 px`、三个子集非零、无 error。
质心天然按纹素锯齿（域中心整数跳，相对偏移每步 ±1 texel），断言用的是 `(探针 − 域中心) / texel`，不是单调跟踪。

### 边界淡出的验证（2026-11-06，`Validation~/CheckDomainFade.cs` + `CheckTraceSeam.cs` + `CheckWaterFade.cs`）

`CheckDomainFade`：把 `_followTarget` 临时换成钉在原点的空物体（域**不动**），探针沿 x 从 −0.4·area 拖到 +0.48·area，让痕迹一路铺到 RT 边上，然后读回每一张已发布的 `_Interaction*Tex`。三条断言——最外一圈贴着 0、边界带内相邻纹素没有台阶、静置 3 s 后软边不退化：

```
                              拖动后                          静置 3 s 后
grass : 最外圈 0.26%  台阶 9.7%          最外圈 0.46%  台阶 0.0%  （草地回弹干净，带内无内容 → SKIP）
snow  : 最外圈 0.11%  台阶 8.0%          最外圈 0.13%  台阶 8.4%  带内 17/24 纹素仍有值
sand  : 最外圈 0.18%  台阶 8.1%          最外圈 0.23%  台阶 7.2%  带内 18/24 纹素仍有值
```

`CheckTraceSeam`：把 `_followTarget` 换成隐藏标记物，球贴着它压在雪地上（域跟着走），让域把雪痕尾端一路扫出 RT；再把球抬高、标记物走回去，雪痕尾端重新回到域内。断言两条——整行最大相邻台阶 ≤ 20%、尾端从 2% 升到 50% 要横跨 ≥ 4 个纹素：

```
                             断口台阶            尾端渐变
snow  : 97.5% → 14.5%              0 → 5 纹素
grass : 97%  → 15.0%               0 → 26 纹素（回弹已经把它摊平）
sand  : 21.7% → 11.2%              2 → 8 纹素
```

（箭头左边是「读时除、写时乘」版本，右边是「窗口化输入 + 位移吸收」。）

### 雪 / 沙材料动力学的验证（2026-11-06，`Validation~/CheckSurfaceDynamics.cs`）

把 `_followTarget` 钉在原点（域不动、无重投影），探针压到域中心**按住 2 s**，再抬高 3 m 空跑 6 s。
全程读回 `_InteractionSandTex` / `_InteractionSnowTex`，沿穿过峰值纹素的那一行做径向剖面：

```
                      压住 2 s（r 从坑心往外）                        抬离后 6 s
sand  坑 0.787  r0    脊 +0.108 @r13（坑沿 r12）   坑 0.787→0.613→0.477→0.372   脊 0.108→0.048→0.036→0.028
snow  压实 0.787 r0   外沿 0.052 = 峰值 6.6%，全场 min ≥ 0（没有脊）   0.787→0.634→0.512→0.414
```

九条断言全过：沙有坑有脊、脊在坑沿外侧、脊不超过坑深的一半、雪只向下压实且没有脊、两种痕迹都单调变浅并且 6 s 内掉到 80%（沙）/ 85%（雪）以下。

判据里的两个坑，写检查时踩过：

- **脊要按带符号的最大值量**，不能按 `|v|`：坑壁比休止角陡，抬离后按休止线塌向坑内，紧贴坑壁那一格会掉到 0 以下（实测 −0.05）。那是塌坡，不是脊 —— 按 `|v|` 量会把一个负值当成"脊还在"。
- **雪的场只有非负**（正 = 往下压），结构上就表达不出向上的隆起；外沿那 6.6% 是 5×5 蠕变晕（仍然是压痕），所以雪的判据是"外沿不超过峰值 10% 且全场无负值"，而不是"完全没有侧向量"。

`CheckWaterFade`：在吸收带起点（uv ≈ 0.92）压下去再抬起来打一记冲量，空跑 8 s 逐帧采样：

```
峰值 0.2094（frame 2）→ 8 s 后 0.0000
最外圈全程最大 0.001439 = 0.69% 峰值
最外 12 纹素剖面：-0.0055 -0.0091 -0.0044 0.0017 0.0062 0.0084 0.0084 0.0072 0.0059 0.0045 0.0030 0.0014
```

修的时候踩到四个坑，都会让结论反过来：

- **`pow` 版本的最外圈残留 28%**：按时间衰减追不上「每步重新填回来」的印记，压在边上的探针会让最外圈停在 `window^(RATE·dt)`。改成乘窗口后降到 0.15–0.27%。
- **每步把存值再乘一次窗口 → 软边随时间退化成硬边**：带内被反复相乘吃空，只剩内沿 `w = 1` 那道坎（雪静置 3 s 台阶 11% → 48%）。按位移吸收不会有这个退化。
- **「读时除、写时乘」的往返 → 淡出只是显示遮罩**：原始值一直是满的，域扫回来时露出 97.5% 的硬断口（`CheckTraceSeam` 的左边那列）。淡出必须是消耗。
- **改了 `WaterInteractionProcessor.cs` 却没重编译就测**：处理器还在跑旧程序集，新的 uniform 恒为 0，边界淡出等于没开。表现是水的最外圈残留 25%、剖面是一条原始波形。改完 C# 先 `unityctl asset refresh`（会回 `compilationTriggered`）再测，别只看 compute 的 import 成功。

### 验证记录（2026-11-05，接到 `Render/Water` 之后）

```
水面板换成 Render/Water + FFT + 交互 RT
  _WaveFieldValid         : 1        (WaveFieldProvider 发布 Wave_C0..2_Disp)
  _InteractionWaterTex    : min -1.000  max 0.019  非零框 (106,106)-(149,149)
  探针移到 (2.5,-0.5,1.5) : 凹陷质心 (63.5, 89.5)，主相机成图上凹陷跟着走，原位留一圈余波
  四边遮挡                : 把 Main Camera 背景临时设成洋红，洋红像素 0.00% —— 墙把视野完全围住
```

修掉的两个坑：

- **FFT Provider 被自己关掉**：`WaveFieldProvider` 在 `InitializeField()` 失败时会 `enabled = false`（`FieldProvider.OnEnable`），场景被保存后就一直停着。表现是水面平坦、控制台无报错。检查 `_WaveFieldValid`，必要时在 Inspector 重新勾上。
- **改完 Shader 源码不重新导入**：`AssetDatabase.ImportAsset(path, ForceUpdate)` 之前，材质拿不到新属性，`SetFloat("_InteractionHeightScale")` 静默失效（`GetFloat` 读回 0）。
- **海底不存在时焦散爆白**：`ComputeCaustics` 的 `sceneDepDf` 是到海底的距离，没有几何体时它等于到远裁剪面的距离，`eps` 和 `intensity` 一起飞掉。Demo 用 `SeaBed`（y = -5）+ 四面墙兜住。

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
| Clarity | 0.5 | **水体唯一的参数**。0 = 浑浊、1 = 清澈；波速 / 每秒衰减 / 粘稠度 / 步进频率都由它派生。物体互动强弱不在这里，见「物体互动强度」 |

就这一个。原来的 Wave Speed / Damping / Object Force / Viscosity 是四个需要联调的手调量，删掉后不会再出现「不知道该调哪个」。

> **改完 compute 不用再重开场景**：`Process` 每次都会重绑纹理和 `_Resolution`。重新导入 compute 后 shader 内部实例会重建、旧绑定静默失效，以前的表现是 Dispatch 不报错但 RT 全零（偶发 `Property ... is not set`）。

---

## 纹理命名

| 纹理 | 管理者 | 用途 |
|------|--------|------|
| `_InteractionOriginTex` | Manager | 相机渲染的共享交互输入 |
| `_InteractionWaterTex` | WaterProcessor | 波方程输出 |
| `_InteractionWaterPTex` | WaterProcessor | Verlet h_prev（内部） |
| `_InteractionGrassTex` | GrassProcessor | 压弯量 0..1 |
| `_InteractionSnowTex` | SnowProcessor | 踩印量 0..1 |
| `_InteractionSandTex` | SandProcessor | 带符号高度 −1..0.25 |
| `_InteractionGrassValid` / `_InteractionSnowValid` / `_InteractionSandValid` | 各 Processor | 子集是否存在；0 时渲染侧不做任何位移 |
| `_CustomDepthTexture` | CustomFeature（`copyDepth` 开启时） | 正交深度纹理；Demo 里关闭，参照面改由 `_CameraDepthTexture` 提供 |

---

## 扩展

新增交互子集继承 `InteractionProcessor`，声明语义键并实现 6 个钩子：

```csharp
[AddComponentMenu("Mine/Interaction/Mud Interaction Processor")]
public class MudInteractionProcessor : InteractionProcessor
{
    public override string InteractionKey => "mud";
    public override int InteractionOrder => 10;

    protected override bool CreateResources()        { /* FindKernel + 建 RT */ return true; }
    protected override void BindTextures()           { /* 绑定 compute 纹理槽 */ }
    protected override void ProcessStep(float dt, Vector2 worldDelta) { /* dispatch + swap */ }
    protected override void BindGlobals()            { /* Shader.SetGlobalTexture / Valid = 1 */ }
    public override void ResetGlobals()              { /* 黑色 + Valid = 0 */ }
    protected override void ReleaseResources()       { /* 释放 RT */ }
}
```

把它挂到 Manager 同一 GameObject（或任意激活物体）即可：`OnEnable` 自动注册，Manager 下一帧补挂接（`IsReady == false` 时会调用 `Initialize`）。再写一个消费 `_InteractionMudTex` 的表面 Shader 就完成了一个子集 —— 不需要改 Manager、不需要在 Manager 上登记。
