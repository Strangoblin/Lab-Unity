# Unity Developer — Memory Index

> 按日期命名的 memory 文件索引。新 memory 以 `YYYY-MM-DD-<slug>.md` 格式添加。

---

## 技术栈（不变式）

- Unity 6 + URP 17+，macOS Metal
- 自动化：unityctl（Editor 远程控制）、Roslyn（C# 运行时注入）
- UnityCtl 宿主边界：bridge 与 Editor 必须处于同一执行环境；沙盒 bridge 与真实宿主 bridge/Editor 不互通，以 `Unity connected` + `unityctl wait` 为连接证据
- 清理/整理回退偏好：只使用 Git（精确 pathspec 的 stash/历史），不再生成压缩备份包；永久禁止 `git stash --all`

## 关键架构决策

- **SSGI 历史纹理生命周期（2026-11-03）**：历史 RT 采用 HideAndDontSave 与 RTHandle 所有权；双缓冲底层纹理失效时重建对应历史。Editor 可控纹理失效、通道隔离与 RenderGraph 导入探针通过；真实切场景复测待确认。见 [记录](2026-11-03-ssgi-history-lifetime.md)。

- **独立草区块模板（2026-11-04）**：`Assets/Mine/Scripts/GrassManager/` 使用单块 100×100 演示地形和可调逻辑地块，草缓存相机周围最多 3×3 块，按 Main Camera 视锥及一个地块边长的水平半径剔除；Demo 默认覆盖全部地形类型，网格基础旋转可配置。独立项目批处理验证通过，手绘和 TerrainManager 适配未实现。见 [记录](2026-11-03-grass-manager-template.md)。

- **后处理 Event 面板（2026-11-02）**：所有后处理 Feature 的 `Settings` 在 `Debug` 前加 `[Header("Event")] passEvent`，默认值等于改造前硬编码事件，`AddRenderPasses` 每帧覆盖后再入队；PCSS/POSS 的投射 Pass 仍固定在 `AfterRenderingShadows`，SSGI 三模块共用一个事件（按模块拆分已因时域滤波与 HiZ 双双分裂回退）。模板与 parameter-panel 规范已同步。见 [记录](2026-11-02-postprocess-event.md)。

- **透明深度合并（2026-11-01）**：`TransparentDepthMergeFeature` 在 `AfterRenderingOpaques` 把透明队列 depth-only 重绘进 URP 的 `_CameraDepthTexture` 拷贝（绑成深度附件 + 强制 ZWrite），相机深度附件不动、彩色输出逐像素不变；已挂 `PC_Renderer`。三个静默失败点：`RendererListParams` 必须 `isPassTagName = false`、Unlit 只匹配 `SRPDefaultUnlit`、`ConfigureInput(Depth)` 才能保证拷贝提前于本 pass。见 [记录](2026-11-01-transparent-depth-merge.md)。

- **渲染管线归并（2026-11-01）**：`Scripts/CustomRenderer`、`Special/Rendering/{Mask,ScreenSpaceTrace}` 合并为 `Special/Renderer/` 三子目录；`Special/UniversalFunction/PreparationRenderer` 删除（三个类自带 `[Obsolete]` 指向 CustomRenderer，且无 GUID 引用），`.meta` 成对搬移故 `IA_Renderer` / `PC_Renderer` 引用不变。命名空间不动。**移动后必须同步写死路径**：SSGI 三份 Validation、两份模块 Validation、`RayStepFunction.hlsl` 注释与相关 README。`cli.py move` 不支持目录（`[Errno 13]`），目录用 `mv` + `.meta`。见 [记录](2026-11-01-renderer-consolidation.md)。

- **SSGI 统一 Trace（2026-10-30）**：统一入口的 DDA / Ray / HiZ 与深度金字塔资源移至顶层并同步两路；同帧 HiZ 共用金字塔，旧值以 Diffuse 迁移。2026-10-28 在线试验与共享探针通过；2026-10-30 Editor 全量回归通过（92 组 / 1,153,512 断言 + 5 负控），动态画质待验。见 [记录](2026-10-30-ssgi-shared-trace.md)。

