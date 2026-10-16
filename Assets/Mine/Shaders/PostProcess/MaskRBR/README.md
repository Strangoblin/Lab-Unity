# MaskRBR

天空和指定可见物体产生屏幕空间径向光束／拖尾。源轮廓外可以接收光束；效果不追踪三维体积光。独立于 SSL 与 MaskBlur，参数由 `MaskRBRFeature.Settings` 提供。v2 起仅由主方向光驱动，不再读取场景亮度。

## 共享遮罩

对象遮罩的 R8 描述符与 RendererList／RenderGraph 流程复用 [ScreenMaskRenderer](../../../Special/Rendering/Mask/README.md)，深度可见性与标量组合复用 [MaskFunction](../../../Special/HLSL/MaskFunction.md)。本效果继续用原 `MaskRBRMask` 覆盖材质及 source 选择；背景／对象联合保留 Pass 0，径向核、主光 HG／角窗和合成仍由本效果负责。

2026-10-13 公共 Mask 迁移：真实 URP 的 22 项旧版／新版全图 RGBA 对照全部通过、最大误差为 0；MaskRBR 既有 47 项 GPU／32 项真实 URP 控制复跑通过。归档输入与复跑入口见公共模块 README。

## 接入

1. 在目标 Universal Renderer Data 添加 `MaskRBRFeature`。
2. `shader` 引用同目录 `MaskRBR.shader`（`PostProcess/MaskRBR`），`maskShader` 引用 `MaskRBRMask.shader`（`PostProcess/MaskRBRMask`）。两者采用序列化引用，保证构建包含 Shader。
3. `source` 选择 Sky、Objects 或 SkyAndObjects；物体通过 GameObject `layerMask` 选择，默认 Nothing。
4. 主方向光在相机前方时启用主体效果；缺少主光、主光位于相机背后或正交相机输出零光束。需要自由中心或手动焦点的旧 `focusMode`／`manualFocus` 已移除。
5. 从 Debug 的 Mask、Rays 依次检查输入与标量输出，再回到 Off 调整光束强度。

## 管线与复用

`AfterRenderingSkybox` 执行：覆盖材质重绘可见物体遮罩（R8）→ 主 Shader 联合遮罩（`Mask=0`，R8）→ 标量径向采样（`Radial=1`，R16_SFloat）→ 主光加法合成（`Composite=2`，RGBA16F）。主 Shader 从四个 Pass 减为三个，移除 HDR 颜色提取 Pass／Source RT；径向中间纹理从 RGBA16F 改为单通道 R16。透明物随后按正常管线渲染。

标量径向直接以线性滤波采样全分辨率联合遮罩，遮罩与径向方程不再读取场景 RGB。遮罩联合与物体选择／遮挡沿用旧契约：独立单采样 R8 遮罩，按相机深度排除遮挡；天空通过相机深度的远平面／清屏值识别；源选择联合使用 max，避免重叠来源重复加权。

物体由 RendererList 重绘，选择范围是 `layerMask ∩ Renderer opaqueLayerMask`。覆盖材质绘制实体表面并读取相机深度，支持普通不透明网格，不要求原材质带 MaskBlur Pass。不会复制原材质的 alpha clipping、特殊剔除或自定义顶点位移；这类材质以及透明 source 需要以后提供原材质专用 Mask Pass。

稳定空间随机复用 `Assets/Mine/Special/HLSL/HashFunction.hlsl`；HG 相函数复用 Core `VolumeRendering.hlsl` 的 `HenyeyGreensteinPhaseFunction`。

## 参数

| 分组 | 参数 | 默认值 | 语义 |
| --- | --- | --- | --- |
| Technical | shader / maskShader | 空 | 显式绑定两个 Shader |
| Technical | source | Sky | 光束来源 |
| Technical | layerMask | Nothing | 选中不透明物体的 Layer |
| Performance | quality | Medium | 固定采样数与工作分辨率 |
| Artistic | length | 0.2 | 最大采样跨度，占最终输出高度的比例 |
| Artistic | falloff | 2 | 沿采样段的权重形状；0 为均匀权重 |
| Artistic | jitterStrength | 0 | 稳定空间层内抖动，0 使用固定分布 |
| Artistic | halfAngle | 60 | 角窗半角（度，范围 1–90），限制接收角度 |
| Artistic | anisotropy | 0.7 | HG 各向异性 g（范围 0–0.95），越大越前向 |
| Artistic | intensity | 0.25 | 最终加光强度，0 关闭正式效果 |
| Debug | debug | Off | Off / Mask / Rays |

| Quality | 径向 tap 数／工作像素 | 分辨率 |
| --- | --- | --- |
| Low | 16 | 宽高各 1/2 |
| Medium | 24 | 全分辨率 |
| High | 48 | 全分辨率 |

Quality 在编译期选择固定循环预算。`length` 只缩放采样段，不增加 tap、Pass 或 RT。两轴距离在输出高度单位中计算，非方形视口保持一致；权重按位置归一化，屏幕外采样点贡献零但权重仍保留在分母，避免 Clamp 边缘条带。

## 方向、角度与亮度

仅支持主方向光。CPU 校验 `mainLightIndex >= 0` 且 `visibleLights[index].lightType == Directional`，结果写入 `_MaskRBRPhaseParameters.w`。`_MainLightPosition.xyz` 为归一化朝向光源的方向；投影 `VP × (dir, 0)` 后经 `ComputeScreenPos` 得到焦点。缺少主光、主光位于背后或正交相机输出零光束；接近侧向时以 0.1 宽度淡出，不提供 Manual 回退。

