# SSGI Unity 环境反射与强度范围

## 需求与实现

- 用户要求补齐 Specular 天空、取消自定义天空，并确认 AO / Diffuse / Specular 三路统一为 0–10。工作区限定 `Assets/Mine/Shaders/PostProcess/SSGI`；基准 HEAD `438a9ea`，保留其他任务的既有脏改动。
- `SpecularGISampling.hlsl` 直接读取 URP 每相机的 `_GlossyEnvironmentCubeMap`，用 `_GlossyEnvironmentCubeMap_HDR` 经 `DecodeHDREnvironment` 解码；粗糙度通过 URP `PerceptualRoughnessToMipmapLevel` 选 mip。Unity 自己提供 `ReflectionProbe.defaultTexture`，不额外捕获天空，不使用 `SampleSH`。
- 移除 Specular 的 `skyCubemap`、`skyMaxMip` 及自定义 Shader 资源绑定；旧 Renderer 序列化值被忽略。SSR 命中与环境的置信度混合不变。
- `HistoryKey` 记录默认环境纹理 ID、HDR 解码向量与 `uint` 精确更新计数；变化只失效 Specular 历史，不释放历史 RT，也不影响 AO / Diffuse。`uint` 避免用 float 表达 2²⁴ 以上的相邻更新计数时丢精度。Unity 的 `Texture.updateCount` 不保证覆盖所有原地 GPU 更新。
- 统一入口和三个独立 Feature 的 intensity 均可设 0–10，运行时也钳制到该区间。Specular 仍为 `saturate(Intensity × Fresnel)` 混合，AO 遮蔽仍饱和到 1，Diffuse 保持线性加法；默认 1 的合成公式不变。

## 验证

- Unity 6000.3.14f1 / Apple M2 / Metal：完整相机矩阵 71 组 / 1,153,056 断言通过；五个负控制（DDA 距离、历史、空间、缺少 Fine、黑色环境）均检测到预期缺陷。
- 正交平面短射线强制 miss，生产 Trace 与独立 URP HDR 解码参考在有效像素上对照；注入黑色输出被检出。参考要求当前默认环境非黑，不能据此证明所有场景都非黑或动态天空实时同步。
- 独立 AO / Diffuse / Specular 与统一入口检查 10 生效、20 钳制到 10；独立 Specular 实际渲染 1→10 像素有差异。环境纹理 ID、HDR 解码和更新计数的历史失效，以及相邻 16,777,216 / 16,777,217 的精确比较均验证。
- 当前 `RenderScene` Main Camera 复制视角，临时 SSGI-only Renderer、Medium/DDA、Roughness 0、关闭克隆时域，654×393 全帧：Specular 1 与 10 有 101,099 像素 RGB 最大通道差 > 0.001，最大差 0.9169；两张完整 PNG 已检查。高强度掠射区域容易达到混合饱和；截图不代表原 Renderer 全功能、连续运动或用户审美验收。
- Editor 编译、Shader 实际变体、结构规范与 diff whitespace 检查通过。自有 RuntimeFixture/meta 清理；原 SSGI 内存 Active=True、三路 intensity=1、Specular roughness=0 / maxDistance=50 保持；未保存场景/Renderer、未提交推送。

## 限制与证据

Unity 默认环境反射是缓存，Skybox Material 动态修改后不保证立即同步；当前实现不解析特定 Skybox Shader、不创建 Reflection Probe，也不保证即时跟随外部材质变化。环境更新时机应在实际链路单独验证。使用的是默认环境，不是局部 Reflection Probe Atlas；XR 和其他图形 API 未验。

报告、五个负控制、原始实景 PNG、必要源码快照与 SHA256 清单位于 [evidence/2026-10-27-ssgi-unity-environment](evidence/2026-10-27-ssgi-unity-environment/)。`input/SSGI` 是实景截图版本，`input/final-SSGI` 是后续精确 uint 历史签名的三个差异文件；截图时克隆时域关闭，因此该签名修改不改变截图中的合成公式。场景和默认环境是 live 输入而非完整冻结项目，无法承诺重现相同绝对数值。
