# 交互系统多子集化：草地 / 雪地 / 沙地共用一套正交捕获（2026-11-06）

## 结论

`Assets/Mine/Scripts/InteractionManager/` 从「Manager + 单个水面处理器」改为与
`FieldManager` / `InstanceManager` 同构的**多子集插件结构**：`UniversalInteractionManager`
只管理共享输入（`_InteractionOriginTex`、正交矩阵、交互域参数）并按顺序驱动所有
`InteractionProcessor`；每个子集自管输出 RT 和全局纹理，互不引用。

新增三个子集：`GrassInteractionProcessor`（`grass`）、`SnowInteractionProcessor`（`snow`）、
`SandInteractionProcessor`（`sand`）。它们共用同一台交互相机、同一个 `_followTarget`
和同一块 12 m 交互域。

**2026-11-06 落地**：`InteractionSurfacesDemo.unity`，三块 12×12 地面相邻拼接
（雪 −18…−6 / 草 −6…6 / 沙 6…18），探针在 x = −12…12 往返。

## 结构

- `IUniversalInteractionProcessor`：`InteractionKey` / `InteractionOrder` / `IsReady` /
  `Initialize` / `Process` / `BindGlobalTextures` / `ResetGlobals` / `Release`。
- `InteractionProcessor`：抽象基类，负责注册、生命周期、RT 创建/释放、`WorldDeltaToPixels`。
  **必须带 `[ExecuteAlways]`**，否则编辑器加载场景时普通 MonoBehaviour 不触发 `OnEnable`，
  处理器不注册，编辑模式预览永远是空的（本轮实测踩到）。
- `UniversalInteractionManager`：静态 `List<InteractionProcessor>`，`Register/Unregister`
  在 `OnEnable` / `OnDisable` 自动执行；语义键重复拒绝并报错；按 `InteractionOrder`
  稳定插入；`Tick` 里对 `IsReady == false` 的处理器补一次 `AttachProcessor`。
- 单活动 Manager 保护：`HasActiveManager && Instance != this` → 报错并禁用后来者。
- 全局量：`_InteractionGrassTex` / `_InteractionSnowTex` / `_InteractionSandTex`，
  各配 `_Interaction*Valid`（绑定时 1、复位时 0）。渲染侧采样都乘 Valid，
  避免子集缺席时未绑定的全局纹理默认成白、整片地面被压弯。

## 三个子集的语义

| 子集 | Compute 核心 | 输出 | 参数 |
|------|--------------|------|------|
| 草地 | `max(prev − recovery·dt, contact)` | 压弯量 0..1 | `_springBack`（回弹速度） |
| 雪地 | 压实 `max` → 蠕变扩散 → 乘性沉降（见下） | 压痕 0..1（只有非负） | `_creep`、`_settle` |
| 沙地 | 挖坑 → 挤出堆脊 → 休止角塌落 → 乘性回平（见下） | 带符号高度 −1..0.25 | `_rim`、`_repose`、`_settle`、`_surfaceScale` |

- 草地渲染：`GrassWind.shader` 取 `_InteractionGrassTex` 的**负梯度**作为“远离压点”的方向，
  叶尖权重 `uv.y`，叠一点向下 press；世界空间偏移，不经过 TBN。
- 雪地/沙地渲染：`SnowGround.shader` / `SandGround.shader` 做垂直位移 + 坡度法线；
  两者都放透明队列并带**无位移 `DepthOnly` Pass**，交给现成的
  `TransparentDepthMergeFeature` 合并参照深度（不改渲染器资产）。
- 移动域重投影：三个 compute 都用 `_DeltaPixels = round(worldDelta × res / areaSize)`
  从上一帧坐标 `id + _DeltaPixels` 取历史；ping-pong RT，Swap 后立刻 `BindGlobals()`。

## 雪 / 沙材料动力学：两种材料，两套方程（2026-11-06）

原来三个子集都是「统一留下痕迹」：雪按下沉量回填、沙按邻域平均摊平，本质是同一种"印记"。
要求是分开——**痕迹随时间消失**，且**沙（小形变）被推到边上堆起、雪（大形变）直接压下去**。

判据是材料能不能承载侧向位移：沙的颗粒不可压实，体积只能被挤到旁边（坑 + 坑沿脊）；
雪的晶格压塌、体积被吃掉（只下沉，没有脊）。

