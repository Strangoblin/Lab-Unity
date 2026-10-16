# Screen Mask

共享屏幕空间标量遮罩生成模块。MaskBlur 的原材质 Pass 与 MaskRBR 的覆盖材质共用 RendererList／RenderGraph 流程；可见性、背景判断与标量组合复用 [MaskFunction](../../HLSL/MaskFunction.md)。

## 输出与依赖

`RecordObjects` 返回当前相机、当前 RenderGraph 内有效的 `TextureHandle`：R8_UNorm、R 通道 0–1 覆盖度、Bilinear／Clamp、单采样、无深度附件、清黑。描述符复制 `sizeReference` 的尺寸与 XR 布局，因此跟随输入纹理尺寸，不保证等于相机最终输出尺寸。空对象列表仍执行清零。

`depth` 必须对应同帧 `_CameraDepthTexture`：helper 声明两者的读取依赖，实际采样由调用方 Shader 完成。helper 不替换全局深度绑定，也不自动为任意材质补齐可见性、裁剪或混合。对象 Mask Pass 应按所需契约设置 `ZWrite Off / ZTest Always / ColorMask R / BlendOp Max / Blend One One`；当前两个消费者均如此。

原材质 Pass 可沿用材质自己的顶点变形、裁剪与权重；当前 PBRToon 的 MaskBlur Pass 为实体不透明表面。覆盖材质策略只支持所给 Shader 实现的几何与裁剪，现有 MaskRBRMask 不复制原材质 alpha clipping、顶点位移或特殊剔除。两条现有路径均限制在 opaque queue。

## API

命名空间：`Mine.Rendering`。

```csharp
TextureDesc ScreenMaskRenderer.CreateDescriptor(
    RenderGraph graph, TextureHandle sizeReference, string name, bool clear = true);

TextureHandle ScreenMaskRenderer.RecordObjects(
    RenderGraph graph, ContextContainer frameData,
    TextureHandle sizeReference, TextureHandle depth,
    int layerMask, ShaderTagId[] shaderTags,
    Material overrideMaterial, int overridePassIndex,
    string name, int outputGlobalID = -1);
```

`shaderTags` 为非空的有序 Pass 搜索列表；`layerMask` 与当前 Renderer 的 opaqueLayerMask 相交。`overrideMaterial=null` 时调用原材质 Pass，否则使用覆盖材质及其 Pass 索引。调用方持有材质并负责创建／释放，helper 没有运行时 Material 或长期纹理状态。

`outputGlobalID=-1` 不发布全局；传入 ID 时在生成 Pass 后发布调用方自己的名字，消费者须声明对应 `UseTexture`／`UseGlobalTexture` 依赖。Feature 跳过或切换相机后不能将旧全局值视为当前有效输出，不缓存跨帧 Handle。

## 现有接入

| 消费者 | 对象策略 | 保留在效果侧的流程 |
| --- | --- | --- |
| MaskBlur | `MaskBlur` 材质 Pass，保留 `_MaskBlurWeight` | 预乘场景颜色提取、H/V 滤波、柔光合成 |
| MaskRBR | Forward／ForwardOnly／Unlit／GBuffer 搜索，覆盖材质 Pass 0 | 背景／物体联合 Pass 0、径向采样、主光 HG／角窗和合成 |

MaskRBR 的背景来源表示深度远平面／清屏区域，不保证该区域一定绘制了 Skybox。全屏联合沿用原 Pass 和绑定，只共用描述符与 HLSL 算子。原 Shader 菜单、Settings、全局纹理名、Pass 索引及 .meta／GUID 均保留；MaskRBRMask 是公共可见性函数的兼容消费壳。

公共遮罩只表达覆盖度。调用方决定它用于 source、receiver 或其他用途；当前两个效果使用 source，扩散后不再乘源遮罩，允许光束／柔光超出原轮廓。共用代码不等于共用一次绘制；只有输入、策略、选择范围、尺寸及生成时点一致时才能复用同一个 Handle。

## 扩展边界

- Stencil：另设输入策略，通过有效的深度模板附件和 Ref／ReadMask／Compare 生成覆盖度；普通采样深度图不提供模板值，附件尺寸／MSAA 与模板位占用需独立处理。
- 颜色筛选：指定场景颜色或外部纹理，明确读取阶段、线性色彩／HDR 约定、颜色距离与软阈值，再生成标量覆盖度。
- 纹理／程序遮罩：输出相同覆盖度契约即可接入消费者；可用 `CreateDescriptor(clear:false)` 创建全屏覆写目标。
- 多通道颜色／物体 ID：单独定义载荷、格式与组合／采样规则，不使用本模块固定的 R8 描述符；离散 ID 不做线性插值。

这些来源尚未实现，本轮实现对象生成和公共标量算子，不引入公共 RendererFeature 或自动缓存。

## 验证

迁移对照入口为 `Validation~/PipelineEquivalence.cs`，使用临时 Renderer／URP 与预览场景，直接比较线性 float 图像。旧实现的 22 份必要输入归入 `Validation~/Baselines/2026-10-13/`，采用 gzip 后 Base64 的无损编码；解码后是 320×180、RGBA little-endian float32、从左下开始的行序，没有显示映射。

将下面的输出目录改为本次未使用的任务目录，再使用连接到本项目的 unityctl 执行：

```sh
unityctl script execute -f Assets/Mine/Special/Rendering/Mask/Validation~/PipelineEquivalence.cs -t 60 -- compare tmp/sessions/YYYY-MM-DD-mask-rerun-ID/output Assets/Mine/Special/Rendering/Mask/Validation~/Baselines/2026-10-13
```

`baseline outputDirectory baselineDirectory` 参数用于在明确的旧实现版本生成新基线，不能在重构后重新生成基线来证明与旧版一致。Compare 先验证完整基线尺寸，再检查全部 RGBA、有限值、有效来源非空、权重／遮挡／空来源与轮廓外扩散控制；容差 `0.002 × (1 + |expected|)`，失败时保存实际图像供诊断。

2026-10-13，Unity 6000.3.14f1／URP 17／Metal（Apple M2）：旧实现基线 22/22 控制通过；正式归档入口迁移对照 22/22 通过，22 × 320 × 180 全图 RGBA 最大误差为 0。每次 25 渲染请求／25 相机回调；临时管线、原场景／Renderer、光照设置及登记的遮罩／深度／主光全局值恢复。MaskRBR 原验证入口复跑 47/47 GPU、32/32 真实 URP 通过；相关四 Shader supported=true、当前 ShaderUtil 消息 0，Pass 数保持 4／3／1／6。

现有场景与资产未保存；XR、动态分辨率、全 MSAA 组合、现有多 Feature 排序、实际 RenderGraph 预算与 GPU 耗时不在本轮验收范围内。公共库复用不增加现有 Pass／RT 配置预算，也不据此声明耗时改善。
