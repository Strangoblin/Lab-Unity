# Unity Developer — Memory Index

> 按日期命名的 memory 文件索引。新 memory 以 `YYYY-MM-DD-<slug>.md` 格式添加。

---

## 技术栈（不变式）

- Unity 6 + URP 17+，macOS Metal
- 自动化：unityctl（Editor 远程控制）、Roslyn（C# 运行时注入）
- UnityCtl 宿主边界：bridge 与 Editor 必须处于同一执行环境；沙盒 bridge 与真实宿主 bridge/Editor 不互通，以 `Unity connected` + `unityctl wait` 为连接证据
- 清理/整理回退偏好：只使用 Git（精确 pathspec 的 stash/历史），不再生成压缩备份包；永久禁止 `git stash --all`

## 关键架构决策

- **GizmoRender 统一 Gizmo 框架（2026-10-16 已归档）**：Source → 成员键/显隐分派 → Canvas/Tint，配置归成员集合，窗口只编辑与扫描；正式目录 `Assets/Mine/Scripts/GizmoRender`，菜单 `Tools/GizmoRender`。统一键校验/唯一性、清单失效清理、嵌套与异常状态恢复；六组件接入，GUID 保留，编译与 67 项 Editor 回归通过。GrassGenerator 与交互 Handles 未迁入。见 [归档](2026-10-16-gizmo-render-handover.md)。

- **生产类工具统一标准（2026-10-16 归属修正）**：通用完整窗口模板归 agent skill，五个 Generator / Baker 窗口独立继承 `EditorWindow`，在本工具内持有固定 Save=Load 路径、动作与所有权；项目 `ProductionTools` 目录已迁出。保留 `CopySerialized` 原位保存与 `SaveAssetIfDirty`，不依赖 agent 目录参与 Unity 编译。见 [当前裁决](../meta/2026-10-16-production-tools-agent-templates.md)。

- **SSGI 档位更新（2026-10-15）**：Diffuse High 的 Trace 宽高改为各 1/3，仍 8 射线/64 步；AO/Specular High 仍 1/2，三路 Low/Medium 仍 1/8、1/4。下方“统一 High 1/2”是集成时的历史状态，不再作为当前预算。Diffuse 采样历史实现签名现为 w=3。

- **空间滤波的质量档与半径正交**：Blur、SNN、Kuwahara、双边滤波和屏幕空间 Resolve 中，Quality 固定样本数/分布/下采样率，Radius 只缩放固定 offset、改变感受野，禁止进入循环边界。默认档必须复现重构前预算与画面；统一规范和模板见 [spatial-filter-budget.md](../../knowledge/unity/shader/postprocess/spatial-filter-budget.md)。

- **SSGI 家族统一入口（2026-09-28 落地）**：`SSGIFeature` 同帧调度 AO/DiffuseGI/SpecularGI，三路共用 `SSGITemporalFilter` 公共时域（每路独立 Material，避免 Blend 串用）与 `SSGIComposite` 单次合成；合成语义 `Scene×AOFactor + DiffuseGI×Intensity×AOFactor → lerp(SpecularGI, Fresnel)`，AO 只乘一次。三路 Trace 尺寸统一 Low 1/8、Medium 1/4、High 1/2；统一面板不暴露 Artistic 组；`Intensity=0` 即跳过该路。三个独立 Feature 保留供对照，不与统一 Feature 同开。**收档清理**：两份设计骨架已删（`SSGI_Content_Skeleton.md` / `Screen_Render_Architecture_Skeleton.md`），`PC_Renderer.asset` 只剩 `[DebugOutput, SSGI]` 两个 Feature（旧 SpecularGIFeature 子资产已移除）。见 [2026-09-28-ssgi-integration-and-archive.md](2026-09-28-ssgi-integration-and-archive.md)。

- SpecularGI 是分层回退链而非平级模式：镜面/GGX 采样 → SSR 几何首命中 → SSPR 远景候选 → Cubemap 环境回退 → 5×5 空间重建 → 时域累积。`SampleSH` 不作为镜面天空；Procedural Skybox 没有可直接提取的 Cubemap，必须显式绑定或另做捕获。单射线 SSSR 的稳定性依赖低分辨率 trace、足够空间样本和连续历史，不能按随机来源硬拒绝或对 1 spp 结果做紧邻域颜色钳制。见 [2026-09-21-speculargi-unified-fallback.md](2026-09-21-speculargi-unified-fallback.md)。

- Shader 参数端点策略：统一在参数入口钳制合法范围，避免逐计算追加冗余保护；尚未统一实施时明确记录待办。见 [2026-09-18-parameter-range-policy.md](2026-09-18-parameter-range-policy.md)。

- 效果参数分三层：**Technical**（几何/投影/深度偏移——为正确性而调，连续可调）、**Performance**（纯成本、不需要微调——**划档位**，枚举）、**Artistic**（一个参数对应一个观感维度，开关类参数一律「0 = 关闭」而非独立 bool + 强度）。第三层的判据是「成本占比 + 是否需要微调」两个正交问题，不是「属于技术还是美术」。实现细节内化为 shader 常量，不进任何一组。见 [2026-09-20-parameter-layering.md](2026-09-20-parameter-layering.md)。