**沙**（`SandInteraction.compute`，四步，顺序有意义）：

```
h = min(h, -press)                                    ① 挖坑（只在 press > 0 的纹素）
if (rim > 0 && h >= -press) h = max(h, rim)           ② 堆脊
   rim = _Rim · relu(boxAvg5x5(pressRaw) − pressRaw)
d = h − prev[n];  Δ = ∓(d − L)                       ③ 休止角（纯 gather，对称交换）
h *= exp(−_Settle·dt)                                 ④ 乘性回平
```

- ②的压差场 `boxAvg − press` 只有在「自己比周围压得轻」的地方为正 —— 正是压头轮廓外侧那一圈。
  用 5×5 均值而不是 5 点拉普拉斯：后者在圆盘压痕上只在正边沿炸出一格尖峰，凑不出有宽度的脊。
- ③ `L = tanθ · texelWorld / _surfaceScale`（`_surfaceScale` 必须等于 `SandGround._DepthScale`，
  只用来把角度换算成归一化高差）；`rate = 1−exp(−k·dt)`，`k = 2/s`（压住时每帧重新堆，
  抬离后慢慢塌；8/s 太急，脊抬离几帧就塌回坑里）。**对称交换**（我塌给它、它抬向我）所以质量近似守恒，
  中心读本帧、邻居读上一帧 → 无写竞争，不需要第二个 Pass。

**雪**（`SnowInteraction.compute`）：

```
imprint = max(prev, press)                            ① 压实（没有脊）
imprint = lerp(imprint, mean4(prev), 1−exp(−_Creep·dt))  ② 蠕变：坑壁摊圆
imprint *= exp(−_Settle·dt)                           ③ 乘性沉降
```

**乘性衰减而不是线性回填**：`h − k·dt` 会把坑底磨平、形状失真；`h·exp(−k·dt)` 保持形状、整体等比变浅，
这才是「痕迹随时间消失」该有的样子（沙的坑和脊也一起等比变浅，不会破坏比例）。

### 三个把人坑住的守卫（都实测复现过）

1. **`if (press > 0) h = min(h, −press)`**：抬离后 `press == 0`，无条件的 `min` 会把坑沿所有**正值**压成 0 ——
   脊一离地就没了（实测 0.108 → 0.000，2 s 内）。挖坑只能发生在球真压到的纹素上。
2. **`if (rim > 0 && h >= −press) h = max(h, rim)`**：无条件的 `max` 在 `rim == 0` 处把**负的坑** max 成 0，
   坑从 −0.79 变 0（表现是"沙地只剩一圈环"，因为只有 rim > 0 的纹素活下来）。
   只加 `rim > 0` 还不够：球走过之后身后的旧沟比本帧压入更深，`max` 会把整条沟换成一点点正堆脊
   （实测拖动后空跑 10 帧，沟 −0.70 → −0.02）。判据用 `h >= −press`（"本帧没被挖穿"）。
3. **横向搬运要乘窗口**：蠕变（雪）和休止角塌落（沙）都是横向搬运，贴着边界搬运等于每帧把域内的量
   重新推进淡出带，窗口只挡输入挡不住它（雪最外圈残留 1.11% → 0.11%，沙 1.32% → 0.23%）。
   乘**速率**不乘存值，所以静置没有累积衰减。

### 抬离后坑沿为什么会掉到 0 以下（不是 bug）

坑壁比休止角陡时，抬离后按休止线塌向坑内；休止线从坑底（r=11，−0.264）以 `L`（≈0.094/纹素）上升，
要走到 r≈14 才回到 0，所以紧贴坑壁那一格**必然**略低于原始地面（实测 −0.05）。
真实干沙挖坑后坑壁内塌也是这样。检查里量脊必须取**带符号的最大值**，不能取 `|v|`，否则会把塌坡当成脊。

### 验证

`Validation~/CheckSurfaceDynamics.cs`（新，9/9）：`_followTarget` 钉原点、探针压到域中心按住 2 s，
再抬高 3 m 空跑 6 s，沿穿过峰值纹素的行做径向剖面。

