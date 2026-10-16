# DiffuseGI — 屏幕空间间接漫反射

独立入口为 `DiffuseGIFeature.cs`，Shader 为 `PostProcess/DiffuseGI`；统一运行由 [SSGIFeature](../SSGI.md) 调度，合成语义见统一运行说明。

## 使用

1. 在实际相机使用的 Universal Renderer Data 上添加 **Diffuse GI Feature**。
2. 将本目录 `DiffuseGI.shader` 拖入 `settings.shader`，将共享 `SSGITemporal.shader` 拖入 `controls.temporalShader`；后者未指定时保留空间结果，累积仍需 Renderer 实际提供运动向量。
3. 从 Medium 档、Intensity = 1 开始，在场景中放置受光彩色表面与邻近接收面，观察颜色串染。
4. Debug 依次检查 Trace、Indirect、Confidence；Off 显示与原场景色相加的结果。
5. Intensity = 0 且 Debug = Off 时完全跳过本效果。Debug 可独立查看未乘强度的结果。

Feature 在 `AfterRenderingSkybox` 执行，要求 RenderGraph 开启。输入为当前已绘制的场景色、深度和法线；使用 `ConfigureInput` 请求并在各 Pass 声明读依赖。普通 Game/SceneView 基础相机参与，Overlay、Preview 和 Reflection 相机跳过。不会通过读取已经加过本效果的输出形成同帧反馈。

> **事件选择（关键约束）**：URP 17 RenderGraph 按 `RecordCustomRenderGraphPasses(renderGraph, event)` 的调用点插入自定义 Feature，且**同一事件下自定义 Feature 先于 URP 内置 Pass 执行**（`UniversalRendererRenderGraph.cs:1275/1283/1287/1303`：`CopyColorPass` 与透明绘制 Pass 都排在同事件的自定义 Pass 之后）。选 `AfterRenderingSkybox` 因此同时满足两条硬约束：追踪输入只含“不透明几何 + 天空盒”；合成落在 `m_CopyColorPass` 写入 `_CameraOpaqueTexture` 之前。理由与实测见“事件排序”。

## 管线与资源

```text
本帧场景色 + 深度 + 法线
  → Trace（低分辨率，cosine 半球采样 + 深度采样穿越）
  → SpatialResolve.Fine（共享 5×5 核，Trace texel 步幅 1）
  → SpatialResolve.Coarse（读取 Fine，步幅 2）
  → Resolve（全分辨率几何引导上采样）
  → SSGITemporalFilter（可选，运动重投影与历史拒绝）
  → Composite（原场景色 + 接收反照率 × 强度 × GI）
```

所有 GI RT 使用线性 `R16G16B16A16_SFloat`，非 MSAA，无 mip。临时纹理由 RenderGraph 管理；合成读原场景色、写独立目标，最后更新 `resources.cameraColor`，没有同一纹理同时采样与写入。材质在 Create/Dispose 中释放，参数保存到 PassData 并在执行阶段绑定。

| 文件 | 职责 |
|---|---|
| `DiffuseGIFeature.cs` | 档位、资源依赖、两级空间重建编排、相机筛选 |
| `DiffuseGI.shader` | uniforms、四个 Pass、全屏入口 |
| `DiffuseGIFunction.hlsl` | 半球采样、辐亮度估计、保边重建与合成 |
| `../SSGISpatialFilter.hlsl` | 沿用 Specular 的共享空间重建核心，固定 25 样本 |
| `../ScreenSpaceTrace.hlsl` | 通用库的 DDA/RAY 几何适配，以及原深度辅助 |

