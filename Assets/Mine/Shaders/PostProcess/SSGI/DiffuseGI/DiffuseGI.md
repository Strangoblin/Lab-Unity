# DiffuseGI — 屏幕空间间接漫反射

基于 [SSGI 骨架 Phase 3](../SSGI_Content_Skeleton.md) 实现。独立入口为 `DiffuseGIFeature.cs`，Shader 为 `PostProcess/DiffuseGI`；统一运行由 [SSGIFeature](../SSGI.md) 调度。

## 使用

1. 在实际相机使用的 Universal Renderer Data 上添加 **Diffuse GI Feature**。
2. 将本目录 `DiffuseGI.shader` 拖入 `settings.shader`，将共享 `SSGITemporal.shader` 拖入 `settings.temporalShader`；后者未指定时保留空间结果。
3. 从 Medium 档、Intensity = 1 开始，在场景中放置受光彩色表面与邻近接收面，观察颜色串染。
4. Debug 依次检查 Trace、Indirect、Confidence；Off 显示与原场景色相加的结果。
5. Intensity = 0 且 Debug = Off 时完全跳过本效果。Debug 可独立查看未乘强度的结果。

Feature 在 `AfterRenderingSkybox` 执行，要求 RenderGraph 开启。输入为当前已绘制的场景色、深度和法线；使用 `ConfigureInput` 请求并在各 Pass 声明读依赖。普通 Game/SceneView 基础相机参与，Overlay、Preview 和 Reflection 相机跳过。不会通过读取已经加过本效果的输出形成同帧反馈。

> **事件选择（关键约束）**：URP 17 RenderGraph 按 `RecordCustomRenderGraphPasses(renderGraph, event)` 的调用点插入自定义 Feature，且**同一事件下自定义 Feature 先于 URP 内置 Pass 执行**（`UniversalRendererRenderGraph.cs:1275/1283/1287/1303`：`CopyColorPass` 与透明绘制 Pass 都排在同事件的自定义 Pass 之后）。选 `AfterRenderingSkybox` 因此同时满足两条硬约束：追踪输入只含“不透明几何 + 天空盒”；合成落在 `m_CopyColorPass` 写入 `_CameraOpaqueTexture` 之前。理由与实测见“事件排序”。

## 管线与资源

```text
本帧场景色 + 深度 + 法线
  → Trace（低分辨率，cosine 半球采样 + 世界空间首个命中）
  → BlurHorizontal → BlurVertical（深度/法线双边权重）
  → Resolve（全分辨率几何引导上采样）
  → SSGITemporalFilter（可选，运动重投影与历史拒绝）
  → Composite（原场景色 + 接收反照率 × 强度 × GI）
```

所有 GI RT 使用线性 `R16G16B16A16_SFloat`，非 MSAA，无 mip。临时纹理由 RenderGraph 管理；合成读原场景色、写独立目标，最后更新 `resources.cameraColor`，没有同一纹理同时采样与写入。材质在 Create/Dispose 中释放，参数保存到 PassData 并在执行阶段绑定。

| 文件 | 职责 |
|---|---|
| `DiffuseGIFeature.cs` | 档位、资源依赖、五阶段编排、相机筛选 |
| `DiffuseGI.shader` | uniforms、五个 Pass、全屏入口 |
| `DiffuseGIFunction.hlsl` | 半球采样、辐亮度估计、保边重建与合成 |
| `ScreenSpaceTrace.hlsl` | 不依赖镜面权重的几何追踪 |

旧 `RayMarchFunction.hlsl` 的各策略直接调用 SSR 专属 `HitProcess`，因此本次把 GI 所需的几何追踪独立成函数库，没有直接复用会乘 `_Smoothness` 的颜色返回接口。该几何接口可供后续 SSR 重构复用。

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