```
                       压住 2 s                                  抬离后 6 s
sand  坑 0.787 r0     脊 +0.108 @r13（坑沿 r12）   坑 0.787→0.613→0.477→0.372   脊 0.108→0.048→0.036→0.028
snow  压实 0.787 r0   外沿 0.052 = 峰值 6.6%，全场 min ≥ 0    0.787→0.634→0.512→0.414
```

雪的判据是「外沿 ≤ 峰值 10% 且全场没有负值」（它的场只有非负，结构上表达不出向上隆起，
那 6.6% 是 5×5 蠕变晕、仍然是压痕），不是「完全没有侧向量」。

回归：`CheckDomainFade` 15/15、`CheckTraceSeam` 7/7、`CheckJitter` 10/10（sand max 0.115 / min −0.788，
脊出现在 max 上）、`CheckWaterFade` 4/4（需先切到 `WaterInteractionDemo.unity`，否则报「缺对象」）。

## 验证（Demo 场景）

- `InteractionSurfacesDemo`：编辑模式 / Play 模式采样 RT，
  `origin max 0.158 / 556 px`（探针半径 0.7、压入 0.4 m × `_Intensity` 0.4，理论值吻合），
  `grass max 0.790 / 556 px`、`snow max 0.787`、`sand min −0.79 / max +0.11`（坑 + 坑沿脊）。
- 草地：1 个 chunk、461 株、可见 222 株；主相机成图上可见草叶被压弯、雪面蓝色踩印、沙面沙沟。
- 构建与进出 Play 均无 error / warning。

## 注意

- **`GrassChunkManager` 只生成相机 3×3 chunk 邻域内的草**：12×12 草地必须做成
  **单个 chunk**（`_chunkSize = 12`），否则相机站在地形外时只生成近处一条；
  主相机 z 也要落在地形外一个 chunk 以内（Demo 取 z = −12）。
- **静态注册表跨场景**：构建 Demo 时必须先挂起其他场景的
  `UniversalInteractionManager` / `InteractionProcessor` / `FieldManager` / `FieldProvider`，
  保存并关闭新场景后再原样恢复；否则新 Manager 会被「唯一活动 Manager」守卫禁用，
  新 Wind 会被同名 producer 拒绝并自我禁用，且这些状态会被写进保存的场景。
- **编辑器失焦时 Play 模式会停帧**（macOS 后台节流）：`Time.frameCount` 停在进入 Play 后的
  第 2 帧，探针不动。把 Unity 窗口切到前台即可恢复；排查“模拟不跑”先看 `Time.time`。
- `[ExecuteAlways]` 的探针驱动在场景关闭时可能收到一次已排队的 `EditorApplication.update`
  回调，`EditorUpdate` 开头要判 `this == null`，否则偶发 `MissingReferenceException`。
- **编辑器卡死时所有 GPU 读写都静默返回 0**，看起来像「交互捕获坏了」：本轮为此排查了
  RenderGraph 管线、深度合并、renderer list，全部是假象。判据：用
  `Graphics.Blit(纯色 Texture2D, rt)` + `AsyncGPUReadback.WaitForCompletion()` 做一次自检，
  读回也是 0 就说明是编辑器/GPU 停了（`Time.frameCount` 不涨、Unity 占用高但无响应），
  重启编辑器即可；**先做这个自检再怀疑渲染代码**。
- 水面 Demo 里没有 `InteractionProbeDriver`（探针是静态的），所以水面一直是平的 ——
  捕获值 0 不代表链路断，探针不动就没有力注入。

## 痕迹抖动：域吸附 + 位姿时序（2026-11-06 修）

现象：探针在移动时，雪/沙/草上的痕迹一直抖；水面 Demo 看不出来。

**水面 Demo 看不出来不代表水系统没这个病**：`WaterInteractionDemo` 的
`UniversalInteractionManager._followTarget` 是 `{fileID: 0}`（域中心 = Manager 自身，
静态），而且没有 `InteractionProbeDriver`。域不动 → `worldDelta` 恒 0、
`_DeltaPixels` 恒 0、捕获位姿恒定，两个成因都被掩盖。重构前的水面代码
（`git show 9fbd03d:.../InteractionManager.cs`）逐字有同样的两个缺陷：

