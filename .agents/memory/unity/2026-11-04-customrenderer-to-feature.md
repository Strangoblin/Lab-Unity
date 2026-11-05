# CustomRenderer → InteractionCaptureFeature（2026-11-04）

`Assets/Mine/Special/Renderer/InteractionCapture/` 取代已删除的 `CustomRenderer`（`ScriptableRenderer` 子类）。

## 根因（不是「API 变了」，是记账点没了）

URP 17 只有 `Setup(ScriptableRenderContext, ref RenderingData)` 会调用自定义 `ScriptableRenderer` 的记账逻辑，而该重写在 RenderGraph 路径下不再执行；RG 的入口 `ScriptableRenderer.OnRecordRenderGraph` 是 `internal virtual`，第三方子类无法 override。工程里 `URP_COMPATIBILITY_MODE` 虽然定义在 Standalone（所以旧代码能编译），但 `UniversalRenderPipelineGlobalSettings` 的 `RenderGraphSettings.m_EnableRenderCompatibilityMode = 0`，`useRenderGraph = true`，于是 `Setup()` 一次都没跑：`_CustomDepthTexture` 从未产生，`_InteractionOriginTex` 恒空。

判定特征：旧代码能编译、其余效果正常、只有自定义 Renderer 相机的输出为空 —— 这不是 shader 或层掩码问题，先查 `useRenderGraph`。

## 为什么不开全局兼容模式

`SSGI`（Diffuse/Specular/AO）、`SSL`、`MaskBlur`、`MaskRBR`、`DDOF`、`SNN`、`POSS`、`Picker`、`ScreenWarp`、`TransparentDepthMerge`、`SSC`、`SSSM`、`SSO` 都只实现了 `RecordRenderGraph`。全局开兼容模式会让它们**静默**失效（不是报错），代价远大于迁移一个 Renderer。

## 方案形状

相机专用的 `UniversalRendererData` 把 `opaqueLayerMask` / `transparentLayerMask` / `prepassLayerMask` 全置 0，URP 自己不画任何东西，Feature 拥有相机目标：

- 深度 Pass（`BeforeRenderingPrePasses`）：`TextureDesc` D32_SFloat_S8_UInt、Point/Clamp、`clearBuffer = true`，`SetRenderAttachmentDepth(depth, AccessFlags.Write)`，`SetGlobalTextureAfterPass(depth, "_CustomDepthTexture")`。
- 法线 Pass（可选）：`DepthNormalOnlyPass.GetGraphicsFormat()`，**用独立私有深度附件**，不覆盖已发布的深度（旧实现复用同一张 depth，会把 `_CustomDepthTexture` 重画成法线层集合，属旧代码的意外）。
- 颜色 Pass（`BeforeRenderingOpaques`）：`SetRenderAttachment(activeColorTexture, 0, AccessFlags.Write)` + `cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0)`，再画颜色层；深度附件绑 `activeDepthTexture`（只为 interactor shader 的 Stencil `Ref 2`），采样深度靠 `builder.UseGlobalTexture(_CustomDepthTexture, AccessFlags.Read)` 建依赖。

## 迁移时容易静默失败的几点

- **Skybox 必须关**：相机 Clear Flags 是 Skybox 时 URP 的 `DrawSkyboxPass` 会在颜色 Pass 之后画进 `targetTexture`，RFloat RT 的 R 通道被天光污染。`RenderScene.unity` 的 `InteractionCamera` 已改 SolidColor + 黑。
- **`AccessFlags.Write` 不等于清屏**：Write 表示「旧内容不要」，真正的清屏来自资源 `clearBuffer`（非 native pass 在创建时清，native pass 由编译器 load action 清，见 `RenderGraphResourceRegistry.CreateTextureCallback`）或自己在 render func 里 `ClearRenderTarget`。`ClearTargetsPass` 用的是后者。
- **全局纹理的生产/消费要成对声明**：`SetGlobalTextureAfterPass` + `UseGlobalTexture(id, AccessFlags.Read)`；只写不读时给 Pass 设 `AllowPassCulling(false)`，否则没有 in-graph 消费者时 Pass 可能被裁掉，全局永远是旧值。
- **别接管 Overlay 相机**：颜色 Pass 会清屏，对 Overlay 相机会抹掉 Base 相机的结果，`AddRenderPasses` 里直接跳过。
- **相机输出 RT 必须有深度缓冲**：`RenderTexture(... , depth = 0, ...)` 当 `targetTexture` 时，URP 每帧打「output Render Texture must have a depth buffer」并回退 `DefaultFormat.DepthStencil`（`UniversalRendererRenderGraph.cs:1810`）。功能不坏但会刷屏；给 RT 加 24 位深度、或把场景内嵌 RT 的 `m_DepthStencilFormat` 改成 92（`D24_UNorm_S8_UInt`）即可。
- **`UniversalRendererData` 新建后 `postProcessData` 为 null 是允许的**：URP 注释明说「No postProcessData means that post processes are disabled」（`UniversalRenderer.cs:456`）；相机上 `m_RenderPostProcessing` 也是 0。
- **`m_PrepassLayerMask` 会被序列化回调覆盖**：`m_AssetVersion <= 2` 时 `OnAfterDeserialize` 把 prepass 掩码同步成 opaque 掩码。用脚本建资产要保证写进去时版本已是 3，否则三个掩码会「被合并」。

## 迁移脚本与验证

`Validation~/MigrateInteractionRenderer.cs`（读取旧 `CustomRendererData` 的串行化字段 → 删除并重建为 `UniversalRendererData` + 子资产 Feature → 写 `m_RendererFeatures` / `m_RendererFeatureMap` → 回填 `PC_RPAsset.m_RendererDataList[1]`）。`AssetDatabase.DeleteAsset` + `CreateAsset` 同一路径复用了原 GUID（`043a360db22f840808eb7dd39afcb466`），场景里只引用 renderer index，无需改引用。

`Validation~/CaptureSmokeTest.cs`（`SingleCameraRequest` 渲染 64×64 RFloat 临时目标，真实 `InteractionManager` 两个 shader）：

```
{"status":"ok","withFeatureMaxR":1.0000,"withoutFeatureMaxR":0.0000,"rendererIndex":1,"errors":[],"warnings":[]}
```

Feature 关闭时的对照为 0，同时证明 URP 自身的 pass 在这套 renderer 上确实什么都不画。Unity 6000.3.14f1 / URP 17.3.0 / Metal，Editor 内渲染请求，未覆盖 Play 模式与真实 `InteractionManager` 运行时。

`ProjectSettings.asset` 里的 `URP_COMPATIBILITY_MODE` 未删（`ProjectSettings/` 未入库）：删除前已全仓确认无 `Setup` / `OnCameraSetup` / `ConfigureCameraTarget` 使用，只剩 URP 自己多编译一套兼容路径。
