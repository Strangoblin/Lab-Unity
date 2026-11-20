# MaskRBR

天空和指定可见物体产生屏幕空间径向光束／拖尾。光束使用采样选区中的 HDR 相机颜色，主方向光决定径向焦点和角度响应。参数由 `MaskRBRFeature.Settings` 提供。

## 共享遮罩

对象遮罩复用 [ScreenMaskRenderer](../../../Special/Renderer/Mask/README.md) 的 RendererList／RenderGraph 流程，深度可见性与标量组合复用 [MaskFunction](../../../Special/HLSL/MaskFunction.md)。`MaskRBRMask` 负责写入三类几何覆盖。

2026-10-13 公共 Mask 迁移：真实 URP 的 22 项旧版／新版全图 RGBA 对照全部通过、最大误差为 0；MaskRBR 既有 47 项 GPU／32 项真实 URP 控制复跑通过。归档输入与复跑入口见公共模块 README。

## 接入

1. 在目标 Universal Renderer Data 添加 `MaskRBRFeature`。
2. `shader` 引用同目录 `MaskRBR.shader`（`PostProcess/MaskRBR`），`maskShader` 引用 `MaskRBRMask.shader`（`PostProcess/MaskRBRMask`）。两者采用序列化引用，保证构建包含 Shader。
3. `source` 选择 Sky、Objects 或 SkyAndObjects；物体来源由 `layerMask` 选择，默认 Nothing。两者是主从关系而非并列关系，见下节。
4. 主方向光在相机前方时启用主体效果；缺少主光、主光位于相机背后或正交相机输出零光束。需要自由中心或手动焦点的旧 `focusMode`／`manualFocus` 已移除。
5. 用 Debug 的 Mask 检查几何覆盖，用 Rays 检查最终光束的颜色和角度分布，再回到 Off 调整强度。

## source 与 layerMask

- `Sky` 选择画面中露出的天空。先取相机深度的远平面／清屏区域，再扣掉相机和 Renderer 绘制的透明轮廓。`layerMask` 对该模式没有影响。
- `Objects` 只选择 `layerMask` 指定的物体。两种队列分别与 Renderer 的 Opaque Layer Mask 和 Transparent Layer Mask 取交集，再使用相机剔除结果。空掩码输出全黑并提示一次。
- `SkyAndObjects` 取两类覆盖的并集。透明物体遮住天空的区域由物体规则决定能否发光。

## 管线与复用

颜色快照在 `BeforeRenderingTransparents`，遮罩与合成最早在 `AfterRenderingTransparents`。旧资产中更早的 `passEvent` 会在运行时提升至这一阶段。

1. `MaskRBR.PreTransparentColor` 保存天空和不透明画面的 HDR RGB。
2. `MaskRBR.ObjectMask` 用 RGBA8 纹理记录选中的不透明轮廓（R）、选中的透明轮廓（G）和遮挡天空的透明轮廓（B）。透明网格按双面轮廓绘制，并由相机深度判断可见性。`Objects` 模式省略 B 通道绘制。
3. `Mask` 将所选覆盖与颜色相乘。选中的透明像素用透明混合后的相机色，天空和选中的不透明像素用快照 RGB。RGBA16F 的 A 保存覆盖率。
4. `Radial` 对已乘遮罩的 RGB 做归一化径向采样，`Composite` 用主方向光角度响应和效果强度缩放 RGB，再加回相机颜色。

常规透明材质可直接参与。天空遮挡按实体轮廓判断，半透明轮廓也会占据其像素。选中的透明像素保留画面混合色，包括背景和同一像素的透明叠层。轮廓精度受 alpha clipping、材质剔除和自定义顶点位移影响。

稳定空间随机复用 `Assets/Mine/Special/HLSL/HashFunction.hlsl`；HG 相函数复用 Core `VolumeRendering.hlsl` 的 `HenyeyGreensteinPhaseFunction`。

## 云遮光（SSC 联动）

`occludeWithClouds` 开启时，遮罩 Pass 在联合结果上再乘 `1 - SSCCloudCoverage`，因此云区不发光，径向核也不会把云后漏出的光糊到云边；云越厚，光束越弱。

跨 Feature 的数据通路是全局纹理 `_SSCCloudTexture`（A 通道为覆盖率，RGB 为预乘辐亮度），由 `SSCFeature` 的 ray march 通过 `SetGlobalTextureAfterPass` 导出。