1. **整数重投影吃掉亚纹素位移**。`texel = areaSize / resolution = 12 / 256 = 4.6875 cm`；
   探针 2.88 m/s 在 30 Hz 下单步 9.6 cm ≈ 2.05 texel，`round` 后残差 0.05 texel/步 →
   痕迹相对地面持续滑；摆动幅度 < 半个纹素时 `round` 在 0 / ±1 之间来回跳 →
   整体一帧一格地闪，就是「球动的时候痕迹抖」。
2. **捕获位姿 ≠ delta 位姿**。编辑模式 `RenderOriginInEditor()` 排在 `Tick()` 之前，
   而 `SyncMatrices()` 在 `Tick` 内部才执行 —— 拍到的是上一步的位姿；Play 模式
   `Tick` 消费的是上一帧帧末相机渲染的结果，同样滞后一步。PingPong 折返时滞后
   翻到另一侧，看起来像折返点一顿。

修法（`InteractionManager.cs`）：

- `SnappedCenter`：域中心 `x/z` 各自吸附到 `round(c / texel) * texel`，相机、
  `_InteractionAreaPos`、`worldDelta` 全部以它为唯一来源 → 单步位移恒为整数 texel，
  `_DeltaPixels` 是**精确**整数，重投影是纯整数搬移，没有残差。
- 统一时序 `SyncMatrices() → RenderOrigin() → Tick(dt)`，编辑模式与 Play 模式同一条路径；
  `Update` 在编辑模式下依旧提前返回（否则会和 `EditorTick` 双重驱动）。
- 交互相机组件 `enabled = false`（`EnsureInteractionRT` 里关、`ReleaseRT` 里还原），
  只由 `RenderPipeline.SubmitRenderRequest` 手动提交，避免帧末再渲一次。
  **禁用相机组件时 `SubmitRenderRequest` 照样出图**：Play 模式实测
  `origin max 0.158 / 551 px`。

验证脚本 `Validation~/CheckJitter.cs`（10 项全过）：域中心网格残差 0、每步位移整数纹素、
`_InteractionAreaPos` 与吸附中心一致、禁用相机后 origin 仍有 0.158 / 65536 px、
捕获质心严格等于 `(探针 − 域中心) / texel`（39/39 步，最大误差 0.008 px）、三个子集非零。
注意质心天然是**锯齿**的（域中心按整数跳，相对偏移每步 ±1 texel），别按「单调跟踪」去断言。

## 域边界淡出：统一一条常量（2026-11-06 修，数据级）

`InteractionField.hlsl` 的 `InteractionDomainMask` 原来是 `step(abs(uv - 0.5), 0.5)` 硬裁，
痕迹走到域边上被切出一条笔直断口（域移动时还是一条跟着扫的硬边）。

**淡出必须在数据里做，不能在渲染侧做。** 第一版只把 `InteractionDomainMask` 换成
`smoothstep` 窗口，RT 里的值照样是满的 —— 长时间来回拖，域把旧痕迹甩到边界外时
还是切出一条直壁；水面更是会在自己的域边界上当墙撞（Dirichlet 反射）。
现在的约定：新建 `Shaders/InteractionDomainEdge.hlsl`，宽度只有一个常量
`INTERACTION_DOMAIN_FADE 0.08`（占 RT 边长比例，12 m / 256 上约 0.96 m、20 纹素）。
渲染侧的 `InteractionDomainMask` 退回**硬的内外判断**，只负责挡住 Clamp 采样把域内边缘
拖到域外；再叠 smoothstep 会和数据里的窗口相乘，把淡出带压窄一半。

**印记类子集（grass / snow / sand）两个动作，缺一不可**：

```hlsl
float InteractionDomainFade  (value, pixel, res) { return value * window; }        // ① 只乘输入
float InteractionDomainAbsorb(pixel, res, absorbPerBand);                          // ② 按位移吸收
```

① 窗口乘在**这一帧新进来的接触深度**上（不是写回的旧值）。压边探针只能压出 ≈ 0，
痕迹自带 20 纹素渐变。
② 吸收量 = 这一步域移动的距离 ÷ 淡出带宽度 × 12（`InteractionProcessor.EdgeAbsorbPerStep`），
域不动时恒为 1。穿过整条带约 e^-6 ≈ 0.25%，**数据是真的被吃掉**。

**三条错路，都实测过，别再走**：

