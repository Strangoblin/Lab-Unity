# SSGI — AO、DiffuseGI 与 SpecularGI 统一管线

入口为 `SSGIFeature.cs`，Renderer 中只需启用一个 SSGI Feature。现有 AO、DiffuseGI、SpecularGI Feature 保留独立运行能力，供对照和回退；同一 Renderer 不应同时启用独立 Feature 与统一 Feature，否则效果会重复合成。

## 帧内顺序

`AfterRenderingSkybox` 获取不透明场景色、深度、法线和运动向量；三个模块都从同一场景色读取，不从其它模块已合成的颜色再次追踪。

```text
Opaque + Skybox
  ├─ AO: Trace → 双边滤波 → 全分辨率保边 Resolve ─┐
  ├─ DiffuseGI: Gather → 两级宽核重建 → 全分辨率 Resolve ├→ 共享时域重投影
  └─ SpecularGI: SSR → Cubemap → 空域 ───────┘
              ↓
        SSGI.Composite（写回 cameraColor 一次）
              ↓
        URP 拷贝 _CameraOpaqueTexture → 透明物体
```

RenderGraph 在统一 Feature 内直接传递三张 `TextureHandle`，合成 Pass 对每张实际启用的纹理声明读依赖。`SSGITemporalFilter` 每相机只保留一对眼深度历史，每个启用的模块各保留一对颜色历史；三路复用同一个 Shader 的运动向量重投影、深度拒绝与失效判定；每路使用独立 Material，避免 RenderGraph 中不同 Blend 参数串用。没有 Cubemap 时环境回退为黑色，不使用 `SampleSH`。

DiffuseGI / SpecularGI 的低分辨率空间阶段共用家族私有 `SSGISpatialFilter.hlsl`：固定 5×5 重尾核、眼深尺度 0.5、法线幂 32。Specular 保留单遍 2/2/1 步幅；Diffuse 三档改为 Fine 步幅 1 → Coarse 步幅 2，后者读取前者输出，两级都以原相机几何作 guide。先连接相邻像素再扩大范围，不是单遍步幅 2 或 mip 金字塔。Diffuse 相较单遍增加一张低分辨率 RT、一次 Raster Pass，空间循环共 50 tap；没有改变采样、步进、时缓或增加方差图，不承诺 GPU 提速。两级配置写入本通道历史签名，旧单遍历史不可沿用。