- **HiZ 像素步幅（2026-10-30）**：旧实现把 DDA 单位（屏幕 `L/maxSteps` 像素）当 1 像素再乘 `2^mip`，每步只覆盖 `maxSteps/L` 路径，是漏打的根因。改为按像素前进 `2^mip`、`parentEnd` 细分到 mip 0、预算 `2 × maxSteps`、零投影回退 DDA。GPU 与新参考模型一致（库 39 组 / 88,826；SSGI 92 组 / 1,153,512）；**不等于画面漏打减少**，仍需人工看。见 [记录](2026-10-30-hiz-pixel-stride.md)。

- **PBRFunction 拆分与代表模型（2026-10-30）**：毛发段拆为 `Special/HLSL/HairFunction.hlsl`，PBRFunction 升为 PBR 家族入口，聚合 HairFunction 与 FabricFunction（「零兄弟 include」的唯一例外，已同步知识库与技能模板）；同时补入 Oren-Nayar / Minnaert / Ashikhmin-Shirley / Wrap 漫射，Blinn-Phong / Beckmann / GTR1 / Charlie 分布，Smith 高度相关 / Kelemen / Neubelt 可见性，F90 菲涅耳与 Sheen 组合。Charlie 取感知粗糙度、其余 D 取 `roughness²`，是刻意保留的语义分叉。见 [记录](2026-10-30-pbrfunction-models-split.md)。

- **织物层（2026-10-30）**：织物 BSDF 已是 PBRToon 的 `ENABLE_FABRIC` 分支，独立 `Render/Fabric` 退役；Frag 切成「本体层 / 织物层 / 表面层」三段，合成在 `Special/HLSL/FabricFunction.hlsl`（`Fabric_ShadeLayer` 一次调用），只留织物色/覆盖度/绒面三个参数加 LUT。**Rg 只能是反射率 `baseColor·(1−metallic)`，不能是风格化后的最终色**（第一版拿着色结果当 Rg 被用户否掉）；rim/specular 被 `eff` 压住是按能量流的有意决定（本体层的光流进织物层），不是近似。Baker 已按生产工具标准规范化（固定路径 + Bake/Load/Save + 所有权 + 原位保存）。六条工具坑（shader 与 Editor 脚本外部写盘都要 ForceUpdate 重导入、`DisplayDialog` 原生模态顶死桥、`_CameraOpaqueTexture` 取不到、附加光为 PerVertex、close 的 verify 永远 MISS）与两条建模结论（SGGX 球面竖带、标量 K/S 需给透射补偏色）见 [记录](2026-10-30-fabric-km-sggx.md)。演示场景已删，只留 `Fabric.mat` 参考材质与 `FabricLut.asset`，验证图在 `Validation~/2026-10-30-pbrtoon-fabric/`。

- **Diffuse HiZ 实验（2026-10-28）**：默认 DDA，实验开关复用公共金字塔与追踪；不支持布局回退、切换只失效 Diffuse 历史。真实相机三档与粗层负控通过；M2 Metal 1080p High 隔离克隆三轮同步墙钟较低约 25–41%，其余尺寸波动大未分辨，画质仍需人工看运动场景。见 [记录](2026-10-28-diffuse-hiz.md)。

- **SSGI 快路径（2026-10-27）**：AO 历史按参数独立失效；零时缓不写历史但采样继续，三路全关不入队并使该相机历史失效；Diffuse 跨射线共享追踪准备。94 组及五个负控制通过，Metal A/B 波动大未分辨性能收益；见 [记录](2026-10-27-ssgi-fast-paths.md)。

