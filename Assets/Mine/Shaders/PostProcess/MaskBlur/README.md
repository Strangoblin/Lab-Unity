# Mask Blur

独立的遮罩柔光后处理。保留原 RimToon 的“提取遮罩内场景颜色 → 模糊 → 叠加柔光”行为，遮罩由材质的专用 `MaskBlur` Pass 提供；光照与边缘光由物体材质负责。

## 接入

1. 在 Universal Renderer Data 添加 `MaskBlurFeature`。
2. 将 `settings.shader` 指定为同目录 `MaskBlur.shader`（菜单路径 `PostProcess/MaskBlur`）。序列化引用保证构建包含该 Shader。
3. 使用带 `Tags { "LightMode" = "MaskBlur" }` Pass 的材质。PBRToon 已提供此 Pass，材质 `_MaskBlurWeight` 控制贡献，默认 1，设为 0 退出柔光。
4. 用 Feature 的 `layerMask` 限定物体层；实际选择还会与 Renderer 的 Opaque Layer Mask 相交。

遮罩针对相机已渲染的不透明表面生成。材质 Mask Pass 采样 `_CameraDepthTexture`，将被其他表面遮挡的片元置零；使用 `ZTest Always`、`ZWrite Off` 和 `BlendOp Max / Blend One One`。独立 R8 遮罩每帧清黑，单采样且不绑定相机深度附件，因此与开启 MSAA 的相机配合时不会产生附件采样数不匹配。此路径不需要 Stencil。

Feature 在 `AfterRenderingSkybox` 执行，用当前场景颜色提取柔光。`requiresIntermediateTexture` 保证可采样的当前颜色目标，`ConfigureInput(Depth)` 只请求需要的相机深度；不会为该效果额外复制未使用的 `_CameraOpaqueTexture`。因此已完成的 PBRToon 光照、边缘光和其他先行效果都会自然参与柔光；后续透明物体继续按原管线渲染。

## 参数

| 分组 | 参数 | 默认值 | 行为 |
| --- | --- | --- | --- |
| Technical | shader | 空 | 必须绑定本目录 Shader |
| Technical | layerMask | Everything | 限定产生遮罩的物体层 |
| Performance | quality | Medium | 固定滤波预算档位 |
| Artistic | blurRadius | 0.5 | 控制屏幕空间扩散尺度，Medium 最远样本偏移为 `2 × radius` 个场景 texel；范围 0–5 |
| Artistic | intensity | 0.5 | 柔光叠加强度，范围 0–1 |
| Debug | debug | Off | Off 为合成画面；Mask 为原始灰度遮罩；Blur 为真实模糊颜色 |

| Quality | 工作分辨率 | Gaussian H/V 轮数 |
| --- | --- | --- |
| Low | 1/2 宽高 | 1 |
| Medium | 全分辨率 | 1 |
| High | 全分辨率 | 2 |

每个方向固定采样 5 次。每轮实际步长使用 `requestedRadius / (downsample × sqrt(iterations))`，以源纹理 texel 为单位；Low 补偿半分辨率像素跨度，High 补偿重复滤波的累积扩散，避免把质量档直接变成额外的半径倍率。下采样、插值和核的离散化仍可能产生细微的平滑差异，各档不保证逐像素相同。默认 Medium 沿用旧实现的分辨率、采样分布和一次 H/V 模糊；旧权重作总和归一化，保持全覆盖遮罩的 alpha 为 1。半径只控制样本分布，不影响采样次数。半径为 0 时强制使用全分辨率提取并跳过所有滤波 Pass，结果为原始选中色。

强度为 0 且 Debug 为 Off 时整个 Feature 跳过。Preview、Reflection 和 Overlay 相机跳过，Game 和 Scene View 的 Base 相机执行。Create 与 Dispose 会销毁运行时材质，避免重复创建泄漏。

## 管线与输出

```
MaskBlur 材质 Pass → R8 原遮罩
当前 scene color × mask → RGBA16F 选中色（alpha = mask）
选中色 → Gaussian H → Gaussian V → RGBA16F 柔光（alpha = 软化遮罩）
scene.rgb + 柔光.rgb × intensity → 新 cameraColor（保留 scene.a）
```

颜色使用预乘遮罩的形式，合成时直接相加，避免再次乘遮罩把扩散到轮廓外的柔光裁掉。选中色与模糊 RT 均为 `R16G16B16A16_SFloat`，保留 HDR 亮度和独立 alpha；最终相机输出沿用原相机颜色描述。

| 全局纹理 | 内容 | 可用阶段 |
| --- | --- | --- |
| `_MaskBlurMaskTexture` | R = 原始可见遮罩，范围 0–1 | Mask Pass 之后 |
| `_MaskBlurTexture` | RGB = 模糊后的预乘场景颜色，A = 同核模糊后的遮罩 | 最终滤波 Pass 之后；半径 0 时为未滤波选中色 |

全局纹理由 `SetGlobalTextureAfterPass` 发布，并在后续 Pass 声明 `UseGlobalTexture` / `UseTexture` 依赖。扩展消费者应在该 Feature 后执行并声明纹理读取；这些输出属于本帧、本相机，Feature 跳过时不能把上一相机的全局值当作当前有效结果。

Fullscreen Shader 使用 `TEXTURE2D_X / SAMPLE_TEXTURE2D_X`，Gaussian 的像素步长来自源纹理 `_BlitTexture_TexelSize`。私有核直接在本效果 Shader 内定义，以适配 XR 纹理维度并避免引入通用 BlurFunction 对深度、法线的额外依赖。

## 验证

2026-10-08 在 Unity 6000.3.14f1 / URP 17 / Metal 完成：

- C# 导入编译成功；PBRToon 六个 Pass、MaskBlur 四个 Pass 的 Shader 诊断均为 0。
- 使用 SampleScene 当前 Main Camera 和真实 Renderer Feature，在 384×216 输出下暂时隐藏其他 Renderer 建立控制组：两个 PBRToon 对象产生 449 个有效遮罩像素，柔光覆盖 591 个像素。
- 材质权重为 0、LayerMask 排除目标、不透明遮挡物覆盖目标时，遮罩均为全黑；临时对象、属性块和设置已恢复。
- 强度为 0 与 Feature 关闭的输出逐像素一致；启用柔光时仅增亮，没有像素变暗。半径为 0 与 Low / Medium / High 均执行正常。
- SampleScene 完整场景进入 Play Mode，MaskBlur 启用、两个 PBRToon 对象存在，未产生新的运行错误；结束后恢复 Edit Mode。

Debug 与正式效果在同一个 AfterRenderingSkybox 阶段输出，后续透明物体仍会覆盖部分调试画面。
观察纯遮罩时可临时隐藏透明物体；不能把后续物体的颜色当作遮罩值。
当前验证没有覆盖 XR 设备及所有 MSAA / 平台组合。深度筛选使用单采样相机深度，
MSAA 几何边缘的覆盖不等同于逐样本深度测试。