最终合成像素按 clip/device 深度 0.5 经 inverseVP 重建有限点并去除动态 UV 缩放偏置，得到朝向场景的视线方向。`mu = clamp(dot(viewDir, lightDir), -1, 1)`，相位为 `HG(g, mu) / HG(g, 1)`，峰值归一到 1；角窗 `smoothstep(cos(halfAngle), cos(0.8 × halfAngle), mu)`，外侧 20% 平滑淡出，超出 halfAngle 为零。这是便于调参的峰值归一化角度响应，不是以每球面度计的物理散射强度。

R16 径向纹理只存 `normalizedMaskBlur × focusFade`；Low 升采样后，三档均在最终分辨率计算 `emittedStrength = rayTexture.r × phase × cone`。角度窗不会被低分辨率升采样扩散到窗外。Debug Rays 展示这份最终发光强度的灰度，而不是未经角度响应的 R16 原纹理；保留轮廓外的拖尾，同时限制其角向接收。

合成阶段：`scene.rgb + _MainLightColor.rgb × emittedStrength × saturate(intensity)`，保留 scene alpha。`_MainLightColor` 是 URP 线性颜色，包含真实主光 intensity，不使用场景阈值也不裁白；因真实光照，输出可能为 HDR。`intensity=0` 或 `length=0` 且 Debug=Off 时整个 Feature 跳过。Debug Mask 无需主光即可工作；Debug Rays 在 `length=0` 时仍以单 tap 输出 `Mask × phase × cone × focusFade`（不进入模糊循环）。Debug Mask/Rays 返回标量灰度并保留原 alpha。

Shader 属性：`_MaskRBRSourceMode`、`_MaskRBRRadialParameters`（length, falloff, jitter, 0）、`_MaskRBRPhaseParameters`（g, outerCos, innerCos, available）、`_MaskRBROutputSize`、`_MaskRBRIntensity`、`_MaskRBRDebugMode`；全局贴图仅 objectMask / mask / ray，移除 source 全局。Debug=Off 时 GPU 资源为 R8 遮罩、R16 径向与 RGBA16F 合成。

## 局限

光束允许覆盖其他不透明前景，源遮罩只决定谁发光、不决定谁接收，不提供三维深度排序／体积遮挡。观感仍需在目标场景调参。Preview、Reflection 和 Overlay 相机跳过；Game 与 Scene View 的 Base 相机执行。后续如增加前景保护，应独立设计 receiver 策略。

## 验证入口与状态

专属验证代码在 `Validation~/`，Unity 不自动导入该目录；使用连接到本项目的 unityctl 执行。输出进入当前任务的 `tmp/sessions/`，不保存场景或 Renderer 资产。GPU 数值检查使用线性 float 回读；PNG 只用于观察，不用于反推 HDR 强度。

将下面的目录名改为本次复跑的未使用任务目录。第二条命令会额外生成四张完整 640×360 PNG（baseline、mask、rays、composite，已无 source 抓图）；省略最后一个参数即可只生成数值报告。

```sh
maskrbr_run_dir="tmp/sessions/YYYY-MM-DD-maskrbr-rerun-ID"
unityctl script execute -f Assets/Mine/Shaders/PostProcess/MaskRBR/Validation~/ShaderChecks.cs -t 60 -- "$maskrbr_run_dir/output/shader-results.json"
unityctl script execute -f Assets/Mine/Shaders/PostProcess/MaskRBR/Validation~/PipelineChecks.cs -t 60 -- "$maskrbr_run_dir/output/pipeline-results.json" "$maskrbr_run_dir/output/captures"
```

2026-10-12，Unity 6000.3.14f1 / URP 17 / Metal（Apple M2）：C# 编译通过，主 Shader 三个 Pass 均受支持、当前诊断无错误或警告。v2 验证从功能目录的归档入口实际复跑：

- 47 项 GPU 控制全部通过：三种联合遮罩、三档固定预算与常量增益、横／竖屏解析斜坡、越界零贡献及 Clamp 负控、稳定抖动、缺光／背光／正交、独立视线与 HG／角窗方程、g=0／0.7／0.95、峰值为 1、Y 镜像负控、场景 RGB 无关、主光颜色与总强度线性缩放及 alpha。逐通道容差为 `0.002 × (1 + |expected|)`；R8／R16 只断言存储的 R，RGBA16F 断言全部 RGBA，回读各分量均检查有限值。测试全局状态恢复。
- 32 项隔离真实 URP 管线控制全部通过，28 次渲染请求对应 28 次相机回调：可见／被遮挡物体、天空联合、三档轮廓外拖尾、非居中主光与独立径向／HG 方程、真实主光 RGB 与强度、场景 HDR RGB 无关、参数零端点、角度单调与窄窗、Low 升采样后的窗外零输出、缺光／背光／正交、稳定抖动、renderScale=0.1／2.0 的最终尺寸绑定。渲染错误为零，临时管线、活动场景、原 Renderer 和光照全局设置恢复；没有保存现有场景或资产。

现有场景中多个 Feature 的先后组合、XR 硬件、动态分辨率、全部 MSAA 组合、RenderGraph 实际资源预算与 GPU 耗时尚未验证；性能档数字是配置采样预算，不是实测耗时。观感仍需在目标场景调参。