- **半接线参数**（跨效果通用缺陷形态）：参数在一侧接线、另一侧是硬编码字面量。默认值下两侧数值恰好一致，故长期不可见；参数一旦升格为档位即变必现。**诊断要 grep 数值字面量，不要 grep 参数名**。见 [2026-09-20-parameter-layering.md](2026-09-20-parameter-layering.md)。

- **RenderPassEvent 与同事件内的先后**：`RenderPassEvent` 只分事件，**同事件内自定义 Feature 一定先于 URP 内置 Pass 执行**（插入点决定：`UniversalRendererRenderGraph.cs` 的 `RecordCustomRenderGraphPasses(renderGraph, event)` 调用在每个事件的内置 Pass 之前；自定义 Feature 之间按 `rendererFeatures` 数组顺序）。所以「同事件顺序不可控」的说法不成立，**要核对的是内置 Pass 与目标资源的先后**：例如想让结果进入 `_CameraOpaqueTexture`，必须选 `AfterRenderingSkybox`（早于 `m_CopyColorPass`）而不是 `BeforeRenderingTransparents`，否则透明物体折射到旧拷贝、把效果盖掉。**判读教训**：做「开/关」减法对照前先确认两侧的管线路径一致；把参数从 0 抬到 >0 往往同时把 Feature 从「整体跳过」切换为「启用」，测到的可能是 prepass / 中间色 RT 的路径差异，而不是效果本身。见 [2026-09-21-diffusegi-ssgi-phase3.md](2026-09-21-diffusegi-ssgi-phase3.md)。

- **全局纹理（`SetGlobalTexture*`）的生命周期与回读**：RenderGraph 下发布的是托管纹理，帧末归还池，**跨帧回读只能读到内置 `UnityBlack`（4×4、`R8G8B8A8_SRGB`、全 0）**——这不是「效果没有输出」的证据。判定效果只能在同帧内（Debug 视图）或走离屏相机对照。另有两条同源陷阱：**Debug 直出的灰度会经过场景 Volume 后处理被改写**（色彩分级把上限压到 209/255；`Tonemapping.mode = None` 也不算关，必须整项关闭探针相机的 `renderPostProcessing`）；**`AssetDatabase.SaveAssets()` 会把整个会话内存态写回 `RendererData` 资产**，除新增条目外既有 Feature 的 `m_Active` 也可能被顺带改写，收尾必须 `git diff Assets/Settings/` 核对。见 [2026-09-21-ssgi-phase4-ao.md](2026-09-21-ssgi-phase4-ao.md)。

- PCSS 阴影：PSSM split（级联数随性能档 2/3/4）→ tiled atlas → blocker search → penumbra → variable PCF。**阴影尚未接入光照合成**——只在特定物体 shader 里挂了 `CustomShadowCaster` 投射；`showShadowMap` 是调试直出。后续才用 PCSS 管线取代 Unity 阴影
- POSS 逐物体软阴影：Shadow Atlas Tile Grid → Compute 屏幕空间解算 → PCF 软边缘，与 CSM 共存
- 交互系统：Manager/Processor 分离架构，RT 管理下放，正交相机 CustomRenderer 深度比较输入
- Shader 组织：`Assets/Mine/Shaders/` 按效果分层
- 后处理：Unity 6 Blitter API（`_BlitTexture`，非 `_MainTex`）
- 知识库分类：按实际职责区分 `standard/`、`script/`、`shader/postprocess/`、`shader/render/`、`shader/hlsl/`（`particle/` 占位）；特征族目录可混合 `.cs/.shader/.compute/.hlsl`，但只有 `README.md` 是 markdown；`shader/hlsl/` 对应 `Assets/Mine/Special/HLSL` 共享库，单效果私有库与其 shader 同目录

---

## 活跃上下文

- CelToon 资源与 SSC 合并（2026-10-17）：SSC 保留 Lab URP 17 RenderGraph，合入 CelToon 的 HG 相函数与高度环境光作为可选散射路径（`_ScatteringWeight` 默认 0，旧观感不变），新增 middle/threshold/phaseG/ambientIntensity；Unity batchmode 编译零消息。该 Feature 尚未挂到 Renderer，无画面验证。见 [SSC 合并记录](2026-10-17-ssc-celtoon-scattering-merge.md)、[资源审计](2026-10-17-celtoon-asset-audit.md)。

- 生产工具归属（2026-10-16 当前）：通用框架已归 agent 完整模板，五窗口独立化并编译通过，42 项 Editor 回归及完整模板编译/Bake/释放验证通过。Noise 上轮修复保持；物理点击、不同资产覆盖确认与 GPU 画质仍待验。见 [迁移记录](../meta/2026-10-16-production-tools-agent-templates.md)；[前一轮接手](2026-10-16-production-tool-handover.md) 保持历史语义。

