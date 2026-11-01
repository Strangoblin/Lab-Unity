# SSGI — AO、DiffuseGI 与 SpecularGI 统一管线

入口为 `SSGIFeature.cs`，Renderer 中只需启用一个 SSGI Feature。现有 AO、DiffuseGI、SpecularGI Feature 保留独立运行能力，供对照和回退；同一 Renderer 不应同时启用独立 Feature 与统一 Feature，否则效果会重复合成。

## 帧内顺序

整个 SSGI 是一个入队 Pass，事件由总面板 `Event → passEvent` 控制（默认 `BeforeRenderingPostProcessing` 550）。三个模块读同一事件处的场景色、深度、法线和运动向量，不从其它模块已合成的颜色再次追踪。事件早于透明深度合并的写回就看到未合并的 `_CameraDepthTexture`（只有不透明物），晚于则看到含透明面的版本；默认 550 配合 `TransparentDepthMergeFeature` 在 500 的合并，三个模块都能作用到透明表面上。

```text
Opaque + Skybox
  ├─ AO: Trace → 双边滤波 → 全分辨率保边 Resolve ─┐
  ├─ DiffuseGI: Gather → 两级宽核重建 → 全分辨率 Resolve ├→ 共享时域重投影
  └─ SpecularGI: SSR → Cubemap → 空域 ───────┘
              ↓
        SSGI.Composite（写回 cameraColor 一次）
              ↓
        URP 后处理
```

曾经试过按模块拆成各自带事件的 Pass（AO/DiffuseGI 读未合并深度、SpecularGI 读合并后深度）：可行，但三份模块无法共用一份时域滤波——帧序号与深度历史会在一帧内推进多次，重投影直接错位；拆开后每帧最多写三份全分辨率深度历史，HiZ 金字塔也要按事件分版本重建。收益不足以抵消，因此保留单 Pass + 一个总事件。

RenderGraph 在统一 Feature 内直接传递三张 `TextureHandle`，合成 Pass 对每张实际启用的纹理声明读依赖。`SSGITemporalFilter` 每相机只保留一对眼深度历史，每个启用的模块各保留一对颜色历史；三路复用同一个 Shader 的运动向量重投影、深度拒绝与失效判定；每路使用独立 Material，避免 RenderGraph 中不同 Blend 参数串用。Specular 默认采样 Unity 环境反射图并按 URP 解码 HDR，不再配置自定义 Cubemap，也不使用 `SampleSH`。

DiffuseGI / SpecularGI 的低分辨率空间阶段共用家族私有 `SSGISpatialFilter.hlsl`：固定 5×5 重尾核、眼深尺度 0.5、法线幂 32。Specular 保留单遍 2/2/1 步幅；Diffuse 三档改为 Fine 步幅 1 → Coarse 步幅 2，后者读取前者输出，两级都以原相机几何作 guide。先连接相邻像素再扩大范围，不是单遍步幅 2 或 mip 金字塔。Diffuse 相较单遍增加一张低分辨率 RT、一次 Raster Pass，空间循环共 50 tap；没有改变采样、步进、时缓或增加方差图，不承诺 GPU 提速。两级配置写入本通道历史签名，旧单遍历史不可沿用。

