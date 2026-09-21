# Unity Developer — Memory Index

> 按日期命名的 memory 文件索引。新 memory 以 `YYYY-MM-DD-<slug>.md` 格式添加。

---

## 技术栈（不变式）

- Unity 6 + URP 17+，macOS Metal
- 自动化：unityctl（Editor 远程控制）、Roslyn（C# 运行时注入）
- UnityCtl 宿主边界：bridge 与 Editor 必须处于同一执行环境；沙盒 bridge 与真实宿主 bridge/Editor 不互通，以 `Unity connected` + `unityctl wait` 为连接证据
- 清理/整理回退偏好：只使用 Git（精确 pathspec 的 stash/历史），不再生成压缩备份包；永久禁止 `git stash --all`

## 关键架构决策

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

## Memory 文件

| 文件 | 日期 | 摘要 |
|------|------|------|
| [2026-09-21-opencode-era-ledger.md](2026-09-21-opencode-era-ledger.md) | 2026-09-21 | **任务台账（2026-08-10 → 09-21）**：时间基点 = 首个 `opencode-go` 会话 08-10 08:06；按主题聚合已完成任务并指向权威记录；标注 **7 项无 memory 记录的任务**（09-11 水面、09-14/09-15 光照库、09-16 光照文件弃用、08-26 Van Gogh 移植等）；在飞项与 09-21 清理动作。**新会话冷启动入口** |
| [2026-09-21-diffusegi-ssgi-phase3.md](2026-09-21-diffusegi-ssgi-phase3.md) | 2026-09-21 | **DiffuseGI（SSGI Phase 3）落地、事件排序修复与验收**：Intensity=0 位精确、天空不参与 gather、正交可用、三档接线正确、滤波是保边弱平滑（≈4.47%）；**排序修复** = `BeforeRenderingTransparents` → `AfterRenderingSkybox`（让合成早于 `_CameraOpaqueTexture` 拷贝）；旧的「调试 RT 污染 / gather 命中透明几何」两条结论已撤回（截屏伪影）；残留水面变暗已定性为管线路径差异（197 px / −0.027，与 GI 和事件无关）。附离屏 RT 对照采集法与四条工具/API 陷阱 |
| [2026-09-21-ssgi-phase4-ao.md](2026-09-21-ssgi-phase4-ao.md) | 2026-09-21 | **AO（SSGI Phase 4）落地与验收**：SSAO 半球核 / HBAO 水平线积分双模式、四 Pass（Trace/BlurH/BlurV/Composite）、可见度约定 `V`、档位 (除数,方向,步进) = Low(4,4,6)/Medium(2,6,8)/High(2,8,12)；持久化进 `PC_Renderer.asset`（第 8 个 Feature）；离屏对照读数：`intensity 0.0001→1` 只变暗不变亮、天空 100% 可见、无像素 < 0.5、合成公式中位误差 0.0000、两次采集逐字节一致；**三个新陷阱** = 跨帧回读全局纹理必得 `UnityBlack` 4×4 / 后处理色彩分级改写 Debug 灰度 / `SaveAssets` 回写整个会话态（曾把 DiffuseGI 的 `m_Active` 写成 0，已修回）。HBAO 为有意简化版，不得当 Bavoil 2008 等价实现引用 |
| [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md) | 2026-09-20 | **RT 回读与画面判读的四个陷阱**（一次误判复盘）：必须全图统计不能抽样；控制组通过 ≠ 结论成立；**「空」可能正是正确答案**（空与否是场景/档位状态的函数）；**目视判读的印象不是证据**（弱信号画面是歧义的，须先算期望→定阈值→再看图） |
| [2026-09-20-unityctl-tool-pitfalls.md](2026-09-20-unityctl-tool-pitfalls.md) | 2026-09-20 | **unityctl 工具三坑**：`screenshot capture` **不可靠**（5 次仅 1 次落盘、报出的分辨率与产物对不上、丢弃传入目录）→ 取像改用 `screenshot window GameView`；`editor run/stop` 是进程生命周期、**`editor stop` 会直接关掉 Editor**；`script eval` 编译错误不回显真错，且 `-u` 加的是 using 而非程序集引用 |
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