几何算法复用 `Special/HLSL/RayStepFunction.hlsl` 与 `ScreenSpaceTraceFunction.hlsl`，不复用旧 SSR 颜色接口。默认 DDA 使用旧 SSR 框架的齐次增量、定步数穿越和二分精炼，RAY 保留原世界步进；不接 HiZ、不创建金字塔，方向采样和贡献不变。空间阶段另按 Specular 已验证的宽核重建对齐。合同见 [统一说明](../SSGI.md#几何追踪后端)。

### 共享空间重建

`SSGISpatial_Resolve` 是 SSGI 家族私有流程，两路共用固定 5×5 核、`1/(1+x²+y²)` 空间权重、`exp2(-abs(eyeDepthDelta)*2)` 深度权重与法线幂 32。Diffuse 三档都使用两级空洞式重建：Fine 步幅 1，Coarse 步幅 2，单位为实际 Trace texel；半径只缩放固定 offset，不改变循环边界。两个 RenderGraph Pass 复用 Shader Pass 1，四个 Shader Pass 的索引不变。Specular 保留单遍 2/2/1 步幅。

Coarse 必须读取 Fine 输出，而不是重新读取 Trace。Fine 的相邻像素混合先连接奇偶子网格，再扩大覆盖范围；这与历史单遍步幅 2 候选不同。两级均使用原始相机深度/法线作 guide，保持低分辨率不变，不是生成 mip 金字塔。无边缘/边界时组合最大支持半径为 6 个 Trace texel。旧单遍 Low/Medium 步幅 1.5、High 步幅 1 作为必要 A/B 输入冻结于 `../Validation~/BaselineDiffuseGIFeature.cs`。

相较上一版单遍重建，本阶段增加一张低分辨率 RGBA16F RT 和一次 Raster Pass，每个像素的空间循环从 25 tap 变为两级合计 50 tap（每 tap 另有 guide 查询）；不宣称 GPU 提速。大尺度重建可能抹平同一表面上的真实照明细节，软深度 guide 也不能保证完全消除跨边漏光。本阶段没有增加方差图、亮度权重或历史权重，尚非完整的方差引导去噪。

Diffuse 保留中心/样本法线有效性检查及归一化，RGBA 使用相同权重；不按置信度拒绝样本，也不改总射线平均/黑色 miss。Specular 保留原始法线权重、中心颜色 fallback 和中心 alpha。两者共享算法但不混用 BRDF、环境回退或 alpha 策略。

后续四点上采样仍使用原严格 `depthSigma` 和归一化法线，缺少兼容 guide 时为零。`depthSigma` 现仅控制上采样，不再控制低分辨率去噪；其序列化值/范围/默认 0.15 不变，也没有通过隐藏最小值或倍率改写它。

### 两级验收（2026-10-14）

Metal 相机/共享空间/消费者/历史矩阵 **66 组、1,152,803 断言**通过，距离、错误后端历史、空间颜色与遗漏 Fine 四个负控制均被识别。两级独立 CPU/GPU RGBAFloat 探针最大差 `9.536743e-7`；这不等同于生产 RGBA16F 的精度界限，Specular 三档冻结滤波回归 RGB 差为 0。可复跑入口和探针范围见 [Validation README](../Validation~/README.md)。

同一临时 Renderer、当前复制视角、640×360、Medium/DDA、时缓 0.5、固定 Shader 时间和射线帧 32–47 下，回读生产持久 Diffuse history：帧差亮度 RMS 从 `0.0034007781` 降至 `0.0026334282`（约 22.6%），表面平均亮度约 +0.29%。两路逐像素表面 mask 一致；不是与前阶段旧镜头数字相除，也不是 GPU 性能、运动稳定性或全场景方差结论。PNG 显示平滑范围扩大，真实间接光细节保留、边缘串色与移动镜头观感仍需人工确认。

### 采样序列（2026-10-14）

旧版已使用径向分层/黄金角点集与逐帧二维旋转，并非独立纯随机；问题之一是像素 Hash 的白噪声偏移让过滤邻域内的方向覆盖容易成簇。本轮在保持预算与重建链不变的情况下，尝试面向空间重建的互补采样：

- 每个 8×8 Trace 像素块，将像素的三位坐标按 Morton 次序映射到二维 Sobol 数字网的前 64 个点。未平移的对齐 2×2、4×4、8×8 子块分别具有对应二维格子的互补覆盖；连续旋转后不能再声称严格落在原固定格子内。
- 块级平移复用通用 `Special/HLSL/HashFunction.hlsl::Hash_FracGrid22`；不扩展共享 Hash API。采样/帧调度留在本功能私有 `DiffuseGI_Noise`，不把 GI 生命周期混入通用库。
- 继续逐帧添加原二维低差异旋转，既不冻结采样也不缩窄半球。每条射线在自身径向区间内抖动：`xi.x=(rayIndex+rotation.x)/rayCount`；方位仍为黄金角点集加 `rotation.y`。这避免射线数 4/8 与数字网径向位数叠加时，多个像素的有效径向相位重复。
- `DiffuseGI_Direction` 的完整 cosine 加权半球、黑色 miss、总射线数分母、confidence 和贡献权重全部不变。Low/Medium/High 仍为 4/6/8 条射线、24/48/64 步及 1/8、1/4、1/2 Trace 分辨率。
- 两级 1→2 空间核、上采样、时缓、Specular/AO、步进库均不变，无新增运行时纹理或 Pass。Diffuse 历史签名 `sampling.w=3` 表示实现版本而非空间级数，拒绝此前单遍与两级旧采样历史；只拒绝本通道本帧，不重分配历史。

本方案是程序化邻域低差异采样，**不是离线优化的蓝噪声/STBN**。块边界、几何拒绝和不同光照形状会破坏理想互补性，不保证每个区域/档位都降噪。CPU float32 检查覆盖三档、128 帧、128×128 网格、范围/径向区间/cosine 均值/数字网和四类合成积分；部分 Low/High 的纯径向 horizon 合成项反而更差，不能以总体平均掩盖此限制。

本轮 Metal 正向矩阵 **67 组、1,152,825 断言**及四个负控制通过，包括 `w=2` 旧序列仅失效 Diffuse 历史；Specular 三档冻结回归 RGB 差仍为 0。采样专属 A/B/A 均使用当前生产两级 Feature，旧采样完整 helper 冻结于 `Validation~/RuntimeFixture/SequenceBaselineFunction.hlsl`，不是旧单遍 Feature 或仍 include 活采样的几何基线。

当前复制静止视角、640×360、Medium/DDA、时缓 0.5、固定 Shader 时间、预热 32 请求、采样帧 32–95：
- 原始 Trace（原生 160×90，同帧依赖导出至自有 RGBA16F RT）：亮度帧差 RMS `0.0276067393 → 0.0236816167`，约下降 **14.2%**；时间方差下降约 **18.3%**。
- 生产持久 history：亮度帧差 RMS `0.0026292862 → 0.0022240074`，约下降 **15.4%**；围绕每像素多帧均值的时间方差下降约 **31.9%**，平均亮度约 **+0.10%**。
- 三支逐帧表面 mask、预算、帧号、历史有效和 Shader 时间严格匹配。重复旧基线存在微小非逐位差异；统计量和按平均亮度归一化的均值图 RMS 漂移均受 **0.2%** 上限检查，并报告实际值，不宣称 bitwise 确定性。

这些是本视角的有限窗口收益，不能证明均值图完全无偏、移动镜头无条纹/拖影，也不能与前次空间重建百分比直接相加。结果有改善，但不是“已经消除 0.5 抖动”；需人工观察后决定是否继续做更强的采样/方差引导方案。GPU/同步耗时对比按用户要求仍后置，运行入口见 [Validation README](../Validation~/README.md#sequence-comparison)。

## 估计量与合成约定

Lambert 漫反射有 `f = ρ/π`，cosine 半球采样概率为 `p(ω) = cosθ/π`，所以蒙特卡洛估计简化为：

```text
GI.rgb = (1/N) Σ [Li(hit_i) × edgeFade_i × artisticFalloff_i]
Lo_indirect = ρ × GI.rgb
Final.rgb = Scene.rgb + Intensity × Lo_indirect
```

分母始终是总射线数 N，未命中样本为零，不用命中数归一化。GI.rgb 表示 `E/π` 的屏幕空间近似，还没乘接收面的反照率；不再额外乘 π、cosθ 或 inverse PDF。推导依据：[PBRT Diffuse Reflection](https://www.pbr-book.org/4ed/Reflection_Models/Diffuse_Reflection) 与 [Cosine-Weighted Hemisphere Sampling](https://www.pbr-book.org/3ed-2018/Monte_Carlo_Integration/2D_Sampling_with_Multidimensional_Transformations)。

GI.a 为有效命中的置信度，独立于 RGB；滤波对 RGB/A 使用相同几何权重，合成不再乘 alpha。这样不会因 miss 或边缘淡出重复压暗。滤波分母也不乘 confidence，避免把少量有效命中放大为完整照明。

`distanceFalloff = 0` 关闭额外衰减。启用后仅在命中贡献中乘一次 `1/(1 + hitDistance² × falloff)`；这是美术控制，不是沿真空射线传播辐亮度的物理衰减。

当前 Forward 后处理没有逐像素材质反照率。统一 SSGI 使用白色近似；独立 DiffuseGI Feature 仍可用 `receiverAlbedo` 测试统一颜色近似，Inspector 色值在线性化后用于合成。不能用已经受光的场景色冒充 albedo，否则会重复乘光照。真正的材质反照率/金属遮罩需后续单独接入。

## 参数

统一 SSGI 面板中，DiffuseGI Settings 只包含 Technical；Performance、Intensity、Temporal 位于统一顶层。统一运行固定 Distance Falloff = 0、Receiver Albedo = 白色；这两项仅在独立 DiffuseGI Feature 的 Controls 中开放。

| 分组 | 参数 | 默认值 | 含义 |
|---|---|---|---|
| Technical | traceBackend | DDA | DDA 投影增量采样 / Ray 世界距离采样；Trace target 4.5 |
| Technical | maxDistance | 5 | 世界空间射线最大距离 |
| Technical | thickness | 0.15 | 线性视深度命中厚度，过大易漏光 |
| Technical | normalBias | 0.03 | 起点沿接收法线的偏移，过大丢近接触 |
| Technical | depthSigma | 0.15 | 四点上采样眼深尺度；低分辨率宽核沿用 Specular 的固定尺度 0.5 |
| Performance | performance | Medium | 成本档位，见下表 |
| Performance | temporalBlend | 0.9 | 最大历史权重；0 关闭历史混合 |
| Artistic | intensity | 1 | 合成强度，0 关闭（Debug Off 时） |
| Artistic | distanceFalloff | 0 | 可选距离衰减，0 关闭 |
| Artistic | receiverAlbedo | sRGB 0.8 | 统一接收反照率近似 |
| Debug | debug | Off | Off / Trace / Indirect / Confidence |

| 档位 | 宽高比例 | 像素数比例 | 每像素射线 | RAY/DDA 样本数 |
|---|---|---|---|---|
| Low | 1/8 × 1/8 | 1/64 | 4 | 24 |
| Medium | 1/4 × 1/4 | 1/16 | 6 | 48 |
| High | 1/3 × 1/3 | 约 1/9 | 8 | 64 |

低分辨率尺寸向上取整且不小于 1，过滤步长使用真实低分辨率 RT 尺寸，不依赖 `_ScreenParams`。Trace 使用质量局部关键字编译固定射线/步进预算，无关键字保留 uniform fallback 供冻结几何基线使用；不强制展开循环，也不把编译期预算单独宣称为明显提速。

2026-10-15 性能再平衡仅把 High 的宽高从各 1/2 调为各 1/3，仍为 8 条射线、64 个步进样本，Low/Medium、完整 cosine 半球、两级重建、时缓和 Specular 不变。1080p 原生 Trace 从 960×540 降为 640×360，追踪像素约减少 55.6%；不是把 High 直接降成 Medium（后者为 480×270、6 条射线、48 步）。粗一级的空间网格也扩大同一滤波核的全分辨率覆盖，代价可能是局部照明细节变软、物体边缘阶梯和噪声变化，不保证等画质。

临时 1/4 High 候选曾出现更明显的边缘/细节损失，因此没有合入。1/3 候选的受控静止视角、时缓 0.5、64 帧窗口中，亮度帧差 RMS 约增加 7%，平均亮度约 −0.10%；这是局部取舍证据，不代表地面抖动已解决。正式版本的同步墙钟对照和复跑合同见 [Timing](../Timing.md#diffuse-high-性能再平衡2026-10-15) 与 [Validation](../Validation~/README.md#high-budget-comparison)。

后端、有效几何/距离衰减、采样档与滤波参数变化使本通道历史失效，不释放纹理；最终强度、接收反照率和历史混合权重不在签名中。DDA 完成固定样本后 miss 沿用零贡献，不重跑 RAY；可能漏掉样本间薄前景。当前验证入口及历史逐格验收的区别见 [统一说明](../SSGI.md)，旧验收表是当时 RAY 快照。

High 分辨率变化由历史签名 `_sampling.x` 自动识别；旧 1/2 High 历史只拒绝 Diffuse 一帧，不重分配全分辨率历史 RT，也不失效 AO/Specular。`_sampling.w=3` 仍是当前采样实现版本，不为此重复增加版本号。

## 输出与局限

- `_GITraceTexture`：本相机低分辨率原始估计；`_GITexture`：全分辨率滤波估计。通过 RenderGraph 的 `SetGlobalTextureAfterPass` 发布，消费者须声明全局纹理读依赖。仅在本效果执行后的同相机同帧有效，不是可跨帧保留的历史纹理；禁用效果或相机被跳过时不得消费残留全局绑定。
- 输入场景色是朝相机方向的出射光近似，含直接光、环境光、可能的已有 GI 和镜面高光；无法恢复真正的漫反射入射光。加法合成可能与烘焙/探针 GI 重复，需要按场景调强度。
- 单层深度看不到屏幕外和遮挡背面。追踪输入是“不透明几何 + 天空盒”（`AfterRenderingSkybox`，见“事件排序”），**不含透明几何**；透明物体对最终画面的影响是“在合成之后混合上来”，而不是进入 gather。旧版本用截屏测出的“隐藏 Water/RainDrops 后 GI>0.10 从 39315 降到 3952”属于这种覆盖造成的取景伪影（见“被撤回的旧结论”）。未命中时仍不注入额外环境光，沿用原场景的环境照明。
- 当前接入公共时域双缓冲，启用时逐帧旋转采样并按运动向量、历史深度拒绝。尚未实现方差钳制；遮挡边界和快速光照变化仍可能有拖影。
- 旧 H/V 版本曾测得 GI>0.02 像素相对变化约 4.47%、`depthSigma` 扫描的梯度差 1.48e-4；这些是历史证据。步幅 2 初始候选的静态时缓 0.5 帧差 RMS 降约 15%，但出现格状残留，最终改为步幅 1.5；后续用户改变镜头/时缓，不能继续用旧镜头数字给最终版本算百分比。当前未宣称消除所有随机噪声或 GPU 提速，边缘串色/运动画质仍需确认。
- 不实现 AO/HBAO、HiZ 加速、Cache-Aware 专利采样或 Compute 迁移。早期设计文档中的“最高 62% 加速”不能作为此实现性能结论。
- XR、动态分辨率与多相机的运行结果需分别验证；使用纹理数组/立体宏并不等于已完成这些平台验收。

## 事件排序：透明物体的 GI 可见性（已修复）

**根因（旧值 `BeforeRenderingTransparents`）**：URP 17 RenderGraph 的相关顺序是

```text
1275  RecordCustomRenderGraphPasses(AfterRenderingSkybox)
1283  m_CopyColorPass.Render(...)                      → 写 _CameraOpaqueTexture
1287  RecordCustomRenderGraphPasses(BeforeRenderingTransparents)
1303  m_RenderTransparentForwardPass.Render(...)
```

旧值把 GI 合成放在 1283 之后。透明物体（`Render/Water`、`Render/RainDrops`）用 `SampleSceneColor()` 读 `_CameraOpaqueTexture` 作为 `transparent` 项再做 alpha 混合，于是它们折射/混合到的是**未叠加 GI 的旧拷贝**，把该区域的 GI 结果盖回原状 —— 表现为“水面上看不到 GI”。问题并不是“同事件顺序不可控”：同事件下自定义 Pass 一定排在内置 Pass 之前，真正的问题是**拷贝发生在合成之前**。

**修法**：`renderPassEvent = RenderPassEvent.AfterRenderingSkybox`（`DiffuseGIFeature.cs` → `DiffuseGIPass` 构造函数，单行 + 注释）。合成因此早于 `_CameraOpaqueTexture` 拷贝，而追踪输入仍是“不透明 + 天空盒”。

**运行期核对（2026-09-21）**：反射读 `PC_Renderer` 的 7 个 Feature，`DiffuseGIFeature ... passEvent=AfterRenderingSkybox`；效果仍然生效 —— intensity 0 → 1 有 28370 px 变亮、平均 +0.03144、最大 +0.57568，`Debug=Indirect` 最大通道 2.2207。

> **「7 个」是测量时点的快照，不是当前值**：同日稍后 AO（Phase 4）注册进同一 Renderer，现为 8 个（见 [AO.md](AO.md)）。本节及其下各表的读数均为该时点采集。

### 被撤回的旧结论

旧版本把两处现象归因于“与透明 Pass 同事件、顺序不可控导致全局 RT 被改写”，现已撤回：

- **Confidence 出现色度**：复合 Pass（`DiffuseGI.shader:80`）在 `Debug=3` 时无条件返回 `float4(indirect.aaa, 1.0)`，按构造即是灰度，它不可能产生色度。屏幕上测到的 12.16% 色度像素只能来自**在本效果之后绘制**的透明几何覆盖（源码 1275/1287 都早于 1303）。因此这是“调试视图被透明层覆盖”的取景伪影，不是 in-place 读写别名。Water/RainDrops 隐藏后 12.16% → 0.82%，与水面占屏比例相符。
- **“透明几何进入 gather 命中”**：同理，“隐藏 Water/RainDrops 后 GI>0.10 从 39315 px 降到 3952”是用**屏幕**采集得到的；水面本身覆盖约 12% 屏（≈40k px），其色值混进 debug 输出即可产生这一落差，无需追踪真的命中透明几何。要判定追踪是否读到透明几何，应回读 `SetGlobalTextureAfterPass` 发布的 `_GITexture` 全局纹理，而不是截屏。**开放项**。

### 残留现象：启用 Feature 会让水面像素轻微变暗（开放项）

把 intensity 从 0 抬到 0.0001（GI 贡献 ≈ 0，只是把 Feature 从“整体跳过”切换为“启用”，见 `AddRenderPasses` 的 `intensity <= 0f && debug == Off` 短路）：

| 对照 | 负差值像素 | 均值 |
|---|---|---|
| 0 → 0.0001（仅启用 Feature） | 197 | −0.02722 |
| 0.0001 → 1（GI 的真实增量） | **0** | 0.00000 |
| 0 → 1（总计） | 162 | −0.02482 |
| 0 → 0.0001，隐藏 `Plane (1)`（Water） | **0** | 0.00000 |
| 0 → 0.0001，事件改回 `BeforeRenderingTransparents` | 197 | −0.02722 |

结论：负差值**与 GI 无关**（GI 增量负像素为 0；`Debug=Indirect` 逐通道最小值为 0，间接光处处 ≥ 0），**也与事件顺序无关**（改回旧事件数值不变），只与“Feature 被启用”给相机管线带来的路径变化有关 —— `ConfigureInput` 请求法线会打开 DepthNormals prepass，而自写 `activeColorTexture` 会启用中间色 RT；Water 同时采样 `_CameraDepthTexture` 与 `_CameraOpaqueTexture`，所以只有水面像素受影响（隐藏水面即归零）。量级 0.027 亮度 / 约 200 px，不影响本效果可用性；prepass 与中间纹理哪一项是主因尚未区分。

**注意排除项**：`Plane (2)`（`Render/SimpleParallax`，AlphaTest、`ZWrite 0`）与上述现象无关——禁用它后所有统计量逐字节不变。

## 验收

测量方法：把主相机复制到离屏 RenderTexture 采集（`ScreenCapture.CaptureScreenshotAsTexture` 在 Play Mode 下不可用），比较 Feature 开/关、Debug 模式、`intensity` 等状态的多张读数。状态改动与读取之间需要等一帧，脚本经 `unityctl` 执行（`script execute` 会忽略 `-u` 程序集引用，须用 `script eval` + `-u`，见 `.agents/agents/unity-developer/cli/unityctl.md`）。

| 项 | 方法 | 结果 |
|---|---|---|
| 关闭一致性 | `intensity=0` 且 Debug=Off vs Feature 禁用 | 逐像素位精确一致：`\|OFF − Z\| = 0.000000` |
| 屏幕外/天空 | Skybox vs 纯亮绿 (0,40,0) vs 纯黑 | `\|GI(sky) − GI(brightGreen)\| = 0.00000000`，`\|GI(sky) − GI(black)\| = 0.00000000` → **PASS**，gather 完全忽略天空盒 |
| 正交投影 | 同场景正交 vs 透视 | 覆盖 5.70% / 均值 0.03103（透视 13.42% / 0.05008）→ 可用，只剩目视质量 |
| 三档性能 | 渲染后回读 `_GISourceSize` / `_GIRayCount` / `_GIStepCount` uniform | 与档位表一致 → 接线正确 |
| 滤波 | 滤波前后梯度对比 | 相对变化约 4.47%（GI>0.02 像素）；`depthSigma` 0.01→2.0 再变 1.48e-4 → 保边弱平滑 |
| 确定性 | `Time.timeScale = 0` 后重复采集 | 位精确一致 |
| Debug 通道 | Trace / Indirect / Confidence | 均为全分辨率输出，未被 SSR Debug 或后续 Pass 覆盖；透明层隐藏时 Confidence 为灰度（0.82% 色度像素） |
| 事件排序 | 运行期读 pass 事件 + 透明层开/关对照 | **已修复** → `AfterRenderingSkybox`；GI 生效（+28370 px）；残留水面变暗已重新定性为开放项，见“事件排序” |

**尚未测量**（收尾时按开放项处理，未做进一步扫描）：薄墙漏光、深度边缘颜色串色、相机运动稳定性；透明物体在修复后的实际观感需在 Game View 人工确认；`_GITexture` 全局回读以判定 gather 是否命中透明几何；水面变暗的 prepass / 中间纹理归因。

编译与运行检查结果另见当日 memory（`.agents/agents/unity-developer/memory/2026-09-21-diffusegi-ssgi-phase3.md`）；上表只覆盖可离线对照的数值项，视觉质量需在 Game View 人工确认。
