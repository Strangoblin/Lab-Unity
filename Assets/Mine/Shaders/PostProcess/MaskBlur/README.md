# Mask Blur

独立的遮罩柔光后处理。保留原 RimToon 的“提取遮罩内场景颜色 → 模糊 → 叠加柔光”行为，遮罩由材质的专用 `MaskBlur` Pass 提供；光照与边缘光由物体材质负责。

## 共享遮罩

对象遮罩的 R8 描述符与 RendererList／RenderGraph 流程复用 [ScreenMaskRenderer](../../../Special/Rendering/Mask/README.md)，深度可见性与标量组合复用 [MaskFunction](../../../Special/HLSL/MaskFunction.md)。本效果继续调用原材质的 `MaskBlur` Pass，保留 PBRToon 顶点处理与 `_MaskBlurWeight`；场景颜色提取、滤波和柔光合成仍由本效果负责。

2026-10-13 公共 Mask 迁移：真实 URP 的 22 项旧版／新版全图 RGBA 对照全部通过、最大误差为 0；MaskRBR 既有 47 项 GPU／32 项真实 URP 控制复跑通过。归档输入与复跑入口见公共模块 README。

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
| Technical | kernel | Gaussian | Gaussian 为固定核；JitteredGaussian 为稳定空间分层抖动核 |
| Performance | quality | Medium | 固定滤波预算档位 |
| Artistic | radiusMode | Pixels | Pixels 使用最终输出像素；Normalized 使用输出高度比例 |
| Artistic | blurRadius | 0.5 | Pixels 模式的核步长，单位为最终输出像素；非负，不改变采样数 |
| Artistic | normalizedRadius | 0.005 | Normalized 模式的核步长，单位为输出高度比例；面板范围 0–0.05 |
| Artistic | jitterStrength | 1 | JitteredGaussian 的层内位置扰动强度，范围 0–1；0 完全回到 Gaussian 路径 |
| Artistic | intensity | 0.5 | 柔光叠加强度，范围 0–1 |
| Debug | debug | Off | Off 为合成画面；Mask 为原始灰度遮罩；Blur 为真实模糊颜色 |

以下预算针对半径大于 0 的完整效果。滤波读取数是各 H/V Pass 的纹理采样总数，按一个工作像素统计；不包括 Extract、Mask 材质绘制、深度读取或 Composite。

| Quality | 工作分辨率 | JitteredGaussian | 抖动滤波读取 | Gaussian | 固定滤波读取 | 逻辑 RT 数量 |
| --- | --- | --- | --- | --- | --- | --- |
| Low | 1/2 宽高 | 每方向 5 tap，2 对 H/V | 20 | 每方向 5 tap，2 对 H/V | 20 | 7 |
| Medium | 全分辨率 | 每方向 9 tap，2 对 H/V | 36 | 每方向 5 tap，2 对 H/V | 20 | 7 |
| High | 全分辨率 | 每方向 9 tap，2 对 H/V | 36 | 每方向 5 tap，2 对 H/V | 20 | 7 |

所有档位的逻辑 RT 是 Mask、Extract、两对 Horizontal / Vertical、Composite。这些是 RenderGraph 资源数量，物理分配仍由 RenderGraph 管理，不代表未经测量的显存占用。相机颜色与请求的相机深度沿用现有 URP 管线，未新增模糊金字塔或预滤波 RT。共享 HLSL 保留 `Blur_Gaussian13Jittered` 接口供其他调用方复用，当前 Feature 不使用 13 tap。

半径不改变采样次数或 RT 数量。每轮核步长为 `requestedRadius / sqrt(iterations)`：全部档位的两轮均使用 `requestedRadius / sqrt(2)`，H/V 的种子依次为 0、1、2、3，以补偿多轮滤波的累积扩散并区分各轮采样。共享 `Blur_GetUVStep` 按最终输出尺寸换算 UV，直接用于采样，不再额外除以 downsample；Low 的纹理像素尺寸自然由 UV 换算补偿。Low 的下采样、插值和离散核仍可能使其与全分辨率档出现差异。当前 Medium 与 High 的分辨率、核、采样数、轮数和种子一致，相同参数下输出应相同；保留两个枚举值以兼容已有序列化设置。