- DiffuseGI性能（2026-10-15）：仅High `(2,8,64)→(3,8,64)`，追踪像素约少55.6%，Low/Medium与AO/Specular不改。正式首轮计时嘈杂，有界复测1080p整次同步墙钟三轮下降约22–33%，非纯GPU/固定收益。静止0.5 history RMS约+7.1%/方差+12.8%，细节有取舍、地面抖动未解决；68组/四负控/旧HalfHigh历史拒绝通过，原配置保持，未提交推送。见 [记录](2026-10-15-diffuse-high-performance.md)。

- 三库收敛（2026-10-14）：记忆独立memory/unity，事实归knowledge/unity，流程/工具归skills；平台先读agents清单直接取用，84架构软链已全部移除。历史正文保留原字节，新路由见 [体系记录](../meta/2026-10-14-library-consolidation.md)。

- Agent / Skill 分工（2026-10-14）：角色只索引、取用与记录；模板/CLI/脚本/流程归 skill，51 个旧入口已移除；实际资产位置通过项目资源 ID 获取，授权独立。取像与测评流程归对应 skill，事实 references 继续保留，详见 [体系记录](../meta/2026-10-14-agent-skill-boundaries.md)。

- DiffuseGI序列（2026-10-14，补归档）：块级平移Sobol数字网、径向分层内抖动，完整cosine半球/预算保持，历史签名w=3。Metal 67组与四负控通过；固定Medium/DDA/0.5视角的Trace/history帧差RMS分别降14.2%/15.4%，不是全场景结论。人工观察墙壁良好、地面仍明显抖动；用户随后允许细节取舍优先性能。见 [记录](2026-10-14-diffuse-sampling-sequence.md)。
- DiffuseGI多尺度（2026-10-14历史）：三档固定Fine步幅1→Coarse步幅2，共用原25tap核心/Shader Pass1，新增一张低分辨率RT和一次Pass；该阶段采样、步进、时域及Specular不改，签名当时w=2（后续序列已为w=3）。66组/1,152,803断言、四个GPU/历史负控制通过；固定复制视角640×360、Medium/DDA、时缓0.5，真实history帧差RMS降22.6%、平均亮度+0.29%，不能与序列收益直接相加。Specular回归差0；不是GPU提速或全场景/运动画质结论。见 [记录](2026-10-14-diffuse-multiscale-reconstruction.md)。
- SSGI单遍空间重建（2026-10-13历史）：已按用户批准向Specular靠近，新增家族共享5x5核心，Diffuse单遍宽核替代H/V，少一张RT/一遍Pass；为避免奇偶子网格，Diffuse步幅1.5/1.5/1，Specular仍2/2/1且三档冻结RGB回归差0。49组/1,152,535断言、距离/历史/空间三个负控制通过，预算/估计量/时域不改；depthSigma只管原严格上采样。初版固定镜头降约15%不是最终候选数字，最终采集时用户已移动镜头并调到时缓0.5，保持这些改动；真实静止/运动画质待确认，不宣称完全去噪/GPU提速。见 [实施记录](2026-10-13-ssgi-shared-spatial-reconstruction.md)。
- DiffuseGI 弱时缓诊断阶段（2026-10-13历史）：静止镜头/时缓0.5也明显，8组回读中RAY/DDA帧差几乎相同、冻结采样降逾99.9%、共享时域有效；单调depthSigma仅改善9.3%，由此提出宽核候选。该候选已完成并升级为两级重建，当前状态见2026-10-14条目；见 [诊断记录](2026-10-13-diffuse-weak-temporal-diagnosis.md)。
- 共享屏幕遮罩（2026-10-13）：新增 Special/HLSL/MaskFunction 与 Special/Rendering/Mask/ScreenMaskRenderer；双策略共用 R8／RendererList 和 raw-depth 可见性。22 项旧新版全图对照最大误差 0，47 GPU／32 URP 回归通过；原参数／GUID／预算保持，Stencil／颜色来源待扩展。见 [记录](2026-10-13-shared-screen-mask.md)。
- 通用射线库 / SSGI 主线（2026-10-13）：用户批准按旧 SSR 增量框架重构 DDA/HiZ，论文逐格/slab 仅留文档；RAY 基线、安全裁切和几何/着色分离保留。默认 DDA，Diffuse 24/48/64、Specular 32/64/96 与 RAY 对齐；不保证薄前景/完整像素覆盖，SSGI 暂不接 HiZ。Metal 库 39 组 / 89,095 断言、相机 39 组 / 1,152,441 断言、三个 GPU/历史负控制及 21 个 report 检查控制通过。计时时Live为MaskRBR on / SSGI off，用户批准临时SSGI-only克隆；1080p双RAY/DDA中位9.290/9.190 ms，波动重叠、不宣称显著提速/等画质，非Game View性能。用户随后确认当前场景DDA目测无误，弱时缓Diffuse抖动另查；HiZ/SSSM、XR/动态有效区、运动及其它平台GPU收益仍待验。原场景/Renderer/采样/BRDF/滤波/合成未改，未提交推送。见 [重构记录](2026-10-13-ssr-framework-rebuild.md)。
- SSGI 前阶段（历史）：[2026-10-12 接线](2026-10-12-ssgi-dda-integration.md) 和 [库阶段](2026-10-12-screen-trace-library.md) 描述已退役逐格合同/预算；[2026-10-13 卡顿排查](2026-10-13-ssgi-dda-wall-timing.md) 的 1080p 9.358/22.273 ms 是当时 DDA256 配置历史证据，不作为当前算法的性能结论或同配置等画质对照。
- MaskRBR（2026-10-12）：v2 仅主方向光，取消 Manual／场景 HDR 提取；归一化径向遮罩 × 峰值归一 HG／角窗 × 主光颜色／光强 × intensity。默认半角 60°／g=0.7；角度响应在最终 Composite 执行，Low 窗外仍为零。主 Shader 三 Pass、Rays 单通道 R16；47/47 GPU、32/32 真实隔离 URP 通过。保留现有 Renderer 内存接入状态；透明源／裁剪材质／接收面遮挡待扩展。见 [当前记录](2026-10-12-maskrbr-mainlight-phase.md)，[v1 历史](2026-10-12-maskrbr-v1.md)。
- 通用 Debug 区（2026-10-12）：已按用户指示删除 InteriorMappingScreenDebug.shader / meta 专用验证；通用工具与相机 PNG 流程保持。见 [退役记录](2026-10-12-interior-screen-debug-retired.md)。
- 通用 Debug 资源（2026-10-10）：已确认迁入 `Assets/Mine/Special/Debug/`，保留 GUID 与 Renderer 绑定，活动入口已同步；Unity 编译和两个默认 Pass 0 的完整 640×360 PNG 验证通过，原场景 / Renderer 状态保持。见 [记录](2026-10-10-debug-resource-migration.md)。
- 后处理搬运包结构对齐（2026-10-10）：DDOF / Kuwahara / SNN / SSL / SSO 五个老搬运包按近期模板重排 feature / shader 结构（CBUFFER、分隔块、SubShader 状态、Settings 分层 Header、PropertyToID、sealed、Release/Dispose），清理死声明与 4 个备份目录；**渲染路径、算法、序列化字段名与 pragma 未动**。Editor 在运行但 bridge 挂死，改由 Editor.log 取得编译证据：changed=11、Assembly-CSharp 重编含本轮改动、无 error CS / Shader error；Metal 逐变体与画面对照待验证，见 [记录](2026-10-10-postprocess-port-structure-pass.md)。
- HashFunction（2026-10-10）：通用 Hash 已提炼，等价调用迁移，并按最终随机作用统一 RainDrop / SpecularGI、SSL radial / VanGogh 变体；Fin / SSO 与格键域因相关性证据分开。原式/新标准式 63,212 次同主机 CPU 逐位比较零差异，HLSL 语法与宏分支通过；部分序列有意改变，Unity / Metal / GPU 对照待验证，见 [记录](2026-10-10-hash-function-extraction.md)。
- BlurFunction（2026-10-10）：5 / 9 / 13 抖动核已统一核心，原接口为常量点数薄封装；CPU float32 对照通过。Editor 未运行，Metal 编译 / 实际采样预算 / 画面对照待验证，见 [记录](2026-10-10-blur-jittered-unification.md)。
- MaskBlur（2026-10-09）：Medium 已改为两轮 H/V，与 High 同配置；共享 HLSL 采样算法未变。暗点控制证据与后续方向见 [记录](2026-10-09-maskblur-stratified-darkspots.md)。优化尚未提交；Editor 未运行，本轮编译与截图待复检。