- **SDF 逐轴归一化（2026-10-23 当前）**：Box / Square 先按每轴半尺寸归一化坐标，再取 `saturate(1-max(abs(q)))`；长短轴各自半程均为0.5，覆盖此前“除最短半尺寸”的约定。该阶段SSC用Box、Y置0只羽化XZ、fade=0.8，Metal 30项GPU读回及8个Pass/关键字组合通过；SSC后续已改为Circle、fade=0.5，见活跃上下文。见 [当前契约与验证](2026-10-23-sdf-axis-normalization.md)。

- **SDF 共享库归一化覆盖度（2026-10-22）**：`Special/HLSL/SDF.hlsl` 改为 `[0,1]` 覆盖度 SDF，中心 1、边界 0、外部 0；新增 `SDF_Circle` / `SDF_Square`，平滑运算同步改为覆盖度最大/最小/差集。规范检查、HLSL 语法编译与中心/边界/外部数值断言通过，Unity/Metal 变体未验。见 [记录](2026-10-22-sdf-normalization.md)。
| [2026-10-21-ssc-quality-tiers.md](2026-10-21-ssc-quality-tiers.md) | 2026-10-21 | SSC 三档面板（32/64/128 步，降 3/2/1）与 HG 外圈过暗修复；三档同步墙钟 3.44/9.19/40.24ms，6.2M 断言通过。HG 部分已由加性双 HG 取代。 |

- **SSC 三档与加性双 HG（2026-10-22）**：Low/Medium/High 保持32步降3、64步降2、128步降1；面板只显示三档，单一GetTier接线。着色改为峰值归一化 `HG(g=.8) + 1.0×HG(0)`；侧向相位0.0038→1.0038，暗部A/B均值约27.67x、亮部约3.29x，519k散射断言与三档623万断言通过，完整恢复。物理步长、太阳光学深度和云内多次散射仍待后续；变更未提交。见 [双HG实现](2026-10-22-ssc-dual-hg.md)。

- **SSC 性能落地（2026-10-20）**：精确Density=1早停、空高度/空密度跳采样、16位sky mask与4bilinear快速重建，HDR/几何保原精细路；target3.5，无新RT/Pass/面板。M2/1080p两场景432样本，当前64步降2普通视角同步墙钟约降25%，降1约29%；覆盖率四档差0、HDR四组及六变体通过。配置仍64/降2，未提交；见 [实现与正式证据](2026-10-20-ssc-performance.md)。

- **SSC 质量修复（2026-10-20）**：远端 SSC 已挂 PC_Renderer，双色权重改 stepShadow³ 增强阴影色；RGBA16F 保覆盖率，固定16tap深度引导B-spline与全分辨率遮挡，0档直通、边界中心积分回退。Metal六变体零消息、0..4档20张控制图/HDR正负控通过；低档变软，运动/XR/性能待验。已先保存并合并远端，修复未提交。见 [记录与对照](2026-10-20-ssc-quality.md)。

- **ScreenWarp（2026-10-19）**：独立屏幕重映射，Barrel/Pincushion/Fisheye/Panini；AfterPost 单 Raster Pass，不改相机、不请求深度法线。PC_Renderer 已接入，现支持可选固定输出 UV 的 RGBA 上层素材，与变形强度独立控制；无素材保持原行为，启用多一次贴图采样。Metal 752 组及真实 URP 52 请求/632 断言通过；Stack/XR/硬件动态分辨率/Picker 逆映射与性能待扩展。见 [遮盖扩展](2026-10-19-screenwarp-overlay.md)；[v1 历史](2026-10-19-screenwarp-v1.md)。

- **HDR Map 生产链（2026-10-19 收尾）**：HDRMapBaker 固定线性 HDR 全景图，Render Layers + Include Sky 决定内容；独立 HDRMapConverter 处理四种 2D 投影、分辨率及全景/室内覆盖。共享 HDRMapFunction 服务天空与室内命中方向；旧脚本保 GUID 归 Legacy，旧图材质 Legacy=4，新经纬图用 0。Metal 80 项功能检查和连续方向梯度通过；不含 FGD / GGX / 反射生产。见 [交付与验证](2026-10-19-hdr-map-pipeline.md)。

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