Pixels 模式使用 `blurRadius / outputSize`，分辨率变化时像素跨度保持一致。Normalized 模式使用 `normalizedRadius × (height / width, 1)`，以输出高度为基准，两轴在像素空间等距。两个模式分别保存参数，切换时不会把像素值误解释为高度比例。输出尺寸在每次 RenderGraph 记录时读取当前相机最终 viewport；XR 使用每眼 viewport。固定五采样核的最远样本为两倍核步长。

JitteredGaussian 在一维支持域内独立扰动每个采样层，包括中心层。五采样核使用五个独立层；九采样核用九个更窄的层覆盖相同支持域。各层使用固定 Gaussian 权重，颜色与遮罩 alpha 共享同一组采样位置和权重；权重不随当次扰动距离改变。位置缩放按核的期望二阶矩补偿抖动带来的扩散变化，以近似保持与固定五采样核相同的宽度；这不保证某个像素的随机核与固定核逐样本等价。

空间噪声来自 `Blur_Hash01` 的 uint hash，输入为最终输出像素坐标、Pass 种子和采样层编号。不引用帧号或时间，同一输出像素在同一 Pass 中保持稳定。该噪声不是蓝噪声；独立分层与更高采样预算可以削弱规则条纹，但大半径、薄小物体仍可能出现静态颗粒或漏采细节。

`jitterStrength = 0` 时 Feature 关闭抖动变体，完整回到相同 Quality 的 Gaussian 路径，包括全部档位的两轮及半径补偿。共享九采样、十三采样函数的零强度分支也回到固定五采样核。对比时切换 `Gaussian / JitteredGaussian`，保持半径、Quality 和 Debug 相同。

半径为 0 时使用全分辨率 Extract，跳过全部 H/V 滤波与 Low 预滤波。强度为 0 且 Debug 为 Off 时整个 Feature 跳过。Preview、Reflection 和 Overlay 相机跳过，Game 和 Scene View 的 Base 相机执行。Create 与 Dispose 会销毁运行时材质，避免重复创建泄漏。

## 遮罩提取与 Low 预滤波

Low 的工作尺寸来自 `GetRenderTargetInfo(scene)`，宽高各整除 2，并钳制到至少 1。Extract 在目标像素中心周围的 `±0.5 × _BlitTexture_TexelSize.xy` 四个源位置，用 Point Clamp 分别采样场景颜色与原始遮罩。每个源位置先构造 `float4(scene.rgb × mask, mask)`，再以 float4 累加四个预乘值并乘 0.25。每个工作像素共 8 次纹理读取。

这样不会把已平均的背景颜色再乘平均遮罩，也避免在遮罩边缘先对两张纹理分别线性插值产生背景色串入。颜色与遮罩 alpha 在提取阶段具有相同覆盖。源尺寸为偶数且严格二比一时，四个位置对应四个源 texel 中心；奇数尺寸的整除降采样不是严格二比一，此时该过程是 2×2 近似预滤波。

Medium、High 以及所有 Radius=0 路径保留原来的单位置 Linear Clamp 提取，每个工作像素读取一次场景颜色与一次遮罩，共 2 次读取。Low 预滤波复用已有 Extract Pass 和 RT，不新增纹理或额外 Pass；开关通过 Extract 的 PassData 在执行期设置。

## 管线与输出

```
MaskBlur 材质 Pass → R8 原遮罩
scene color × mask → RGBA16F 选中色（alpha = mask）
  Low：四个源位置各自预乘后平均
  Medium / High / Radius=0：单位置线性采样
选中色 → H → V → H → V→ RGBA16F 柔光（alpha = 软化遮罩）
scene.rgb + 柔光.rgb × intensity → 新 cameraColor（保留 scene.a）
```

