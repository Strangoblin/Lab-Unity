---
name: 透明物体深度合并进 _CameraDepthTexture
description: TransparentDepthMergeFeature 在 AfterRenderingOpaques 把透明队列 depth-only 重绘进相机深度拷贝，三条 RenderGraph API 事实与验证方法。
date: 2026-11-01
---

# 透明深度合并（2026-11-01）

`Assets/Mine/Special/Renderer/TransparentDepth/TransparentDepthMergeFeature.cs`，已挂 `Assets/Settings/PC_Renderer.asset`（index 6，Debug 默认 Off）。

## 为什么这么做

用户要「透明物体写深度并合并总深度图，让各种后处理兼并透明物体」。选定方案是**独立深度拷贝 + 合并**，不是「透明前 prepass 直写相机深度」：后者会改相机深度附件，令后画的后层透明被前层深度裁掉，破坏混合。

实现不额外开 RT：URP 的 `_CameraDepthTexture` 拷贝本身就是一张可写深度纹理，把它当**深度附件**（`AccessFlags.ReadWrite`）绑上，再用 `RenderStateBlock{ Depth: ZWrite=true, ZTest=LEqual }` 重绘透明队列，硬件深度测试即完成 min 合并。相机深度附件（`resources.cameraDepth`）全程不碰，彩色输出逐像素不变（开关两态 MD5 相同）。

## 三条会静默失败的 API 事实

1. **`RendererListParams` 默认 `isPassTagName = true`**。配 `tagValues = {ShaderTagId.none}` 会按「pass 名恰为 none」过滤，`CreateRendererList` 照样成功、`DrawRendererList` 照样执行，**但一个物体都不画**，没有任何报错。URP 自己的 `RenderingUtils.CreateRendererListWithRenderStateBlock` 显式写 `isPassTagName = false`，照抄。
2. **URP `Unlit.shader` 的 forward pass 没有 LightMode 标签**，只匹配 `SRPDefaultUnlit`；`UniversalForward` / `UniversalForwardOnly` 对 Unlit 材质完全无效。默认标签表三项都要有。
3. **`ConfigureInput(ScriptableRenderPassInput.Depth)` 决定了拷贝时机**。`UniversalRenderer` 取 `min(AfterRenderingTransparents, requiresDepthTextureEarliestEvent - 1)`：本 pass 在 `AfterRenderingOpaques`(300) 请求深度，拷贝就被提前到 299，即使 RP Asset 的 Copy Depth Mode 是 `AfterTransparents` 也成立。缺这一行，`AfterTransparents` 模式下合并结果会被随后的拷贝整个覆盖。

## 工程现状约束

本工程 `PC_Renderer` 是 Forward+ / MSAA 1 / `Copy Depth Mode = ForcePrepass`（枚举值 2，不是 `AfterOpaques`）。ForcePrepass 下 `_CameraDepthTexture` 是独立 handle，与 `cameraDepth` 不同 —— 已用临时日志确认（`depth == resources.cameraDepth` → False）。

## 验证方法（本机）

- `unityctl screenshot capture` 在本机**写不出文件**（`Warning: screenshot file not written after 5s`，Game View 未取像），改用 `RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest{ destination = rt })` 离屏渲到 RT，再 `ReadPixels` + `EncodeToPNG`。
- 定位「pass 在跑但没画东西」最快的办法：临时在 render func 里 `context.cmd.ClearRenderTarget(true, false, Color.black, 1f)`，若调试视图整体变黑说明深度附件写入通道是通的，问题只可能在 RendererList。
- 测试场景 `Assets/Mine/Special/Renderer/Test/TransparentDepthTest.unity`：建场景要用 `NewSceneMode.Additive` + 保存后 `CloseScene`，避免顶掉用户/并行会话正在打开的场景（当时 `RainEffectTest` 属于另一个会话）。

## 限制

软边 alpha 在深度上变成硬边遮挡（需要按层掩码筛对象）；透明几何多画一遍；透明 shader 自采样 `_CameraDepthTexture` 有自遮挡风险；MSAA>1、XR、Deferred 未验。详见模块 [README](../../../Assets/Mine/Special/Renderer/TransparentDepth/README.md)，验证记录在 `tmp/sessions/2026-11-01-transparent-depth/VERIFY.md`。

## SSGI 事件位置（2026-11-01 定稿）

总面板 `Event → passEvent` 一个事件控制三份光照，默认 `BeforeRenderingPostProcessing`(550)。原来硬编码 `AfterRenderingSkybox`(400)。

- 深度选择不需要额外拷贝：`UniversalResourceData.cameraDepthTexture` 是同一个 handle，RenderGraph 按录制顺序给读写下版本，事件早于合并写回读到未合并深度，晚于则读到合并后。
- 默认 550 配合本 Feature 在 500 的合并，AO/Diffuse/Specular 都能作用到透明表面；代价是它们也"看见"了透明物体的法线/颜色。

### 按模块设事件：试过，放弃了

`renderPassEvent` 是 `ScriptableRenderPass` 的属性，按模块设事件必须把 SSGI 拆成三个入队 Pass（内部 `RecordIntegrated` 的模块无法各自设事件）。实现可行，但有三处连带代价：

1. **时域滤波必须每模块一份**。共用一份 `SSGITemporalFilter` 会让 `BeginFrame/CompleteFrame` 每帧跑三次：`frameIndex` 连跳、深度历史写三遍，重投影错位。拆开等于每帧最多三份全分辨率深度历史（原一份）。
2. **HiZ 金字塔按 `SourceRevision` 缓存**（`ScreenSpaceDepthPyramid.FrameCache`），事件不同的模块必须用不同 revision，否则晚的那个命中早的用未合并深度建好的层级。
3. 跨 Pass 传 handle 只能靠同一帧内的字段暂存，多相机录制时靠 `AddRenderPasses` 每相机重置。

用户判断收益不抵成本，保留单 Pass。若将来重启，代码骨架见 2026-11-01 会话记录。

### 事件覆盖与验证（旧记录）

- URP 录制顺序支持这条链：透明 forward → `RecordCustomPassesWithDepthCopyAndMotion(AfterRenderingTransparents)` → … → `RecordCustomRenderGraphPasses(BeforeRenderingPostProcessing)` → 之后才跑 `m_PostProcessPassRenderGraph`。
- **覆盖对比**：本 pass 在 300 时 SSGI(400)/MaskBlur/MaskRBR(400) 都已在合并之后；450 时连 SSC/PCSS/POSS(500) 也能覆盖；500 反而丢掉 400 这三个 —— 所以「推迟」只在确实想让 SSGI 作用到透明表面上时才值得做（画面会变：AO/GI/镜面叠到透明物体上）。
- **A/B 陷阱**：想用 SSGI 的 `Debug=AO` 输出做「合并开/关」对照是无效的 —— AO 有逐帧随机旋转，`temporal.ao = 0` 时同一配置连拍两张 MD5 都不同，噪声淹没差异。要确定性对照，就把调试 blit 挪到 SSGI 的事件上读 `resources.cameraDepthTexture`（同一 handle、同一版本），不要依赖 SSGI 自己的输出。
- `ScriptableRendererFeature.isActive` 是只读属性，开关要走 `SerializedObject` 的 `m_Active`。