## Memory 文件

| 文件 | 日期 | 摘要 |
|------|------|------|
| [2026-10-17-ssc-celtoon-scattering-merge.md](2026-10-17-ssc-celtoon-scattering-merge.md) | 2026-10-17 | SSC 合并 CelToon 散射：保留 URP 17 RenderGraph，新增默认关闭的 HG/高度环境光路径；静态检查与 Unity 编译通过，未接入 Renderer、无画面验证。 |
| [2026-10-17-celtoon-asset-audit.md](2026-10-17-celtoon-asset-audit.md) | 2026-10-17 | CelToon→Lab 资源审计：OutlineFunction 已迁入；SSC 同 GUID 为 Lab 后续版本未覆盖；RimToon/SSR/Portal/ASE 水体等按退役、第三方或脚本依赖跳过。 |
| [2026-10-16-gizmo-render-handover.md](2026-10-16-gizmo-render-handover.md) | 2026-10-16 | GizmoRender 更名与结构收敛完成；13 份 meta/GUID 保留、六组件接入、最终编译及 67 项 Editor 回归通过，源码/验证/证据归档。 |
| [2026-10-16-production-tool-handover.md](2026-10-16-production-tool-handover.md) | 2026-10-16 | 续接框架与模板收尾，修复 Noise 2D 载入被预览替换及 3D 假回看；同步五工具文档，新增功能侧验证入口，编译与 37 项 Editor 回归通过，场景/Renderer 指纹不变。 |
| [2026-10-15-production-tool-framework.md](2026-10-15-production-tool-framework.md) | 2026-10-15 | **生产类工具统一框架**：五个 Generator/Baker 窗口接入抽象骨架（固定路径 + Bake/Load/Save + 所有权），保存改原位更新保 GUID、不全局 SaveAssets；净减 636 行，编译与 eval 确认，按钮交互待验收。 |
| [2026-10-15-diffuse-high-performance.md](2026-10-15-diffuse-high-performance.md) | 2026-10-15 | Diffuse High 1/3网格，保留8射线64步；正式两轮原始墙钟与A/B/A画质归档，68组/四负控通过，低中档与地面残留未解决。 |
| [2026-10-14-diffuse-sampling-sequence.md](2026-10-14-diffuse-sampling-sequence.md) | 2026-10-14 | Diffuse块级数字网/径向区间抖动；固定Medium/0.5 Trace与history RMS降14.2%/15.4%，地面残留未解决。旧临时记忆按新路由补归档。 |
| [2026-10-14-diffuse-multiscale-reconstruction.md](2026-10-14-diffuse-multiscale-reconstruction.md) | 2026-10-14 | Diffuse两级1→2复用共享核心，66组/四个负控制通过；同视角弱时缓history RMS降22.6%，预算/时域/Specular保持，视觉与GPU成本待验。 |
| [2026-10-13-ssgi-shared-spatial-reconstruction.md](2026-10-13-ssgi-shared-spatial-reconstruction.md) | 2026-10-13 | Diffuse/Specular共享25tap几何核心、Diffuse移除H/V中间RT/Pass，1.5texel适配避免奇偶子网格；49组与三个负控制通过，Specular回归0、用户画质待确认。 |
| [2026-10-13-diffuse-weak-temporal-diagnosis.md](2026-10-13-diffuse-weak-temporal-diagnosis.md) | 2026-10-13 | 静态时缓0.5抖动：8组真实持久GI回读，RAY/DDA几乎相同、冻结采样降逾99.9%、共享时域有效；Sigma单改仅降9.3%，宽核重建候选待对照，生产源码/原配置不改。 |
| [2026-10-13-shared-screen-mask.md](2026-10-13-shared-screen-mask.md) | 2026-10-13 | 公共 R8／RendererList 与 raw-depth／标量算子，双消费者接入；22 全图对照零误差、47 GPU／32 URP 回归通过，源码／验证输入／证据归档。 |
| [2026-10-13-ssr-framework-rebuild.md](2026-10-13-ssr-framework-rebuild.md) | 2026-10-13 | 旧 SSR 增量 DDA/HiZ 原地替换逐格/slab、SSGI 预算与 RAY 对齐；39 库/39 相机正控、三个负控制与 isolated SSGI-only 24 块计时通过，原内存配置不改，运动/薄前景画质待验。 |
| [2026-10-13-ssgi-dda-wall-timing.md](2026-10-13-ssgi-dda-wall-timing.md) | 2026-10-13 | Medium 实际原场景 M2/Metal 同步墙钟重现 DDA 延迟增加、Diffuse 主导；36 块 / 432 样本与预算控制、严格报告验证归档，生产配置不改。 |
| [2026-10-12-ssgi-dda-integration.md](2026-10-12-ssgi-dda-integration.md) | 2026-10-12 | SST 适配公共库，默认 DDA/RAY 对照、128/256/512 单元与逐 GI 配置历史失效；39 相机组 / 1,152,437 断言、距离/历史错误控制通过，未测 GPU 收益或连续运动画质。 |
| [2026-10-12-screen-trace-library.md](2026-10-12-screen-trace-library.md) | 2026-10-12 | 通用 RAY/DDA/HiZ、按需帧内 RenderGraph 深度金字塔、独立 GPU 首命中与旧 RAY 回归；39 组 / 69,432 断言与负控制通过，真实 SSGI DDA 接线待下一阶段。 |
| [2026-10-12-maskrbr-mainlight-phase.md](2026-10-12-maskrbr-mainlight-phase.md) | 2026-10-12 | MaskRBR v2 主光／峰值归一 HG／角窗，移除 Manual 和 HDR 提取；最终像素角窗、三 Pass／R16，47 GPU／32 真实隔离 URP 控制通过，证据与 v1 输入归属登记。 |
| [2026-10-12-maskrbr-v1.md](2026-10-12-maskrbr-v1.md) | 2026-10-12 | 独立源遮罩径向光束：天空／Layer 物体、HDR 预乘与归一化核、三档预算与长度正交；56 GPU／20 真实隔离 URP 检查通过，源码、验证入口、精选 PNG／报告归档，现有场景与资产保持。 |
| [2026-10-12-interior-screen-debug-retired.md](2026-10-12-interior-screen-debug-retired.md) | 2026-10-12 | 删除无运行引用的 InteriorMapping 专用验证 Shader/meta，通用 Debug 工具保留；README 与布局状态同步，图像验证 skill/reference/helper 仍可复用。 |
| [2026-10-10-validation-artifact-archive.md](2026-10-10-validation-artifact-archive.md) | 2026-10-10 | Hash 复跑工具与历史验证证据归档入口；旧 source/目标/哈希按批准清单登记，活跃基线保留。 |
| [2026-10-10-debug-resource-migration.md](2026-10-10-debug-resource-migration.md) | 2026-10-10 | 通用 Debug Feature / Shader 集中到 Special/Debug，GUID 与 Renderer 绑定保持；旧目录清理、菜单及活动入口同步，Unity 编译与真实 PC Renderer 的两个完整 PNG 验证通过。 |
| [2026-10-10-postprocess-port-structure-pass.md](2026-10-10-postprocess-port-structure-pass.md) | 2026-10-10 | **5 个老搬运包结构对齐**：shader 侧 CBUFFER / include 归位 / 分隔块 / SubShader 状态，feature 侧 Settings 分层 + PropertyToID + sealed + Dispose；清死声明与 4 个备份目录，序列化名与渲染路径不变。Editor.log 证据编译通过；Metal 逐变体与画面未验证。 |
| [2026-10-10-hash-function-extraction.md](2026-10-10-hash-function-extraction.md) | 2026-10-10 | **通用 Hash 库**：五个入口按最终作用与输入域裁决，等价抽取与变体行为合并分别验证；原式/新标准式 CPU 对照、故障控制、HLSL 语法及宏分支通过。Unity / Metal / GPU 未验证。 |
| [2026-10-10-blur-jittered-unification.md](2026-10-10-blur-jittered-unification.md) | 2026-10-10 | **BlurFunction 统一抖动核**：5 / 9 / 13 共用一个固定展开循环与权重表；原接口薄封装，保留分布与归一化。规范及 CPU float32 对照通过；Editor 未运行，实际 Metal 编译 / 样本预算待验证。 |
| [2026-10-09-maskblur-stratified-darkspots.md](2026-10-09-maskblur-stratified-darkspots.md) | 2026-10-09 | **MaskBlur 暗点与两轮 H/V**：纯红 / 常量 GPU 控制证明小遮罩有限采样可产生起伏与漏采；记录等宽分层核及匹配参考。Low / Medium / High 统一两轮，Medium 当前与 High 同配置；本轮 Medium 仅静态检查，Editor 未运行，编译 / 截图待复检。 |
| [2026-10-08-rimtoon-maskblur-pbrtoon.md](2026-10-08-rimtoon-maskblur-pbrtoon.md) | 2026-10-08 | **RimToon 退役与 PBRToon 遮罩柔光**：独立 MaskBlur、专用 Mask Pass、PC_Renderer 接入；`1bfef8a` 功能 / `0011167` 删除 49 文件。遮罩、遮挡、参数端点、质量档与 Play 验证通过，用户确认功能无误。 |
| [2026-09-28-ssgi-integration-and-archive.md](2026-09-28-ssgi-integration-and-archive.md) | 2026-09-28 | **SSGI 统一整合与收档清理**：Codex 全天 13 提交完成统一入口（`SSGIFeature` 同帧三路 + 公共时域 + 单次合成），17:33 用量限额中断，收档清理由 Claude 接手——删两份设计骨架（4 文件 `delete_gated`）、移除 `PC_Renderer.asset` 旧 SpecularGIFeature 子资产（运行时探针 `features=2`）、修正 AO/DiffuseGI 文档引用。**可复用**：Codex 会话 jsonl 用户消息在 `response_item.role=user`（非 `user_message`）；中断点查 `thread_history_1.sqlite` 最后 failed turn；`m_RendererFeatureMap` 十六进制串是 List<long> 类型失配的死数据，手改 feature 列表无需维护；跨会话确认不被 auto 分类器承认，删除前需本会话点名确认；`unityctl script eval` 多语句须显式 `return` |
| [2026-09-21-ssgi-consolidation.md](2026-09-21-ssgi-consolidation.md) | 2026-09-21 | **SSGI 家族收敛、SSR 系列退役**：`PostProcess/{SSR,SSPR,StochasticSSR}/` 连目录全删（28 文件走 `delete_gated`）；`ScreenSpaceTrace.hlsl` 迁入 `SSGI/` 根，三处 include 同步改指新路径，消除 SSGI → 待删目录的反向依赖；两份进度骨架迁入 `SSGI/`，新增 Phase 6「统一 Composite 待实施」。**三条可复用经验**：`AssetDatabase.GetDependencies` **不跟踪 HLSL include**（别用它验证 include）；`GetShaderMessages` 的沉默要用**负控探针**先校准（故意 include 不存在路径 → 应报 `Couldn't open include file`），再删旧文件重导入取 0 msg 才算证据；`Assets/Mine/` 下 **Bash 删除被 Edit deny 级联挡住**（`rmdir` 与 `rm -d` 均拒，同命令在 /tmp 通过），而 `delete_gated` 只删文件不删目录 → 空壳目录无合法通道。附「家族私有共享库」第三档归属裁定（规范缺口待 meta-developer 补）与 `.backup_v*` 逐字节可恢复性核对法 |
| [2026-09-21-speculargi-unified-fallback.md](2026-09-21-speculargi-unified-fallback.md) | 2026-09-21 | **SpecularGI 统一反射链落地**：SSSR/GGX 使用 SSR 首命中，SSPR 与显式 Cubemap 依次回退；按 Camera 保存颜色/深度历史；闪烁根因是初版 1/2 分辨率+9 点、来源拒绝/硬钳制，以及空 Cubemap 下 hit/黑色 miss 高方差。最终改为 Medium 1/4、5×5 25 点、连续时域，并用 HistoryWeight 证实时域有效；用户确认效果正确。 |
| [2026-09-21-opencode-era-ledger.md](2026-09-21-opencode-era-ledger.md) | 2026-09-21 | **任务台账（2026-08-10 → 09-21）**：时间基点 = 首个 `opencode-go` 会话 08-10 08:06；按主题聚合已完成任务并指向权威记录；标注 **7 项无 memory 记录的任务**（09-11 水面、09-14/09-15 光照库、09-16 光照文件弃用、08-26 Van Gogh 移植等）；在飞项与 09-21 清理动作。**新会话冷启动入口** |
| [2026-09-21-diffusegi-ssgi-phase3.md](2026-09-21-diffusegi-ssgi-phase3.md) | 2026-09-21 | **DiffuseGI（SSGI Phase 3）落地、事件排序修复与验收**：Intensity=0 位精确、天空不参与 gather、正交可用、三档接线正确、滤波是保边弱平滑（≈4.47%）；**排序修复** = `BeforeRenderingTransparents` → `AfterRenderingSkybox`（让合成早于 `_CameraOpaqueTexture` 拷贝）；旧的「调试 RT 污染 / gather 命中透明几何」两条结论已撤回（截屏伪影）；残留水面变暗已定性为管线路径差异（197 px / −0.027，与 GI 和事件无关）。附离屏 RT 对照采集法与四条工具/API 陷阱 |
| [2026-09-21-ssgi-phase4-ao.md](2026-09-21-ssgi-phase4-ao.md) | 2026-09-21 | **AO（SSGI Phase 4）落地与验收**：SSAO 半球核 / HBAO 水平线积分双模式、四 Pass（Trace/BlurH/BlurV/Composite）、可见度约定 `V`、档位 (除数,方向,步进) = Low(4,4,6)/Medium(2,6,8)/High(2,8,12)；持久化进 `PC_Renderer.asset`（第 8 个 Feature）；离屏对照读数：`intensity 0.0001→1` 只变暗不变亮、天空 100% 可见、无像素 < 0.5、合成公式中位误差 0.0000、两次采集逐字节一致；**三个新陷阱** = 跨帧回读全局纹理必得 `UnityBlack` 4×4 / 后处理色彩分级改写 Debug 灰度 / `SaveAssets` 回写整个会话态（曾把 DiffuseGI 的 `m_Active` 写成 0，已修回）。HBAO 为有意简化版，不得当 Bavoil 2008 等价实现引用 |
| [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md) | 2026-09-20 | **RT 回读与画面判读的四个陷阱**（一次误判复盘）：必须全图统计不能抽样；控制组通过 ≠ 结论成立；**「空」可能正是正确答案**（空与否是场景/档位状态的函数）；**目视判读的印象不是证据**（弱信号画面是歧义的，须先算期望→定阈值→再看图） |
| [2026-09-20-unityctl-tool-pitfalls.md](2026-09-20-unityctl-tool-pitfalls.md) | 2026-09-20 | **unityctl 工具四坑**：`screenshot capture` **不可靠**（5 次仅 1 次落盘、报出的分辨率与产物对不上、丢弃传入目录）→ 取像改用 `screenshot window GameView`；`editor run/stop` 是进程生命周期、**`editor stop` 会直接关掉 Editor**；`script eval` 编译错误不回显真错，且 `-u` 加的是 using 而非程序集引用；**本机 Unity 是非 Hub 布局**（`/Applications/Unity/Unity-6000.3.14f1/Unity.app`），`editor run` 必须带 `--unity-path` |
| [2026-09-20-parameter-layering.md](2026-09-20-parameter-layering.md) | 2026-09-20 | 参数分 Technical/Performance/Artistic 三层：判据是「成本占比 + 是否需要微调」；**半接线参数**（grep 数值字面量而非参数名）；位级等价的字面量参数化；档位表纯函数展开；开关类参数用「0=关闭」；softness 命名错位；blend=0 是 lerp 恒等故不必跳过 dispatch；uniform 绑定契约静态核对 |
| [2026-09-20-temporal-accum-pcss.md](2026-09-20-temporal-accum-pcss.md) | 2026-09-20 | 时域累积抽离为 TemporalFunction.hlsl 共享库 + PCSS 接入；SetComputeVectorParam 与 uniform 宽度必须匹配；Kernel invalid 的真错只在 Editor.log |
| [2026-09-20-snowy-wrapup.md](2026-09-20-snowy-wrapup.md) | 2026-09-20 | Snowy 双 Pass 原型收尾、材质旧参数清理、最终实现说明与旧贴图确认删除 |
| [2026-09-18-parameter-range-policy.md](2026-09-18-parameter-range-policy.md) | 2026-09-18 | 参数 0/1 端点风险统一入口钳制，原型阶段避免逐点冗余保护；Snowy 纹理风效 |
| [2026-09-17-pbrtoon-urp17.md](2026-09-17-pbrtoon-urp17.md) | 2026-09-17 | PBRToon 五 Pass 实例化/Stereo 与 URP 17 结构迁移，保留原材质算法 |
| [2026-07-24-pcss-integration.md](2026-07-24-pcss-integration.md) | 2026-07-24 | PCSS 软阴影完整方案 |
| [2026-07-30-interaction-system.md](2026-07-30-interaction-system.md) | 2026-07-30 | 正交交互系统：Manager/Processor 架构 + Verlet 波方程 + 移动域重投影 |
| [2026-07-30-fgd-lut-baker.md](2026-07-30-fgd-lut-baker.md) | 2026-07-30 | FGD LUT 烘焙工具 + ENVFunction 合并 + _UseFGDLut 自动检测 |
| [2026-08-06-poss-per-object-soft-shadow.md](2026-08-06-poss-per-object-soft-shadow.md) | 2026-08-06 | POSS 逐物体软阴影：重构 + PCF + 极简组件，ShaderTagId 废弃改用专用 ShadowCaster 材质 |
| [2026-08-04-water-caustics-screen-independent.md](2026-08-04-water-caustics-screen-independent.md) | 2026-08-04 | 水体焦散屏幕无关化：ddx/ddy 基底分析 + 常数 sceneDet + 世界空间 corrDet |
| [2026-08-07-ssr-dda-fisheye-w-sign-flip.md](2026-08-07-ssr-dda-fisheye-w-sign-flip.md) | 2026-08-07 | SSR DDA 鱼眼扭曲根因：近平面 w 过零导致齐次插值奇异，DDA/Ray3D 盲区互补 |
| [2026-08-25-feature-screen-debug.md](2026-08-25-feature-screen-debug.md) | 2026-08-25 | RendererFeature 屏幕调试：真实场景中间 RT 输出 + Game View 观察，替代 Probe/Pixel Probe |
| [2026-08-27-starrynight-angle-field.md](2026-08-27-starrynight-angle-field.md) | 2026-08-27 | 多中心角场拓扑（N 源必断裂）+ 圆周值/周期消费配方 + 四种融合方案对比 + StarryNight 三文件拆分 |
| [2026-09-02-starrynight-wheat-bands.md](2026-09-02-starrynight-wheat-bands.md) | 2026-09-02 | TheStarryNight 麦田前景收尾：多层横带 + 循环位移回卷 + SKY/MOUNT/WHEAT 三分类命名 |
| [2026-09-03-editor-window-family-ruling.md](2026-09-03-editor-window-family-ruling.md) | 2026-09-03 | 窗口壳归属裁定(更正)：editor-baker-window.cs 从未落盘，EditorWindow 壳归 window/ 家族非 baker |
| [2026-09-03-standard-code-window-family.md](2026-09-03-standard-code-window-family.md) | 2026-09-03 | standard 补代码框架(standard-shader.shader/standard-script.cs) + renderpass 双变体合并 + postprocess 文档并入 README + window 家族新开 |
| [2026-09-03-generator-unification.md](2026-09-03-generator-unification.md) | 2026-09-03 | Curve/Noise Generator 统一：CurveBake 共享骨架去重 + C2 余数/空结果修复 + period 死参清理 + PackChannels 下沉 + 窗口迁工具目录 Editor/ |
| [2026-09-03-render-hlsl-template-families.md](2026-09-03-render-hlsl-template-families.md) | 2026-09-03 | render 家族落地(直写单 Pass + 复杂材质对) + hlsl 新家族：Special/HLSL 共享库三档依赖决策表 + 私有/共享拆库裁决 |
| [2026-09-04-interior-mapping-archive.md](2026-09-04-interior-mapping-archive.md) | 2026-09-04 | InteriorMapping + InteriorMap Baker 完成归档：正式产物、方向/投影约定、验证证据与迁移/清理结果 |
| [2026-09-17-uv-aspect-anisotropy.md](2026-09-17-uv-aspect-anisotropy.md) | 2026-09-17 | UV 空间各向异性：屏幕 UV 内做 R/S 必然剪切/拉伸 + 逆映射语义 + TRS2D_InverseTransformUV |