颜色使用预乘遮罩的形式，合成时直接相加，避免再次乘遮罩把扩散到轮廓外的柔光裁掉。选中色与模糊 RT 均为 `R16G16B16A16_SFloat`，保留 HDR 亮度和独立 alpha；最终相机输出沿用原相机颜色描述。

| 全局纹理 | 内容 | 可用阶段 |
| --- | --- | --- |
| `_MaskBlurMaskTexture` | R = 原始可见遮罩，范围 0–1 | Mask Pass 之后 |
| `_MaskBlurTexture` | RGB = 模糊后的预乘场景颜色，A = 同核模糊后的遮罩 | 最终滤波 Pass 之后；半径 0 时为未滤波选中色 |

全局纹理由 `SetGlobalTextureAfterPass` 发布，并在后续 Pass 声明 `UseGlobalTexture` / `UseTexture` 依赖。扩展消费者应在该 Feature 后执行并声明纹理读取；这些输出属于本帧、本相机，Feature 跳过时不能把上一相机的全局值当作当前有效结果。

Fullscreen Shader 使用 `TEXTURE2D_X / SAMPLE_TEXTURE2D_X`，固定核、分层抖动核与尺寸换算统一复用 `Assets/Mine/Special/HLSL/BlurFunction.hlsl` 的 XR 接口。H/V 的 Shader target 为 3.5，使用本地 `multi_compile` 核与质量变体，保证运行时切换所需变体在构建中保留。共享库中 target 低于 3.5 时回到固定五采样 XR 核的回退路径保持不变；旧 Gaussian / Bilateral 接口仍保持原有采样语义，其他效果不随本次优化改变。

## 验证

2026-10-08 首次提炼在 Unity 6000.3.14f1 / URP 17 / Metal 完成：

- C# 导入编译成功；PBRToon 六个 Pass、MaskBlur 四个 Pass 的 Shader 诊断均为 0。
- 使用 SampleScene 当前 Main Camera 和真实 Renderer Feature，在 384×216 输出下暂时隐藏其他 Renderer 建立控制组：两个 PBRToon 对象产生 449 个有效遮罩像素，柔光覆盖 591 个像素。
- 材质权重为 0、LayerMask 排除目标、不透明遮挡物覆盖目标时，遮罩均为全黑；临时对象、属性块和设置已恢复。
- 强度为 0 与 Feature 关闭的输出逐像素一致；启用柔光时仅增亮，没有像素变暗。半径为 0 与 Low / Medium / High 均执行正常。
- SampleScene 完整场景进入 Play Mode，MaskBlur 启用、两个 PBRToon 对象存在，未产生新的运行错误；结束后恢复 Edit Mode。用户随后明确确认“功能无误”。

2026-10-09 前三步优化验证（Stage 3，Low 当时为单轮 H/V）：

- 通过 `SingleCameraRequest` 渲染预览场景中的 Game Camera，执行真实 PC Renderer Feature，输出离屏 RT 为 640×360。近处与远处对象距离分别为 2、10；Normalized 核步长 0.05 在该输出下对应 18 个最终输出像素。该过程未更改或保存主场景。
- 相对旧 IGN 实现，最终显示空间 halo 高频 RMS：Medium 近处/远处下降 51.6%/54.5%，High 下降 41.3%/54.8%；Low 近处增加 5.1%、远处下降 4.5%。各档规则频谱集中度明显降低，但 Low 的有限采样颗粒仍明显。这些百分比是受控截图的诊断，不能解释成物理能量或所有场景的效果保证。
- 仅比较采样预算步骤（独立 hash 五采样 → 九采样）时，Medium 近处/远处均下降约 53%，High 两轮九采样下降约 60%/62%。曾对比 High 单轮十三采样，远处颗粒与方形尾域均更明显，最终保留两轮九采样。
- Low 预滤波接入后，Medium / High 的固定核与抖动核、正式合成图、原始 mask 均与上一阶段逐字节相同。Low 修正边界颜色提取，没有带来明显降噪收益。最终相对旧 IGN 的显示亮度总量变化约 0.16%–5.17%，宽度变化不超过 0.46 像素，未通过缩小或降低曝光获得上述高频改善。
- C# 导入编译成功；实际执行各档及调试/合成后，MaskBlur Shader 诊断为空。原场景、Main Camera、Feature 设置和其他 Feature 开关均恢复，未保存场景或 Renderer 资产。
- 滤波读取预算按实现静态统计；尚未测量 GPU 耗时，不能将纹理读取比例直接当作性能提升比例。