一个带中间产物的 Feature 有两段时间，单值的 `passEvent` 表达不了，所以字段按语义拆开：

| SSC 字段 | 默认值 | 含义 |
| --- | --- | --- |
| `cloudBufferEvent` | `AfterRenderingOpaques`(300) | 云缓冲产出、可供消费的时刻 |
| `compositeEvent` | `AfterRenderingTransparents`(500) | 写回相机颜色的时刻 |

启用云遮光时，按实际阶段配置 `cloudBufferEvent < MaskRBR < compositeEvent`。MaskRBR 默认在 `AfterRenderingTransparents`（500），可将 SSC 的 Composite Event 设为 `BeforeRenderingPostProcessing`（550）。云缓冲排得过晚时本帧跳过源遮光。云先合成时，光束会叠在云上。`MaskRBRPass.ValidateCloudOrder()` 会提示冲突。

可用性由 CPU 侧 `SSCFeature.CloudOcclusionReady` 判定：只有本帧确实记录了 ray march，遮罩 Pass 才声明 `UseGlobalTexture`，避免 SSC 未启用时抛异常。SSC 关闭、Shader 缺失或 `cloudBufferEvent` 被调到本 Feature 之后时，`_MaskRBROccludeClouds` 为 0，自动退化为无遮光的旧行为。云区同时被遮罩阶段和 SSC 合成阶段各乘一次 `(1 - coverage)`，这是刻意的双重扣减，观感上是更强的云遮光。

## 参数

| 分组 | 参数 | 默认值 | 语义 |
| --- | --- | --- | --- |
| Technical | shader / maskShader | 空 | 显式绑定两个 Shader |
| Technical | source | Sky | 光束来源 |
| Performance | quality | Medium | 固定采样数与工作分辨率 |
| Occlusion | occludeWithClouds | true | 按 SSC 云覆盖率扣减源遮罩；SSC 未就绪时自动关闭 |
| Artistic | length | 0.2 | 最大采样跨度，占最终输出高度的比例 |
| Artistic | falloff | 2 | 沿采样段的权重形状；0 为均匀权重 |
| Artistic | jitterStrength | 0 | 稳定空间层内抖动，0 使用固定分布 |
| Artistic | halfAngle | 60 | 角窗半角（度，范围 1–90），限制接收角度 |
| Artistic | anisotropy | 0.7 | HG 各向异性 g（范围 0–0.95），越大越前向 |
| Artistic | intensity | 0.25 | 最终加光强度，0 关闭正式效果 |
| Event | layerMask | Nothing | 选中不透明和透明物体的 Layer；`source = Sky` 时不参与 |
| Event | passEvent | AfterRenderingTransparents | 运行时下限为 AfterRenderingTransparents；启用云遮光时需考虑 SSC 的前后顺序 |
| Debug | debug | Off | Off / Mask / Rays |

`Event` 组把两个适用范围并排列出：`layerMask` 决定**谁参与**（空间范围），`passEvent` 决定**何时执行**（帧内时机）。MaskRBR 的 `passEvent` 不是自由值：开启云遮光时它是偏序约束的一部分，见下节。

| Quality | 径向 tap 数／工作像素 | 分辨率 |
| --- | --- | --- |
| Low | 16 | 宽高各 1/2 |
| Medium | 24 | 全分辨率 |
| High | 48 | 全分辨率 |

Quality 在编译期选择固定循环预算。`length` 只缩放采样段，不增加 tap、Pass 或 RT。两轴距离在输出高度单位中计算，非方形视口保持一致；权重按位置归一化，屏幕外采样点贡献零但权重仍保留在分母，避免 Clamp 边缘条带。

## 方向、角度与亮度

仅支持主方向光。CPU 校验 `mainLightIndex >= 0` 且 `visibleLights[index].lightType == Directional`，结果写入 `_MaskRBRPhaseParameters.w`。`_MainLightPosition.xyz` 为归一化朝向光源的方向；投影 `VP × (dir, 0)` 后经 `ComputeScreenPos` 得到焦点。缺少主光、主光位于背后或正交相机输出零光束；接近侧向时以 0.1 宽度淡出，不提供 Manual 回退。

最终合成像素按 clip/device 深度 0.5 经 inverseVP 重建有限点并去除动态 UV 缩放偏置，得到朝向场景的视线方向。`mu = clamp(dot(viewDir, lightDir), -1, 1)`，相位为 `HG(g, mu) / HG(g, 1)`，峰值归一到 1；角窗 `smoothstep(cos(halfAngle), cos(0.8 × halfAngle), mu)`，外侧 20% 平滑淡出，超出 halfAngle 为零。这是便于调参的峰值归一化角度响应，不是以每球面度计的物理散射强度。