- PCSS 阴影：PSSM split（级联数随性能档 2/3/4）→ tiled atlas → blocker search → penumbra → variable PCF。**阴影尚未接入光照合成**——只在特定物体 shader 里挂了 `CustomShadowCaster` 投射；`showShadowMap` 是调试直出。后续才用 PCSS 管线取代 Unity 阴影。见 [PCSS.md](../../../Assets/Mine/Shaders/PostProcess/PCSS/PCSS.md)
- POSS 逐物体软阴影：Shadow Atlas Tile Grid → Compute 屏幕空间解算 → PCF 软边缘，与 CSM 共存。见 [POSS.md](../../../Assets/Mine/Shaders/PostProcess/POSS/POSS.md)
- 交互系统：Manager/Processor 分离架构，RT 管理下放，正交相机 CustomRenderer 深度比较输入。见 [README](../../../Assets/Mine/Scripts/InteractionManager/README.md)；模拟侧通用坑见 [gpu-simulation.md](../../knowledge/unity/standard/compute/gpu-simulation.md)
- Shader 组织：`Assets/Mine/Shaders/` 按效果分层
- 后处理：Unity 6 Blitter API（`_BlitTexture`，非 `_MainTex`）
- 知识库分类：按实际职责区分 `standard/`、`script/`、`shader/postprocess/`、`shader/render/`、`shader/hlsl/`（`particle/` 占位）；特征族目录可混合 `.cs/.shader/.compute/.hlsl`，但只有 `README.md` 是 markdown；`shader/hlsl/` 对应 `Assets/Mine/Special/HLSL` 共享库，单效果私有库与其 shader 同目录

---

## 活跃上下文

- SSGI环境与强度（2026-10-27）：Specular改用URP默认环境Cubemap和HDR解码，移除自定义天空字段；三路统一/独立强度均0–10，Specular与AO合成权重仍饱和。71组/五负控通过，实景克隆的Specular 1→10可见变化；默认环境是Unity缓存，动态Skybox不保证实时同步。见 [记录](2026-10-27-ssgi-unity-environment-intensity.md)。

- SurfaceDither（2026-10-26）：在 `Render/SurfaceDither` 新增表面稳定分形 Bayer 抖动材质；8×8 基点的 128×128×64 3D 图案、UV0/Object Space 双坐标、四 Pass 与五变体零消息，近/远边缘密度比 0.996。主光/阴影仅编译，真实场景画面未独立验证。见 [记录](2026-10-26-surface-dither.md)。

- SurfaceLines（2026-10-27）：在 `Render/SurfaceLines` 新增纯解析线条材质；UV0/Object/World 三坐标、主轴投影、导数抗锯齿、最小屏幕线宽、可选交叉线与 `_MinShade` 风格化着色。四 Pass + alpha/shadow 变体零错误零消息；球体 Game View 冒烟后已恢复原位置与 SurfaceDither 材质。见 [记录](2026-10-27-surface-lines.md)。

- ExteriorMapping（2026-10-26）：在 `InteriorMapping/` 新增实体方块面片映射，逐像素取 ray-AABB 近交点并采样六面纹理；新增 `_CubeSpace`，默认 `Object Transform` 使 billboard 只转窗口、方块吃对象世界 TRS（位置、旋转、含非均匀缩放），`Quad Relative` 保留随面片转的旧行为。CPU 面 UV/旋转/内部远交点复算、规范检查、Unity 6000.3.14f1 Pass 0 编译零消息通过。`RenderScene` 已放置 `ExteriorMapping_NoiseQuad`（Scale 3×3×3），六面分别挂 Perlin/Noise/Noise1/NoiseWave/Noise2DRG/Voronoi；三姿态、对象旋转 180° 与 Z Scale 3→1 对照确认行为，场景已保存。见 [记录](2026-10-26-exterior-mapping.md)。