### 提取、RGBA 与兼容性检查

- 使用真实 Blitter、MaskBlur Shader Pass、线性 Float 纹理/RT 进行 11 项 GPU 检查，全部通过；未手工设置 `_BlitTexture_TexelSize`，验证本次绑定源纹理的自动尺寸有效。
- 2×2 源中一个红色选中像素与三个蓝色背景像素，Low 输出 RGBA 为 `(0.25, 0, 0, 0.25)`；旧分别线性采样再相乘的对照输出 `(0.0625, 0, 0.1875, 0.25)`，重现背景颜色污染。
- 恒定 HDR 源 `(40000, 20000, 10000)`、遮罩 0.5 输出 `(20000, 10000, 5000, 0.5)`，float 累加没有半精度中间和溢出。三档 H/V 恒定 RGBA 的归一化检查全部通过。
- 5×3 源到 2×1 目标的棋盘/一像素遮罩检查没有引入遮罩外高亮蓝色，结果有限且 alpha 在 0–1。此项不证明奇数尺寸的面积积分或一像素覆盖率精确。
- 真实 Feature 的八项逐像素比较通过：三档 `jitterStrength=0` 与 Gaussian 完全相同；640×360 下三档 Pixels=18 与 Normalized=0.05 完全相同；Radius=0 的三档提取完全相同。

### Low 两轮 H/V 试验（Stage 4）

在用户要求试验 Low 第二轮后，Gaussian 与 JitteredGaussian 的 Low 均改为两对 H/V；继续使用半分辨率、每方向五采样和既有四点预乘提取。每轮核步长为请求值除以 √2，种子依次 0、1、2、3。半径 0 仍跳过全部滤波，Medium / High 的配置未改动。

- C# 导入编译成功。复用相同近远相机条件取得 20 张完整 640×360 截图，实际执行的 Low 两种核都记录到第二轮种子 2、每轮 Normalized 步长 0.03535534；Shader 诊断为 0。
- 相对 Stage 3 单轮 Low，JitteredGaussian 的显示空间 halo 高频 RMS：近处下降 60.95%，远处下降 71.40%，截图粗颗粒明显减少；仍有有限采样残差。
- 重复滤波并不保证离散图像形状完全相同。近处显示亮度总量增加 1.27%，远处增加 5.71%；显示空间 σ 近处增加 0.60–0.75 像素，远处增加 0.81–1.44 像素。这些是显示空间诊断，不是物理能量变化的证明。
- Medium / High 的两种核、抖动正式合成及原始 mask 共 14 张负控制与 Stage 3 文件字节、像素完全相同。
- Low 每工作像素滤波读取从 10 增至 20，逻辑 RT 从 5 增至 7，多出两个半分辨率 Raster Pass。实际 GPU 耗时尚未测量。
- 截图后原场景与 Feature 设置经 finally 恢复，未保存场景或 Renderer 资产。Editor 重新连接后补跑八项兼容性复检：三档零抖动与 Gaussian、Pixels=18 与 Normalized=0.05，以及 Radius=0 的跨档比较均逐像素相同。

### Medium 两轮 H/V（Stage 5）