- **每步把存值再乘一次窗口** → 按步累积衰减，带内吃空、只剩内沿 `w = 1` 那道坎：
  雪静置 3 s 带内台阶 11% → **48%**（用户报的「等一会儿软边变硬边」）。
- **「读时除、写时乘」的往返**（`Unfade` 还原原始值）→ 静置不变形了，但存进去的原始值
  一直是满的，淡出只是**显示遮罩**；域把痕迹扫出 RT 时数据没被消耗，走回来就恢复满强度，
  紧挨着真正掉出 RT 的那一格（0）—— 域中间凭空多出一条 **97.5%** 的硬断口
  （用户报的「软只是一层遮罩，球体回走时下面还是硬的」）。
  `Validation~/CheckTraceSeam.cs` 就是复现它：域扫出去再走回来，一行里找最大台阶。
- **按时间指数衰减**（`value * pow(window, RATE*dt)`）：印记每步用原始接触值重新填回
  （`max(prev, depth)`），衰减追不上，最外圈停在 `window^(RATE·dt)`（实测残留 28%）。

吸收按**位移**而不是时间的理由：域停住时痕迹一点不该变（按时间会继续掉，且退化成硬边）；
球慢慢走和快跑也不该衰减不一样。

**水面额外上一条按时间的海绵带**：波是动的，必须真的耗能，否则从 0 墙反射回来。
每子步乘 `exp(-_Sponge * (1 - window))`，`_Sponge = 12 * subDt`（帧率无关，按子步折算，
且只在第一个子步吃一次位移吸收）；注入的冲量也乘窗口。实测最外圈全程 ≤ 0.69% 峰值、8 s 归零。

**沙地的横向流动在淡出带里关掉**（`Flow × window`）：扩散就是把坡摊平，贴着边界摊平
等于把 0 抬起来，边界纹素会被邻域填满（实测最外圈残留 1.32% → 0.23%）。乘的是**速率**
不是存值，所以没有累积衰减。

验证脚本：`Validation~/CheckDomainFade.cs`（任意场景通用，自动找所有已发布的
`_Interaction*Tex`）——把 `_followTarget` 临时换成钉在原点的空物体（域不动），
探针从 −0.4·area 拖到 +0.48·area 让痕迹铺到 RT 边，三条断言：「最外圈 ≤ 峰值 1%」、
「边界带内相邻纹素台阶 ≤ 峰值 25%」、「静置 3 s 后带内仍有 ≥ 4 个纹素带值」。
实测拖动后 grass / snow / sand 最外圈 0.26 / 0.11 / 0.18%，台阶 9.7 / 8.0 / 8.1%；
静置 3 s 后 0.46 / 0.13 / 0.23%，台阶 0.0 / 8.4 / 7.2%（草地回弹干净，带内空 → 跳过）。
`Validation~/CheckTraceSeam.cs` —— 球压着地面走、域把痕迹尾端扫出 RT，再把球抬高走回来，
断言「整行最大台阶 ≤ 20%」+「尾端 2%→50% 横跨 ≥ 4 纹素」：雪 97.5%→14.5% / 0→5 纹素，
草 97%→15.0% / 0→26，沙 21.7%→11.2% / 2→8。
`Validation~/CheckWaterFade.cs` ——在 uv ≈ 0.92（吸收带起点）压下去再抬起来，空跑 8 s：
峰值 0.2094、最外圈全程 ≤ 0.001439（0.69%）、8 s 后归零。

**排查这个坑时踩到的第三个坑**：改了 `WaterInteractionProcessor.cs` 却只 import 了
`.compute` 就测 —— 处理器还在跑旧编译产物，新的 uniform 恒为 0，淡出等于没开，
水的最外圈残留 25%、剖面是原始波形。改完 .cs 先 `unityctl asset refresh`
（返回里看有没有 `compilationTriggered`）再测。

PNG 目视对照仍然有用：域中心固定（`_followTarget = null`，Manager 挪到 x = 9 → 域 [3, 15]），
拖一条跨边界的沙沟 / 草痕再截图。注意非 Play 模式下拿不到编辑器窗口的帧，
要自己 `RenderPipeline.SubmitRenderRequest(Camera.main, SingleCameraRequest{destination=rt})` 再写 PNG。