- SSC 固定自适应高度（2026-10-23 当前）：整体重构维持撤回，仅复用DensityLOD的low决定局部云顶；强度现固定.5，面板字段与接线已移除。用户暂停平方衰减改动，DensityH保留原pow(r,1-thickness)，thickness=.5为sqrt(r)，并非严格半球。Circle boundaryFade保留用户当前.5。Metal8组合消息0；见 [最新裁决](2026-10-23-ssc-fixed-adaptive-height.md)。[此前可调版验证](2026-10-23-ssc-adaptive-height.md)、[结构计划](2026-10-23-ssc-column-density-plan.md)、[逐轴归一化](2026-10-23-sdf-axis-normalization.md) 与 [首次接线](2026-10-23-ssc-sdf-boundary.md) 保留历史。

- SSC 后续重设计（2026-10-20）：性能部分已按用户授权落地，见 [正式结果](2026-10-20-ssc-performance.md)；云结构/积分/面板方案仍未实施。实时RenderScene为1/4宽高+64步，磁盘128步勿覆盖；XZ×高度剖面、无步长alpha、LOD均值与光照距离耦合仍待后续。拟Coverage/Cloud Size/Erosion/Density + 单Quality。前轮探针耗时与旧.995候选保留为历史，勿与正式适配器混算，见 [原诊断与方案](2026-10-20-ssc-redesign-proposal.md)。

- HDR Map 工具已完成拆分、采样接入和最终验证；正式入口 `Tools/HDR Map Baker...` / `Tools/HDR Map Converter...`，测试归 `HDRMapConverter/Validation~/`。见 [2026-10-19 收尾](2026-10-19-hdr-map-pipeline.md)。

- NormalSmoother Baker（2026-10-17）：窗口收敛为标准 Baker —— 输入固定单个 Mesh，固定资产路径 + Bake/Load/Save + 资产所有权，去掉多目标 / 子级 / 场景回写。编译、BakeCore、Save 新建与覆盖保 GUID 冒烟通过；GUI 画面未验收。见 [Baker 记录](2026-10-17-normal-smoother-baker.md)；[批量版记录](2026-10-17-normal-smoother-window.md) 为历史。
- NormalSmoother 搬运（2026-10-17）：CelToon 法线平滑工具按项目脚本规范落到 Assets/Mine/Scripts/NormalSmoother/，公开 API/参数名保持不变；以原文件作参考实现做 21 例逐通道等价比对全部一致，骨骼权重与 UInt32 索引分支另验；Editor 窗口未搬运、无画面验证。见 [记录](2026-10-17-normal-smoother.md)。