用户确认记录暗点问题后，Medium 的 Gaussian 与 JitteredGaussian 均改为两对 H/V，每轮核步长为请求值除以 √2，种子依次 0、1、2、3。全分辨率及每方向抖动九采样保留，滤波读取从 18 增至 36；固定五采样读取从 10 增至 20，逻辑 RT 从 5 增至 7。Low / High 的配置未改动，当前 Medium 与 High 的配置一致。

本轮仅修改 Feature 的固定轮数与文档，未改动共享 HLSL 采样算法。静态结构与差异检查通过；本轮 Unity Editor 未运行，尚未重新编译或采集 Medium 两轮截图。上文验证数据属于各自注明的历史阶段。

### 一轮 H/V 暗点控制验证

当前抖动位置在等宽层内均匀选取，各层乘固定 Gaussian 近似质量；它对应分段常量核的分层估计，没有按 Gaussian 累计概率抽样。九采样中心层质量 0.16303，只比邻层 0.15222 略大，中心层也会移动。每方向步长 18 时中心层最大位移约 4.98 像素；中心层路径在 H/V 中的质量乘积约 0.02658，不能解释成整个二维中心像素的权重或最终值。

为了排除材质纹理、光照及 PNG 裁切，直接通过实际 H/V Shader Pass 对线性纯红硬圆滤波，640×360、RGBA16F 中间纹理、RGBAFloat 读回。源圆内 red=alpha=1，圆外为 0。六项控制全部通过，无新增 Shader 诊断或遗留测试材质：

| 输入 / 滤波 | 圆心附近线性红通道范围 | 结果 |
| --- | --- | --- |
| 全画面恒定 RGBA / 一轮九采样 | 同一恒定值，只有半精度舍入 | 无空间噪点 |
| 半径 80 纯红圆 / 一轮九采样 | 1–1 | 足够大纯色区域的中心不产生暗点 |
| 半径 16 纯红圆 / 一轮九采样 | 0.09448–0.26758 | 重现明显明暗起伏，无实际 0 |
| 半径 16 纯红圆 / 两轮九采样 | 0.21252–0.23315 | 明暗起伏显著减少 |
| 半径 3 纯红圆 / 一轮九采样 | 0–0.02667 | 原圆内 32 个像素中一半精确为 0，圆心也为 0 |

半径 16 的“圆心附近”定义为中心半径 4 像素；半径 3 案例统计整个原圆。所有纯红圆的 red 与 alpha 逐像素相等，最大差为 0，证明这些起伏可仅由遮罩覆盖采样产生。半径 16 的一轮五采样作为额外核对照，圆心附近 red 为 0.08441–0.36572。

密集参考按当前分段核的理想均匀、独立层模型积分，并精确包含双线性重建；不是另换连续 Gaussian。半径 16 圆心附近，一轮参考均值 0.19677、GPU 均值 0.19538；两轮参考均值 0.22169、GPU 均值 0.22176。相对各自参考的局部 RMS 分别约 17.15% 与 2.53%。两轮虽然匹配期望二阶矩，但核形状与中心均值不同，不能视为完全相同核的单纯降噪。

之前 PBRToon 单轮 Medium 截图的严格遮罩内部没有 RGB全0或近0像素；远球中心 7×7 的 PNG 红通道为 91–127，属于相对暗点。正式合成在本 Feature 阶段是 scene.rgb 加非负柔光，不能把原场景的正红色变成黑色；Blur Debug 展示的是独立柔光图。若足够大的真正纯色覆盖区内仍出现实际零值，需要另查资源或上游数据，不能一概归因随机采样。

Debug 与正式效果在同一个 AfterRenderingSkybox 阶段输出，后续透明物体仍会覆盖部分调试画面。观察纯遮罩时可临时隐藏透明物体；不能把后续物体的颜色当作遮罩值。

当前验证没有覆盖 XR 硬件及所有 MSAA / 平台组合。深度筛选使用单采样相机深度，MSAA 几何边缘的覆盖不等同于逐样本深度测试。硬件动态分辨率和导入缩放 RTHandle 的有效 viewport 组合亦不能从静态预算或本次离屏测试推定已经覆盖。