Diffuse 保留法线有效性/归一化和加权 confidence，Specular 保留原始法线权重/中心 fallback/中心 alpha；各自四点上采样不变，Diffuse 原 `depthSigma` 仍作严格上采样 guide。不共享方向、BRDF、环境回退或合成语义。多尺度可能平滑真实间接光细节，几何边缘和运动画质仍需人工确认，细节见 [DiffuseGI](DiffuseGI/DiffuseGI.md#共享空间重建)。

2026-10-14 两级版本：66 组相机/空间/消费者/历史验收与四个负控制通过；固定当前复制视角、640×360、Medium/DDA、时缓 0.5 的生产 history 帧差 RMS 下降约 22.6%，平均亮度约 +0.29%。该结果仅描述这次静态对照，不是 GPU 提速、运动画质或噪声完全消除的结论。入口与冻结单遍基线说明见 [Validation README](Validation~/README.md)。

DiffuseGI 同日后续采样试验保持两级核与预算，采用块级平移数字网和每条射线自身区间内的径向抖动；只将 Diffuse 历史实现版本升为 `w=3`。固定静止视角、Medium/DDA、时缓 0.5 的采样 A/B/A 中，原始 Trace 帧差 RMS 另降约 14.2%，持久 history 帧差 RMS 另降约 15.4%、时间方差降约 31.9%、均值约 +0.10%；重复基线统计漂移受 0.2% 界限检查。不是 STBN、冻结方向或缩窄半球，也不是已消除抖动或 GPU 提速，不能直接相加前次降幅。详见 [采样说明](DiffuseGI/DiffuseGI.md) 和 [专属验证](Validation~/README.md#sequence-comparison)。

## 时域历史

统一入口在三路空间结果之后调用 `SSGITemporalFilter.Resolve`，将结果直接写入各自历史纹理的另一侧，再写一次全分辨率深度历史。当前档位使用 AO 0.85、DiffuseGI 0.9、SpecularGI 0.5 的最大历史权重；各值可单独调节至 0（关闭历史混合）。相机断帧、尺寸变化、明显矩阵跳变、运动向量缺失或前帧深度不匹配时，本帧直接采用空间结果。模块关闭再开启时也会检查其颜色历史是否仍在正确的双缓冲侧。

采样帧计数与历史双缓冲计数分离；公共时域 Shader 可用且产生信号时，即使权重为 0 或运动向量不可用也推进采样序列。独立 Feature 需要指定 `SSGITemporal.shader` 才启用公共时域阶段。某路权重为 0 时直接使用空间结果，跳过该路历史写入并使旧历史失效；若所有路均为 0，也跳过共享深度历史写入。已有纹理不会因零权重释放，重新启用首帧拒绝历史，次帧在条件满足时恢复。三路强度均为 0 时统一 Pass 不入队，并使该相机历史失效；不改变其他 Feature 的输入请求。历史颜色为 AO 的 RHalf 与两路 GI 的 ARGBHalf，深度为 RHalf；透明几何本身仍不参与追踪（只有读了合并深度的模块会看到透明面）。

各路历史按实际参数和深度布局比较配置签名。AO 包含模式、有效半径、偏移、角度、深度滤波、采样档位和独立模式的距离衰减；GI 包含后端、几何参数、采样档位和空间滤波。SpecularGI 包含 Roughness、默认环境纹理身份、更新计数和 HDR 解码参数，独立 DiffuseGI 包含 Distance Falloff。签名变化只拒绝受影响通道本帧的旧颜色，不清空或重新分配历史，也不改变共享深度双缓冲。Unity 纹理更新计数不保证覆盖所有原地 GPU 更新。深度描述符与 RTHandle/动态比例变化逐通道检查；只改最终合成强度、接收反照率或非零 Temporal Blend 不失效。

## 几何追踪后端

统一 SSGI 面板的 `Trace` 提供 `DDA`、`Ray`、`HiZ` 三种模式，同时控制 DiffuseGI 与 SpecularGI；默认 DDA。独立 DiffuseGI / SpecularGI Feature 在各自顶层提供同样的 Trace 设置。射线长度、厚度与偏移仍由各分量分别配置。后端为 Trace Pass 局部关键字变体，参数与关键字先存入 PassData，再在执行阶段绑定。

旧统一配置首次初始化时以 Diffuse 的旧后端、HiZ 开关和金字塔资源为准，合并为新的统一 Trace；若旧的 Diffuse 与 Specular 选择不同，Specular 会同步到 Diffuse 的旧选择。独立 Feature 分别迁移各自的旧设置，初始化后的新选择不再被旧字段覆盖。

`ScreenSpaceTrace.hlsl` 保留 `SST_Trace` 调用签名，适配 [通用步进库](../../../Special/Renderer/ScreenSpaceTrace/README.md) 的 `ScreenTrace_TraceDDA/TraceRay`；追踪 shader 显式 include RayStep → ScreenTrace 后再 include SST。Diffuse 在多射线循环外准备共享追踪上下文和配置，单射线入口仍兼容 `SST_Trace`；公共库仍逐射线验证输入。AO、Temporal、Composite 仍只使用原 SST 深度辅助，不新增追踪依赖。Trace Pass 需 shader target 4.5，本轮只在 Metal 验证，不作为低规格平台回退保证。

| 档位 | Diffuse Trace 宽高比例 | Specular Trace 宽高比例 | Diffuse 射线数 | Diffuse RAY/DDA 样本 | Specular RAY/DDA 样本 |
|---|---:|---:|---:|---:|---:|
| Low | 1/8 | 1/8 | 4 | 24 | 32 |
| Medium | 1/4 | 1/4 | 6 | 48 | 64 |
| High | 1/3 | 1/2 | 8 | 64 | 96 |

2026-10-15 仅再平衡 Diffuse High 的工作网格，以少量细节取舍减少追踪工作量；AO/Specular High 保持 1/2。Diffuse 的射线/步进预算和两级重建不变，不缩小半球或提高时缓；旧 1/2 High 历史通过已有分辨率签名只拒绝 Diffuse。实际耗时与质量代价见 [Timing](Timing.md#diffuse-high-性能再平衡2026-10-15)。

DDA 按旧 SSR 框架预计算屏幕位置、`1/w` 与 `eyeDepth/w` 增量，循环累加，按固定投影样本检测穿越并二分；采样真实有效相机深度，不逐格，也不做解析 slab 求交。近平面/视口裁切后距离仍相对原世界起点，基础采样覆盖完整可见片段，不恢复旧 StepSize 缩短覆盖。厚度为线性眼深单位。RAY 保留原等世界距离样本和 6 次二分。预算数值统一，但位置分布及薄前景漏追风险不同，不保证等画质。

RAY/DDA 完成固定采样无命中返回 `MISS` 或 `LEFT_VIEW`，不再将未覆盖的像素解释为 `BUDGET`；HiZ 的回退/细查仍可能耗尽预算并返回 `BUDGET`。效果层的 Diffuse 零贡献、Specular 环境反射回退不变，不自动重跑 RAY。诊断字段只供按需使用，不新增调试 RT。DDA 不调用深度金字塔服务；HiZ 为实验性模式。退役论文版仅保留 [文档参考](../../../Special/Renderer/ScreenSpaceTrace/README.md#退役方案仅文档参考)，不留并行源码。

使用 `HiZ` 时，需在同一个 Trace 面板将 `Assets/Mine/Special/Renderer/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute` 指定到 `Depth Pyramid Shader`。统一入口的两路同帧共享一份正眼深金字塔；独立 Specular 在透明物之后执行，使用单独的深度阶段标识。缺资源、部分视口、动态尺寸、XR 数组或未 resolve 的 MSAA 等不支持条件回退 DDA，并发出一次警告；Shader 另外核对实际深度尺寸和 UV 比例，不匹配时使用 DDA。统一模式切换会分别失效受影响的 Diffuse 与 Specular 历史，不影响 AO。实验性的 HiZ 现以屏幕像素步进（mip `m` 前进 `2^m` 像素，回退时细分到 mip 0），仍可能漏过薄几何；迭代预算为 `2 × maxSteps`，不保证与 DDA 等画质或更快。

## 合成语义

AO 输出可见度 `V`，DiffuseGI 输出尚未乘接收反照率和强度的间接辐亮度近似，SpecularGI 输出空间与时间滤波后的反射辐亮度；屏幕反射低置信度时回退到 Unity 默认环境反射图，不再计算假平面反射。统一 Shader 按以下顺序合成：

```text
AOFactor = 1 - saturate((1 - V) × AOIntensity)
Color    = Scene × AOFactor
Color   += DiffuseGI × DiffuseIntensity × AOFactor
Color    = lerp(Color, SpecularGI, saturate(SpecularIntensity × Fresnel))
```

AO 固定乘到原场景色和新增 DiffuseGI。Forward 后处理无法拆出场景色中的直接光与环境光，因此这里不是严格的“仅遮蔽间接光”。DiffuseGI 使用白色接收反照率近似；不能把已经受光的场景色当作材质反照率，否则会重复计入光照。相较旧版 0.8 的全局颜色，DiffuseGI 可能变亮，可用统一强度调节。SpecularGI 沿用原有 Fresnel 混合，不把反射辐亮度当成无条件加法项。

统一和独立入口的 AO、Diffuse、Specular 强度范围均为 0–10，运行时也按该范围钳制。AO 遮蔽量和 Specular 的 `Intensity × Fresnel` 混合权重仍饱和到 1；Diffuse 保持线性加法。强度 1 的既有合成公式不变。

## Renderer 配置与调试

初次集成曾在 `Assets/Settings/PC_Renderer.asset` 启用统一 SSGI。2026-10-12 接线核对时 PC Renderer 未包含 SSGI；2026-10-13 性能排查时已配置并启用统一 Feature。两轮验证均未保存或改写场景 / Renderer 配置。使用时在实际相机使用的 Renderer 上绑定统一 Feature，勿与独立 GI Feature 同开。统一 Debug 提供 AO、DiffuseGI、SpecularGI 分量视图，各子模块原有 Debug 只在独立 Feature 使用。

统一 Inspector 的「Technical · Modules」只保留各模块的 Shader、追踪几何与滤波参数；Specular 天空使用 Unity 默认环境反射图。三个模块的性能档位集中在 Performance，强度集中在 Intensity，历史权重集中在 Temporal。三路 Trace 工作尺寸在 Low 为宽高各 1/8、Medium 各 1/4；High 的 Diffuse 为各 1/3，AO/Specular 仍为各 1/2，非整除尺寸统一向上取整；各路的射线或步进预算仍独立。统一面板不再暴露 Artistic 组：AO/DiffuseGI 的距离衰减固定为 0，Scene AO 固定为 1，接收反照率固定为白色；全局 Specular Roughness 是缺少逐像素材质数据时的近似值，保留在 SpecularGI 技术设置中。每路 Intensity = 0 即跳过该路追踪和历史解析，无额外启用开关。Debug 是统一合成的分量视图。独立 AO、DiffuseGI、SpecularGI Feature 各自保留 Settings（技术参数）和 Controls（性能、强度、时缓、艺术与独立调试），供单模块对照。\n\n性能档位决定固定采样预算和工作分辨率；AO 世界半径只控制遮蔽覆盖，不改变循环次数。

## 验证与限制

2026-09-28 初次集成在 SampleScene 的 Main Camera 上，以同一个 256×144 离屏目标逐项切换模块，较全关基线的采样像素差异为 AO 1208、DiffuseGI 146、SpecularGI 1778、全开 1808；四个 Shader 强制导入均为 0 消息，Play Mode 运行无错误日志。该测试证明三路已接入真实管线，不代替 Game View 的主观画质检查。

公共时域重构后四个 Shader 强制导入均为 0 消息，Play Mode 连续运行时相机历史推进到第 3 帧，三路颜色历史均已写入，且无错误日志。此检查不代替 Game View 的闪烁、拖影和快速镜头运动观测。SpecularGI 的追踪/空间阶段仍使用 Unsafe Pass；后续先用 Profiler 衡量中间 RT 与该 Pass 的开销。透明物体在合成后绘制，因此不会作为屏幕追踪的命中几何。

### 2026-10-12 DDA 接线验证

以下为退役逐格实现的历史验收记录，不代表当前增量 DDA 的完整几何覆盖合同；当前验证入口已改为固定样本/穿越与历史接线验证。

Unity 6000.3.14f1 / URP 17.3.0 / Metal，实际 Game camera 的 `SingleCameraRequest` 与 RenderGraph：**39 组 / 1,152,437 断言通过**。覆盖 189×101、321×179、97×61 的透视/正交深度网格、首命中 UV/世界距离、near/离屏起点裁切、薄前景与稀疏 RAY 控制、显式 BUDGET；统一/独立入口 × 两后端 × 三档与有效 uniform/关键字；逐 GI 历史失效、无关参数不失效、无重分配、双相机与尺寸失效；验证恢复原 Renderer 实例、Feature 引用与 Active。

冻结的接线前 RAY shader/追踪源码作基线：Diffuse 三档最大 RGB 差异不超过 `3.05175781e-5`，Specular 为 0；统一/独立信号及 AO 后端隔离差异均为 0。两个负控制分别给 GPU 距离加 0.5、伪造旧历史签名允许错误后端历史，检查均确实失败并被 runner 识别。640×360 两路/两后端 PNG 是空间信号显示对照，不是 GPU 性能或运动稳定性证据。

在项目根、Editor/bridge 连接、SSGI 工作区已授权时复跑：

```bash
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run.sh
```

源码归 `Validation~/`，runner 临时导入并经门禁清理自己创建的 `RuntimeFixture` 和 metas。Unity CLI 的成功退出码不足以判定脚本成功，runner 另检查 JSON 响应与实际 `results.json`，拒绝空/不完整矩阵。运行输出在 `tmp/sessions/`。

未验证：动态分辨率下非单位有效区、局部 camera.rect、XR/其他图形 API、连续运动/快速镜头的闪烁拖影、代表真实场景画质及 GPU 中位数/P95。独立 DiffuseGI 沿用原输入请求，历史仍以 Renderer 实际提供运动向量为前提。本轮不宣称 DDA 比 RAY 更快，不改采样、BRDF、朝向/边缘置信度、空域与最终合成算法。

### 2026-10-13 DDA 卡顿与同步计时

历史逐格实现：SampleScene / Main Camera / PC_Renderer，Medium AO + 双 GI，在 M2 / Metal 上重现整次渲染延迟增加。两次复跑，最终 36 块 / 432 样本：642×366 双 RAY 3.644 ms、双 DDA256 6.395 ms；1920×1080 为 9.358 / 22.273 ms，主要增量来自 DiffuseGI。该版本已退役，以下数字不作为当前增量 DDA 性能结论。

这些是包含 CPU/驱动/等待/管线/回读的 GPU 同步墙钟，不是纯 GPU 或 Trace Pass 时间；预算敏感性控制不代表等画质优化完成。本轮生产算法和预算保持，原 39 组与两个负控制复跑通过。全部对照、中位数/P95、限制和可复现命令见 [Timing.md](Timing.md)。

### 2026-10-13 旧 SSR 框架重构验收

按用户决定原地替换公共 DDA / HiZ：预计算齐次增量、循环累加、穿越后二分与 mip 步幅回退；不再保留逐格/slab 源码与几何全覆盖 oracle，仅留文档参考。保留 RAY 基线、安全裁切、世界距离及着色分离。DDA / RAY 档位预算统一为 Diffuse 24/48/64、Specular 32/64/96；DDA 完整采样片段后无命中返回 MISS/LEFT_VIEW，薄前景可漏追。SSGI 仍默认 DDA，不接生产 HiZ，也不改 AO、方向采样、BRDF、空间/时域滤波和合成。

Metal 实际验证：公共库 **39 组 / 89,095 断言**、SSGI 相机/入口/历史 **39 组 / 1,152,441 断言**通过；库距离、SSGI 距离与错误后端历史三个负控制确实被识别。冻结 RAY 生产回归：Diffuse 三档最大 RGB 差异分别为 0、`1.52587891e-5`、`3.05175781e-5`，Specular 三档为 0；统一/独立信号及 AO 后端隔离差异均为 0。这是当前验证场景与容差内的回归，不是 XR/动态有效区位精确保证。编译无错误；冻结基线与其它已有代码仍有警告，不宣称全项目零消息。

原 Renderer 此时内存状态是 MaskRBR 开启、SSGI 关闭。用户批准只用一次性 SSGI-only 克隆计时，原场景、Renderer、Feature Active 保持；24 块 / 288 样本，1920×1080 双 RAY 中位数 **9.290 ms**、双 DDA **9.190 ms**，波动重叠，不宣称显著提速。它不是当前 Game View 配置的性能，也不是退役算法的同配置等画质对比；方法、完整表格和显式入口见 [Timing.md](Timing.md)。连续运动/薄几何画质仍待人工确认。

### 2026-10-30 HiZ 像素步幅

旧 HiZ 把 DDA 单位（`L/maxSteps` 像素）当作 1 像素，再乘以 `2^mip`，实际每步只覆盖 `maxSteps/L` 的路径，mip 越大漏得越多。本轮按用户要求直接把步幅改为屏幕像素：mip `m` 前进 `2^m` 像素，粗层命中时记录 `parentEnd` 细分到 mip 0，预算改为 `2 × maxSteps`，零投影回退 DDA。公共库 README 与三个模块文档同步；采样、BRDF、空间/时域滤波和默认 DDA 不变。

Unity 6000.3.14f1 / Metal：公共库 **39 组 / 88,826 断言**通过，GPU DDA 距离注入 0.5 的负控制被识别；SSGI 相机/入口/历史 **92 组 / 1,153,512 断言**通过，5 个负控制（DDA 距离、错误后端历史、空间参考、多尺度参考、默认环境 HDR）均被识别。CPU 参考模型同步为「弦像素路径 + `parentEnd` 细分」，因此这组结果证明 GPU 与新参考一致、预算有界，不证明画面漏打减少；画质与薄几何覆盖仍需在 Game View 人工确认。