- CelToon 资源与 SSC 替换（2026-10-17 历史）：SSC 以 CelToon 单循环密度场与 HG/高度环境光散射替换旧路径，修正 rotation；当时仅 batchmode 编译，未挂 Renderer。现已拉取远端挂载版本、收敛为2D R噪声并完成颜色/重建验证，见 [当前记录](2026-10-20-ssc-quality.md)。来源保留于 [替换记录](2026-10-17-ssc-celtoon-scattering-merge.md)、[资源审计](2026-10-17-celtoon-asset-audit.md)。

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
| [2026-11-01-renderer-consolidation.md](2026-11-01-renderer-consolidation.md) | 2026-11-01 | 三条渲染管线归入 `Special/Renderer`（CustomRenderer / Mask / ScreenSpaceTrace），保留 Unity 6 版 CustomRenderer，删除自标 `[Obsolete]` 且无人引用的 `UniversalFunction/PreparationRenderer`。`.meta` 成对搬移 → GUID 与资产引用不变；命名空间不改。写死路径（SSGI 与模块 Validation、HLSL 注释、README）须同步。`cli.py move` 不支持目录。 |
| [2026-11-01-weather-layer.md](2026-11-01-weather-layer.md) | 2026-11-01 | 天气层从 PBRToon 分离并拆成 `RainEffect` / `SnowEffect`：水痕取噪声等值带、布料吸水按 Voronoi 遮罩衰减细节、积雪闪烁只乘稳定雪面。`Properties` 未声明的贴图会让 `SetTexture` 静默失效；外部改 shader 必须 ForceUpdate；切材质/光照后至少 5 张丢弃帧再取像；「参数对但图没变」先做同机位 A/B。 |
| [2026-10-30-fabric-km-sggx.md](2026-10-30-fabric-km-sggx.md) | 2026-10-30 | 织物层：KM 双流 + SGGX + R/T LUT；Rg 只能是反射率不能是风格化最终色；三段式合成在 `FabricFunction.hlsl`，重构后成图逐像素一致。外部改 shader 必须 ForceUpdate、`_CameraOpaqueTexture` 取不到、PC_RPAsset 附加光是 PerVertex、SGGX 球面竖带、标量 K/S 要补偏色。 |
| [2026-10-27-ssgi-unity-environment-intensity.md](2026-10-27-ssgi-unity-environment-intensity.md) | 2026-10-27 | Specular改接Unity默认环境/HDR，去掉自定义天空，三路强度0–10；71组/五负控及实景克隆对照通过。 |
| [2026-10-27-surface-lines.md](2026-10-27-surface-lines.md) | 2026-10-27 | 纯解析表面线条材质：UV0/Object/World、导数抗锯齿、最小线宽、交叉线；四 Pass/变体零消息，球体冒烟后恢复场景。 |
| [2026-10-26-surface-dither.md](2026-10-26-surface-dither.md) | 2026-10-26 | 表面稳定分形 Bayer 抖动材质、3D 图案生成器与隔离验证；四 Pass/五变体零消息，近远边缘密度比 0.996。 |
| [2026-10-26-exterior-mapping.md](2026-10-26-exterior-mapping.md) | 2026-10-26 | 实体方块面片映射、六面纹理与 billboard/fixed 双模式；CPU、规范与 Unity Pass 0 编译通过，画面未验。 |
| [2026-10-22-sdf-normalization.md](2026-10-22-sdf-normalization.md) | 2026-10-22 | SDF 库改为中心 1、边界/外部 0 的覆盖度图元，新增圆/方形；规范、HLSL 语法与数值断言通过。 |
| [2026-10-20-ssc-performance.md](2026-10-20-ssc-performance.md) | 2026-10-20 | SSC精确早停/空域跳采样/sky mask快速重建；432样本同步墙钟、覆盖率/HDR/薄前景与正式证据，性能已实施，形状/面板未改。 |
| [2026-10-20-ssc-redesign-proposal.md](2026-10-20-ssc-redesign-proposal.md) | 2026-10-20 | SSC黏连/积分与光照耦合根因、216样本成本对照、候选质量档和参数重设计；生产实现未改。 |
| [2026-10-20-ssc-quality.md](2026-10-20-ssc-quality.md) | 2026-10-20 | SSC 双色增强、RGBA16F、16tap深度重建及遮挡；Metal六变体/20张控制图/HDR正负控，附对照证据与低档柔化限制。 |
| [2026-10-19-screenwarp-overlay.md](2026-10-19-screenwarp-overlay.md) | 2026-10-19 | 固定输出 UV 的可选 RGBA 遮盖与独立不透明度；Metal 752 组/URP 52 请求通过，素材槽留空，原相机/场景配置保持。 |
| [2026-10-19-screenwarp-v1.md](2026-10-19-screenwarp-v1.md) | 2026-10-19 | 四种独立屏幕映射与PC_Renderer接入；Metal432组/URP30请求通过，相机保持，交互逆映射等边界留后续。 |
| [2026-10-19-hdr-map-pipeline.md](2026-10-19-hdr-map-pipeline.md) | 2026-10-19 | Baker/Converter 拆分、四投影统一方向采样、旧 GUID 迁移；Metal 80 项功能及连续梯度通过，验证源码归档；FGD/GGX/反射排除。 |
| [2026-10-17-normal-smoother-baker.md](2026-10-17-normal-smoother-baker.md) | 2026-10-17 | 窗口改为标准 Baker：只接受单个 Mesh、固定资产路径与 Bake/Load/Save/所有权；编译、BakeCore、新建与覆盖保 GUID 冒烟通过，GUI 未验收。 |
| [2026-10-17-normal-smoother-window.md](2026-10-17-normal-smoother-window.md) | 2026-10-17 | 搬运批量平滑法线窗口到 NormalSmoother/Editor/；原位覆盖保 GUID、场景回写用持久资产、预览按参数哈希重建；编译与落盘冒烟通过，GUI 未人工验收。 |
| [2026-10-17-normal-smoother.md](2026-10-17-normal-smoother.md) | 2026-10-17 | 搬运 CelToon NormalSmoother 到 Assets/Mine/Scripts/NormalSmoother/，按项目脚本规范重排；21 例原实现逐通道等价、骨骼权重与 UInt32 分支通过；Editor 窗口未搬运、无画面验证。 |
| [2026-10-17-ssc-celtoon-scattering-merge.md](2026-10-17-ssc-celtoon-scattering-merge.md) | 2026-10-17 | SSC 以 CelToon 单循环密度场 + HG/高度环境光替换旧路径，保留 URP 17 RenderGraph、三噪声模式与 ray count；静态检查与 Unity 编译通过，未接入 Renderer、无画面验证。 |
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
| [2026-08-04-water-caustics-screen-independent.md](2026-08-04-water-caustics-screen-independent.md) | 2026-08-04 | 水体焦散屏幕无关化：ddx/ddy 基底分析 + 常数 sceneDet + 世界空间 corrDet |
| [2026-09-02-starrynight-wheat-bands.md](2026-09-02-starrynight-wheat-bands.md) | 2026-09-02 | TheStarryNight 麦田前景收尾：多层横带 + 循环位移回卷 + SKY/MOUNT/WHEAT 三分类命名 |
| [2026-09-03-editor-window-family-ruling.md](2026-09-03-editor-window-family-ruling.md) | 2026-09-03 | 窗口壳归属裁定(更正)：editor-baker-window.cs 从未落盘，EditorWindow 壳归 window/ 家族非 baker |
| [2026-09-03-standard-code-window-family.md](2026-09-03-standard-code-window-family.md) | 2026-09-03 | standard 补代码框架(standard-shader.shader/standard-script.cs) + renderpass 双变体合并 + postprocess 文档并入 README + window 家族新开 |
| [2026-09-03-generator-unification.md](2026-09-03-generator-unification.md) | 2026-09-03 | Curve/Noise Generator 统一：CurveBake 共享骨架去重 + C2 余数/空结果修复 + period 死参清理 + PackChannels 下沉 + 窗口迁工具目录 Editor/ |
| [2026-09-03-render-hlsl-template-families.md](2026-09-03-render-hlsl-template-families.md) | 2026-09-03 | render 家族落地(直写单 Pass + 复杂材质对) + hlsl 新家族：Special/HLSL 共享库三档依赖决策表 + 私有/共享拆库裁决 |
| [2026-09-04-interior-mapping-archive.md](2026-09-04-interior-mapping-archive.md) | 2026-09-04 | InteriorMapping + InteriorMap Baker 完成归档：正式产物、方向/投影约定、验证证据与迁移/清理结果 |
| [2026-09-17-uv-aspect-anisotropy.md](2026-09-17-uv-aspect-anisotropy.md) | 2026-09-17 | UV 空间各向异性：屏幕 UV 内做 R/S 必然剪切/拉伸 + 逆映射语义 + TRS2D_InverseTransformUV |
| [history.md](history.md) | — | 已归档记忆时间线，逐日一句 |
