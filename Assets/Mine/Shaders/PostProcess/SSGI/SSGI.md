# SSGI — AO、DiffuseGI 与 SpecularGI 统一管线

入口为 `SSGIFeature.cs`，Renderer 中只需启用一个 SSGI Feature。现有 AO、DiffuseGI、SpecularGI Feature 保留独立运行能力，供对照和回退；同一 Renderer 不应同时启用独立 Feature 与统一 Feature，否则效果会重复合成。

## 帧内顺序

`AfterRenderingSkybox` 获取不透明场景色、深度、法线和运动向量；三个模块都从同一场景色读取，不从其它模块已合成的颜色再次追踪。

```text
Opaque + Skybox
  ├─ AO: Trace → 双边滤波 → 全分辨率保边 Resolve ─┐
  ├─ DiffuseGI: Gather → 双边滤波 → 全分辨率 Resolve ├→ 共享时域重投影
  └─ SpecularGI: SSR → Cubemap → 空域 ───────┘
              ↓
        SSGI.Composite（写回 cameraColor 一次）
              ↓
        URP 拷贝 _CameraOpaqueTexture → 透明物体
```

RenderGraph 在统一 Feature 内直接传递三张 `TextureHandle`，合成 Pass 对每张实际启用的纹理声明读依赖。`SSGITemporalFilter` 每相机只保留一对眼深度历史，每个启用的模块各保留一对颜色历史；三路复用同一个 Shader 的运动向量重投影、深度拒绝与失效判定。没有 Cubemap 时环境回退为黑色，不使用 `SampleSH`。

## 时域历史

统一入口在三路空间结果之后调用 `SSGITemporalFilter.Resolve`，将结果直接写入各自历史纹理的另一侧，再写一次全分辨率深度历史。当前档位使用 AO 0.85、DiffuseGI 0.9、SpecularGI 0.5 的最大历史权重；各值可单独调节至 0（关闭历史混合）。相机断帧、尺寸变化、明显矩阵跳变、运动向量缺失或前帧深度不匹配时，本帧直接采用空间结果。模块关闭再开启时也会检查其颜色历史是否仍在正确的双缓冲侧。

AO 和 DiffuseGI 只在有运动向量、公共时域 Shader 可用时推进逐帧采样旋转；独立 Feature 需要把 `SSGITemporal.shader` 指定给自己的 `temporalShader` 才启用历史。SpecularGI 的独立入口同样使用公共时域阶段。历史颜色为 AO 的 RHalf 与两路 GI 的 ARGBHalf，深度为 RHalf；透明几何仍不参与追踪。

## 合成语义

AO 输出可见度 `V`，DiffuseGI 输出尚未乘接收反照率和强度的间接辐亮度近似，SpecularGI 输出空间与时间滤波后的反射辐亮度；屏幕反射低置信度时直接回退到显式 Cubemap，不再计算假平面反射。统一 Shader 按以下顺序合成：

```text
AOFactor = 1 - saturate((1 - V) × AOIntensity)
Color    = Scene × lerp(1, AOFactor, SceneAO)
Color   += DiffuseGI × ReceiverAlbedo × DiffuseIntensity × AOFactor
Color    = lerp(Color, SpecularGI, saturate(SpecularIntensity × Fresnel))
```

`SceneAO` 默认 1，延续原 AO 对整个场景色的近似遮蔽；设为 0 可保留原场景色，只让 AO 调制新增 DiffuseGI。Forward 后处理无法拆出场景色中的直接光与环境光，因此这里不是严格的“仅遮蔽间接光”。SpecularGI 沿用原有 Fresnel 混合，不把反射辐亮度当成无条件加法项。

## Renderer 配置与调试

`Assets/Settings/PC_Renderer.asset` 已启用统一 SSGI，AO 和 DiffuseGI 使用各自 Medium 档；SpecularGI 参数从原 Feature 复制，旧 SpecularGI Feature 关闭但保留。统一 Debug 提供 AO、DiffuseGI、SpecularGI 三种分量视图；各子模块原有 Debug 字段只在独立 Feature 中使用。

统一 Inspector 的「Technical · Modules」只保留各模块的 Shader、追踪几何与滤波参数；SpecularGI 的 Cubemap 与 Sky Max Mip 也在这里。三个模块的性能档位集中在 Performance，强度集中在 Intensity，历史权重集中在 Temporal，距离衰减、接收反照率、粗糙度与 Scene AO 集中在 Artistic。每路 Intensity = 0 即跳过该路追踪和历史解析，无额外启用开关。Debug 是统一合成的分量视图。独立 AO、DiffuseGI、SpecularGI Feature 各自保留 Settings（技术参数）和 Controls（性能、强度、时缓、艺术与独立调试），供单模块对照。\n\n性能档位决定固定采样预算和工作分辨率；AO 世界半径只控制遮蔽覆盖，不改变循环次数。

## 验证与限制

2026-09-28 初次集成在 SampleScene 的 Main Camera 上，以同一个 256×144 离屏目标逐项切换模块，较全关基线的采样像素差异为 AO 1208、DiffuseGI 146、SpecularGI 1778、全开 1808；四个 Shader 强制导入均为 0 消息，Play Mode 运行无错误日志。该测试证明三路已接入真实管线，不代替 Game View 的主观画质检查。

公共时域重构后四个 Shader 强制导入均为 0 消息，Play Mode 连续运行时相机历史推进到第 3 帧，三路颜色历史均已写入，且无错误日志。此检查不代替 Game View 的闪烁、拖影和快速镜头运动观测。SpecularGI 的追踪/空间阶段仍使用 Unsafe Pass；后续先用 Profiler 衡量中间 RT 与该 Pass 的开销。透明物体在合成后绘制，因此不会作为屏幕追踪的命中几何。