当前 Forward 后处理没有逐像素材质反照率，`receiverAlbedo` 是统一的接收反照率近似，Inspector 色值在线性化后用于合成。不能用已经受光的场景色冒充 albedo，否则会重复乘光照。接入真正的材质反照率/金属遮罩属于后续材质数据集成。

## 参数

统一 SSGI 面板中，下表的 Technical 属于 DiffuseGI Settings；Performance、Intensity、Temporal 与 Artistic 位于统一顶层。独立 DiffuseGI Feature 则把非技术项放在 Controls 中。

| 分组 | 参数 | 默认值 | 含义 |
|---|---|---|---|
| Technical | maxDistance | 5 | 世界空间射线最大距离 |
| Technical | thickness | 0.15 | 线性视深度命中厚度，过大易漏光 |
| Technical | normalBias | 0.03 | 起点沿接收法线的偏移，过大丢近接触 |
| Technical | depthSigma | 0.15 | 滤波几何深度阈值，单位同线性视深度 |
| Performance | performance | Medium | 成本档位，见下表 |
| Performance | temporalBlend | 0.9 | 最大历史权重；0 关闭历史混合 |
| Artistic | intensity | 1 | 合成强度，0 关闭（Debug Off 时） |
| Artistic | distanceFalloff | 0 | 可选距离衰减，0 关闭 |
| Artistic | receiverAlbedo | sRGB 0.8 | 统一接收反照率近似 |
| Debug | debug | Off | Off / Trace / Indirect / Confidence |

| 档位 | 宽高比例 | 像素数比例 | 每像素射线 | 每条步进预算 |
|---|---|---|---|---|
| Low | 1/4 × 1/4 | 1/16 | 4 | 24 |
| Medium | 1/2 × 1/2 | 1/4 | 6 | 48 |
| High | 1/2 × 1/2 | 1/4 | 8 | 64 |

低分辨率尺寸向上取整且不小于 1，过滤步长使用真实低分辨率 RT 尺寸，不依赖 `_ScreenParams`。档位仅为初始预算，未经 GPU 性能基准，不承诺帧耗。

## 输出与局限

- `_GITraceTexture`：本相机低分辨率原始估计；`_GITexture`：全分辨率滤波估计。通过 RenderGraph 的 `SetGlobalTextureAfterPass` 发布，消费者须声明全局纹理读依赖。仅在本效果执行后的同相机同帧有效，不是可跨帧保留的历史纹理；禁用效果或相机被跳过时不得消费残留全局绑定。
- 输入场景色是朝相机方向的出射光近似，含直接光、环境光、可能的已有 GI 和镜面高光；无法恢复真正的漫反射入射光。加法合成可能与烘焙/探针 GI 重复，需要按场景调强度。
- 单层深度看不到屏幕外和遮挡背面。追踪输入是“不透明几何 + 天空盒”（`AfterRenderingSkybox`，见“事件排序”），**不含透明几何**；透明物体对最终画面的影响是“在合成之后混合上来”，而不是进入 gather。旧版本用截屏测出的“隐藏 Water/RainDrops 后 GI>0.10 从 39315 降到 3952”属于这种覆盖造成的取景伪影（见“被撤回的旧结论”）。未命中时仍不注入额外环境光，沿用原场景的环境照明。
- 当前接入公共时域双缓冲，启用时逐帧旋转采样并按运动向量、历史深度拒绝。尚未实现方差钳制；遮挡边界和快速光照变化仍可能有拖影。
- 实际滤波强度有限：GI>0.02 像素上的相对变化约 4.47%，`depthSigma` 从 0.01 调到 2.0 只再改变梯度 1.48e-4。它是保边去噪，不是强平滑。
- 不实现 AO/HBAO、HiZ 加速、Cache-Aware 专利采样或 Compute 迁移。原骨架中的“最高 62% 加速”不能作为此实现性能结论。
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

编译与运行检查结果另见骨架 Phase 3 的验收记录；上表只覆盖可离线对照的数值项，视觉质量需在 Game View 人工确认。