Diffuse 保留法线有效性/归一化和加权 confidence，Specular 保留原始法线权重/中心 fallback/中心 alpha；各自四点上采样不变，Diffuse 原 `depthSigma` 仍作严格上采样 guide。不共享方向、BRDF、环境回退或合成语义。多尺度可能平滑真实间接光细节，几何边缘和运动画质仍需人工确认，细节见 [DiffuseGI](DiffuseGI/DiffuseGI.md#共享空间重建)。

2026-10-14 两级版本：66 组相机/空间/消费者/历史验收与四个负控制通过；固定当前复制视角、640×360、Medium/DDA、时缓 0.5 的生产 history 帧差 RMS 下降约 22.6%，平均亮度约 +0.29%。该结果仅描述这次静态对照，不是 GPU 提速、运动画质或噪声完全消除的结论。入口与冻结单遍基线说明见 [Validation README](Validation~/README.md)。

DiffuseGI 同日后续采样试验保持两级核与预算，采用块级平移数字网和每条射线自身区间内的径向抖动；只将 Diffuse 历史实现版本升为 `w=3`。固定静止视角、Medium/DDA、时缓 0.5 的采样 A/B/A 中，原始 Trace 帧差 RMS 另降约 14.2%，持久 history 帧差 RMS 另降约 15.4%、时间方差降约 31.9%、均值约 +0.10%；重复基线统计漂移受 0.2% 界限检查。不是 STBN、冻结方向或缩窄半球，也不是已消除抖动或 GPU 提速，不能直接相加前次降幅。详见 [采样说明](DiffuseGI/DiffuseGI.md) 和 [专属验证](Validation~/README.md#sequence-comparison)。

## 时域历史

统一入口在三路空间结果之后调用 `SSGITemporalFilter.Resolve`，将结果直接写入各自历史纹理的另一侧，再写一次全分辨率深度历史。当前档位使用 AO 0.85、DiffuseGI 0.9、SpecularGI 0.5 的最大历史权重；各值可单独调节至 0（关闭历史混合）。相机断帧、尺寸变化、明显矩阵跳变、运动向量缺失或前帧深度不匹配时，本帧直接采用空间结果。模块关闭再开启时也会检查其颜色历史是否仍在正确的双缓冲侧。

AO 和 DiffuseGI 只在有运动向量、公共时域 Shader 可用时推进逐帧采样旋转；独立 Feature 需要把 `SSGITemporal.shader` 指定给自己的 `temporalShader` 才启用历史。SpecularGI 的独立入口同样使用公共时域阶段。历史颜色为 AO 的 RHalf 与两路 GI 的 ARGBHalf，深度为 RHalf；透明几何仍不参与追踪。

GI 的历史另按实际录制的后端、几何参数、采样/分辨率档位、空间滤波参数和 Shader 比较精确配置签名；SpecularGI 包含 Roughness 与环境 Cubemap/mip，独立 DiffuseGI 包含 Distance Falloff。变化只拒绝受影响 GI 通道本帧的旧颜色，不清空或重新分配历史，也不改变共享深度双缓冲。深度描述符与 RTHandle/动态比例变化另行检查；只改最终合成强度、接收反照率或 Temporal Blend 不失效。AO 不加入本轮 GI 签名逻辑。

## 几何追踪后端

DiffuseGI 与 SpecularGI 各自的 Technical Settings 提供 `traceBackend`，统一与独立入口读取同一个设置。默认 `DDA=0`，`Ray=1` 保留对照；旧序列化资源缺少该字段时也使用 DDA。后端为 Trace Pass 局部关键字变体，参数与关键字先存入 PassData，再在执行阶段绑定。

`ScreenSpaceTrace.hlsl` 保留 `SST_Trace` 调用签名，适配 [通用步进库](../../../Special/Rendering/ScreenSpaceTrace/README.md) 的 `ScreenTrace_TraceDDA/TraceRay`；追踪 shader 显式 include RayStep → ScreenTrace 后再 include SST。AO、Temporal、Composite 仍只使用原 SST 深度辅助，不新增追踪依赖。Trace Pass 需 shader target 4.5，本轮只在 Metal 验证，不作为低规格平台回退保证。

| 档位 | Diffuse Trace 宽高比例 | Specular Trace 宽高比例 | Diffuse 射线数 | Diffuse RAY/DDA 样本 | Specular RAY/DDA 样本 |
|---|---:|---:|---:|---:|---:|
| Low | 1/8 | 1/8 | 4 | 24 | 32 |
| Medium | 1/4 | 1/4 | 6 | 48 | 64 |
| High | 1/3 | 1/2 | 8 | 64 | 96 |

2026-10-15 仅再平衡 Diffuse High 的工作网格，以少量细节取舍减少追踪工作量；AO/Specular High 保持 1/2。Diffuse 的射线/步进预算和两级重建不变，不缩小半球或提高时缓；旧 1/2 High 历史通过已有分辨率签名只拒绝 Diffuse。实际耗时与质量代价见 [Timing](Timing.md#diffuse-high-性能再平衡2026-10-15)。

DDA 按旧 SSR 框架预计算屏幕位置、`1/w` 与 `eyeDepth/w` 增量，循环累加，按固定投影样本检测穿越并二分；采样真实有效相机深度，不逐格，也不做解析 slab 求交。近平面/视口裁切后距离仍相对原世界起点，基础采样覆盖完整可见片段，不恢复旧 StepSize 缩短覆盖。厚度为线性眼深单位。RAY 保留原等世界距离样本和 6 次二分。预算数值统一，但位置分布及薄前景漏追风险不同，不保证等画质。

RAY/DDA 完成固定采样无命中返回 `MISS` 或 `LEFT_VIEW`，不再将未覆盖的像素解释为 `BUDGET`；通用 HiZ 的回退/细查仍可能耗尽预算并返回 `BUDGET`。效果层的 Diffuse 零贡献、Specular Cubemap 回退不变，不自动重跑 RAY。诊断字段只供按需使用，不新增调试 RT。DDA 不调用深度金字塔服务；生产 HiZ 尚未接线。退役论文版仅保留 [文档参考](../../../Special/Rendering/ScreenSpaceTrace/README.md#退役方案仅文档参考)，不留并行源码。

## 合成语义

AO 输出可见度 `V`，DiffuseGI 输出尚未乘接收反照率和强度的间接辐亮度近似，SpecularGI 输出空间与时间滤波后的反射辐亮度；屏幕反射低置信度时直接回退到显式 Cubemap，不再计算假平面反射。统一 Shader 按以下顺序合成：

```text
AOFactor = 1 - saturate((1 - V) × AOIntensity)
Color    = Scene × AOFactor
Color   += DiffuseGI × DiffuseIntensity × AOFactor
Color    = lerp(Color, SpecularGI, saturate(SpecularIntensity × Fresnel))
```

AO 固定乘到原场景色和新增 DiffuseGI。Forward 后处理无法拆出场景色中的直接光与环境光，因此这里不是严格的“仅遮蔽间接光”。DiffuseGI 使用白色接收反照率近似；不能把已经受光的场景色当作材质反照率，否则会重复计入光照。相较旧版 0.8 的全局颜色，DiffuseGI 可能变亮，可用统一强度调节。SpecularGI 沿用原有 Fresnel 混合，不把反射辐亮度当成无条件加法项。

## Renderer 配置与调试

初次集成曾在 `Assets/Settings/PC_Renderer.asset` 启用统一 SSGI。2026-10-12 接线核对时 PC Renderer 未包含 SSGI；2026-10-13 性能排查时已配置并启用统一 Feature。两轮验证均未保存或改写场景 / Renderer 配置。使用时在实际相机使用的 Renderer 上绑定统一 Feature，勿与独立 GI Feature 同开。统一 Debug 提供 AO、DiffuseGI、SpecularGI 分量视图，各子模块原有 Debug 只在独立 Feature 使用。

统一 Inspector 的「Technical · Modules」只保留各模块的 Shader、追踪几何与滤波参数；SpecularGI 的 Cubemap 与 Sky Max Mip 也在这里。三个模块的性能档位集中在 Performance，强度集中在 Intensity，历史权重集中在 Temporal。三路 Trace 工作尺寸在 Low 为宽高各 1/8、Medium 各 1/4；High 的 Diffuse 为各 1/3，AO/Specular 仍为各 1/2，非整除尺寸统一向上取整；各路的射线或步进预算仍独立。统一面板不再暴露 Artistic 组：AO/DiffuseGI 的距离衰减固定为 0，Scene AO 固定为 1，接收反照率固定为白色；全局 Specular Roughness 是缺少逐像素材质数据时的近似值，保留在 SpecularGI 技术设置中。每路 Intensity = 0 即跳过该路追踪和历史解析，无额外启用开关。Debug 是统一合成的分量视图。独立 AO、DiffuseGI、SpecularGI Feature 各自保留 Settings（技术参数）和 Controls（性能、强度、时缓、艺术与独立调试），供单模块对照。\n\n性能档位决定固定采样预算和工作分辨率；AO 世界半径只控制遮蔽覆盖，不改变循环次数。

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