RGBA16F 径向纹理保存 `weightedSourceRGB × focusFade`。Low 升采样后，三档均在最终分辨率计算 `emittedRGB = rayTexture.rgb × phase × cone`。角度窗保持在最终分辨率。Debug Rays 展示 `emittedRGB`。

合成为 `scene.rgb + emittedRGB × saturate(intensity)`，保留 scene alpha 和 HDR 亮度。`intensity=0` 或 `length=0` 且 Debug=Off 时整个 Feature 跳过。Debug Mask 无需主光即可显示灰度覆盖率。Debug Rays 在 `length=0` 时用单 tap 显示带角度响应的源颜色。

全局贴图为 `_MaskRBRPreTransparentColor`、`_MaskRBRObjectMaskTexture`、`_MaskRBRMaskTexture`、`_MaskRBRRayTexture`。启用云遮光且 SSC 已就绪时额外读取 `_SSCCloudTexture`。

## 局限

光束允许覆盖其他不透明前景，源遮罩只决定谁发光、不决定谁接收，不提供三维深度排序／体积遮挡。观感仍需在目标场景调参。Preview、Reflection 和 Overlay 相机跳过；Game 与 Scene View 的 Base 相机执行。后续如增加前景保护，应独立设计 receiver 策略。

## 验证入口与状态

专属验证代码在 `Validation~/`，Unity 不自动导入该目录；使用连接到本项目的 unityctl 执行。输出进入当前任务的 `tmp/sessions/`，不保存场景或 Renderer 资产。GPU 数值检查使用线性 float 回读；PNG 只用于观察，不用于反推 HDR 强度。

将下面的目录名改为本次复跑的未使用任务目录。第二条命令会额外生成四张完整 640×360 PNG（baseline、mask、rays、composite，已无 source 抓图）；省略最后一个参数即可只生成数值报告。

```sh
maskrbr_run_dir="tmp/sessions/YYYY-MM-DD-maskrbr-rerun-ID"
unityctl script execute -f Assets/Mine/Shaders/PostProcess/MaskRBR/Validation~/ShaderChecks.cs -t 60 -- "$maskrbr_run_dir/output/shader-results.json"
unityctl script execute -f Assets/Mine/Shaders/PostProcess/MaskRBR/Validation~/PipelineChecks.cs -t 60 -- "$maskrbr_run_dir/output/pipeline-results.json" "$maskrbr_run_dir/output/captures"
unityctl script execute -f Assets/Mine/Shaders/PostProcess/MaskRBR/Validation~/CloudOcclusionCheck.cs -t 60 -- "$maskrbr_run_dir/output/cloud"
```

历史 v2 的以下结果用于对照旧版的标量光束。2026-10-12，Unity 6000.3.14f1 / URP 17 / Metal（Apple M2）：

- 47 项 GPU 控制全部通过：三种联合遮罩、三档固定预算与常量增益、横／竖屏解析斜坡、越界零贡献及 Clamp 负控、稳定抖动、缺光／背光／正交、独立视线与 HG／角窗方程、g=0／0.7／0.95、峰值为 1、Y 镜像负控、场景 RGB 无关、主光颜色与总强度线性缩放及 alpha。逐通道容差为 `0.002 × (1 + |expected|)`；R8／R16 只断言存储的 R，RGBA16F 断言全部 RGBA，回读各分量均检查有限值。测试全局状态恢复。
- 32 项隔离真实 URP 管线控制全部通过，28 次渲染请求对应 28 次相机回调：可见／被遮挡物体、天空联合、三档轮廓外拖尾、非居中主光与独立径向／HG 方程、真实主光 RGB 与强度、场景 HDR RGB 无关、参数零端点、角度单调与窄窗、Low 升采样后的窗外零输出、缺光／背光／正交、稳定抖动、renderScale=0.1／2.0 的最终尺寸绑定。渲染错误为零，临时管线、活动场景、原 Renderer 和光照全局设置恢复；没有保存现有场景或资产。

现有场景中多个 Feature 的先后组合、XR 硬件、动态分辨率、全部 MSAA 组合、RenderGraph 实际资源预算与 GPU 耗时尚未验证；性能档数字是配置采样预算，不是实测耗时。观感仍需在目标场景调参。
