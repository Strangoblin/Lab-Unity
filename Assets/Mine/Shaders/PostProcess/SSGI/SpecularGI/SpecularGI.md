# SpecularGI — 屏幕空间镜面反射与环境回退

**路径:** `Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/`  
**类型:** FullScreenPass  
**目标管线:** Unity 6 / URP 17 / RenderGraph

统一运行由 [SSGIFeature](../SSGI.md) 在 `AfterRenderingSkybox` 调度；独立 Feature 同样通过共享的 `SSGITemporalFilter` 完成时域重投影，需绑定 `SSGITemporal.shader`。

## 功能概述

镜面或 GGX 射线先尝试 SSR 几何命中，再按屏幕命中的置信度与 Unity 默认环境反射混合。URP 每相机绑定 `ReflectionProbe.defaultTexture` 和 HDR 解码参数到 `_GlossyEnvironmentCubeMap` / `_GlossyEnvironmentCubeMap_HDR`，使用 URP 的 `DecodeHDREnvironment` 与粗糙度 mip 映射。无需给 SSGI 单独配置或生成 Cubemap。原 SSPR/SSPM 假平面候选已移除，效果不使用 `SampleSH`。

```text
镜面或 GGX 方向
  → SSR 深度采样穿越
  → Unity 默认环境反射图补足未命中与低置信度区域
  → Spatial Resolve
  → SSGITemporalFilter
  → Fresnel Composite
```

```text
screenWeight = screenConfidence
skyWeight    = 1 - screenConfidence
radiance     = screenRadiance × screenWeight + skyRadiance × skyWeight
```

Trace 的 Alpha 保存 `screenConfidence`，供 Screen/Sky Source 调试；时域阶段正常混合 Alpha，但合成只使用 RGB。未命中、边缘淡出或背面命中都会增加环境权重。默认环境反射图由 Unity 管理，跟随场景 Lighting 的环境反射源；它是缓存，Skybox Material 动态变化后不保证立即更新。自定义 Cubemap / Sky Max Mip 字段已移除，旧序列化值不会被使用。

## 参数

统一 SSGI 面板中，Shader、追踪几何和 Roughness 属于 SpecularGI Settings；Performance、Intensity、Temporal 位于统一顶层。Roughness 是尚无逐像素材质输入时的全局近似，不在独立 Controls 中重复配置。

| 分组 | 参数 | 默认值 | 说明 |
|---|---|---:|---|
| Resources | Shader | None | `PostProcess/SpecularGI` |
| Resources | Temporal Shader | None | 共享 `PostProcess/SSGITemporal`，独立 Feature 需绑定 |
| Trace | Mode | DDA | DDA / Ray / 实验性 HiZ；统一入口由两路共享 |
| Trace | Depth Pyramid Shader | None | HiZ 需要绑定共享的 `ScreenSpaceDepthPyramid.compute` |
| Technical | Max Distance | 50 | SSR 世界空间最大距离 |
| Technical | Thickness | 0.05 | 深度命中厚度 |
| Technical | Normal Bias | 0.03 | 射线起点法线偏移 |
| Performance | Performance | Medium | Trace 分辨率与步数档位 |
| Technical · Material Approximation | Roughness | 0.1 | 0 附近退化为确定性镜面射线；独立 Feature 当前资产为 0.1 |
| Artistic | Intensity | 1 | 0–10 的最终 Fresnel 合成强度；混合权重仍饱和到 1 |
| Temporal | Temporal Blend | 0.95 | 有效历史的最大权重 |

| 档位 | Trace 分辨率 | RAY/DDA 样本数 |
|---|---:|---:|
| Low | 1/8 | 32 |
| Medium | 1/4 | 64 |
| High | 1/2 | 96 |

Trace 宽高按各档除数向上取整；High 的 Diffuse 使用 1/3，Specular 和 AO 使用 1/2。

几何复用 `Special/HLSL/RayStepFunction.hlsl` / `ScreenSpaceTraceFunction.hlsl`，经 SST 壳调用。DDA 按固定投影样本查询有效相机深度，不逐格；HiZ 复用同帧共享深度金字塔与像素步幅追踪。原 GGX/镜面生成、法线/边缘置信度、Cubemap、滤波与 Fresnel 保留。未命中继续用环境回退，不重跑 RAY。不同后端的采样位置不同，均可能漏掉薄前景；合同见 [统一说明](../SSGI.md#几何追踪后端)。

## 历史与调试

每台 Camera 有独立的颜色历史；统一 SSGI 中与 AO、DiffuseGI 共用每相机眼深历史。分辨率变化、帧间断、相机矩阵突变或深度不匹配时拒绝历史。空间阶段在 Trace 分辨率使用 5×5 几何权重滤波，再以四点几何引导上采样到全分辨率供时间累积。

空间循环提炼到家族私有 `../SSGISpatialFilter.hlsl`，DiffuseGI 也使用这一重建核心；Specular 的半径/固定眼深尺度/法线权重/输入采样/fallback 保持，wrapper 继续保留中心 alpha，四点上采样不改。共享核心不包含 BRDF、Cubemap 或时域拒绝策略。验证使用另存的 `Validation~/BaselineSpecularFilter.hlsl` 对照提炼前真实滤波，而非让旧基线引用当前 helper 来自证等价。

几何/质量、Roughness、默认环境纹理引用、`updateCount` 或 HDR 解码参数变化只失效 Specular 旧颜色资格，不清空/重新分配历史；最终强度与非零 Temporal Blend 不失效。统一 Trace 模式切换同时影响 Diffuse 与 Specular，各自拒绝旧历史。`updateCount` 只反映 Unity 暴露的更新计数，不能保证检测每一种原地 GPU 内容更新。逐帧签名与独立/统一调用路径相同。原 RAY 三档回归最大 RGB 差异为 0；当前增量与历史逐格的验证区别见 [统一说明](../SSGI.md)。

Debug 提供 Screen Source、Sky Source、Trace、Spatial 和 Temporal。Screen/Sky Source 显示两路权重。旧序列化枚举数值保持稳定，已移除的 Planar Source 数值 2 留空。

## 已知限制

- 使用 Feature 的全局 Roughness，尚无逐像素材质粗糙度输入。
- HiZ 为实验模式，可能改变屏幕反射命中；不支持的深度布局会回退 DDA，动态画质仍需人工观察。
- 使用 Unity 默认环境反射图，暂未读取局部 Reflection Probe Atlas；动态天空与默认图的更新时机由 Unity 管理，不保证逐帧一致。
- 历史 RT 为普通二维纹理，尚未验证 XR 与动态分辨率。
