# SpecularGI — 分层屏幕空间镜面反射

**路径:** `Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/`  
**类型:** FullScreenPass  
**目标管线:** Unity 6 / URP 17 / RenderGraph

统一运行由 [SSGIFeature](../SSGI.md) 在 `AfterRenderingSkybox` 调度；本文以下独立 Feature 管线仍可用于对照和回退。

## 功能概述

SpecularGI 将传统 SSR 作为统一几何求交能力。每条镜面或 GGX 射线首先尝试屏幕空间命中，低置信度区域由零步进 SSPR 补充，剩余部分从显式 Cubemap 获取环境辐亮度。

该效果不使用 `SampleSH`。未配置 Cubemap 时，环境级保持黑色。

## 渲染管线

```text
镜面或 GGX 方向
  → SSR 几何首命中
  → SSPR 远景候选
  → Cubemap 环境回退
  → Spatial Resolve
  → Temporal Resolve
  → Fresnel Composite
```

三级辐亮度权重满足：

```text
screenWeight = screenConfidence
planarWeight = (1 - screenConfidence) × planarConfidence
skyWeight    = (1 - screenConfidence) × (1 - planarConfidence)
```

三者之和为一，回退不会重复增加能量。Trace 输出的 Alpha 保存主导来源编码：屏幕反射为 1，SSPR 为 0.5，Cubemap 为 0。实际辐射仍使用连续三级权重；编码只服务 Debug 和时域来源突变拒绝。

## 参数说明

### Resources

| 参数 | 默认值 | 说明 |
|---|---:|---|
| Shader | None | `PostProcess/SpecularGI` |
| Sky Cubemap | None | 镜面环境回退；按 Roughness 选择 mip |

### Technical

| 参数 | 默认值 | 说明 |
|---|---:|---|
| Max Distance | 50 | SSR 世界空间最大距离 |
| Thickness | 0.05 | 深度命中厚度 |
| Normal Bias | 0.03 | 射线起点法线偏移 |

### Performance

| 档位 | Trace 分辨率 | 步数 |
|---|---:|---:|
| Low | 1/8 | 32 |
| Medium | 1/4 | 64 |
| High | 1/2 | 96 |

### Artistic

| 参数 | 默认值 | 说明 |
|---|---:|---|
| Roughness | 0.25 | 0 附近退化为确定性镜面射线 |
| Intensity | 1 | 最终 Fresnel 合成强度，0 关闭 |
| Planar Strength | 1 | SSPR 候选强度，0 禁用 |
| Planar Threshold | 0.95 | 轴对齐法线启发式阈值 |
| Planar Fade Start/End | 15/50 | SSPR 远景淡入范围 |
| Sky Max Mip | 6 | Cubemap 最大粗糙度 mip |
| Temporal Blend | 0.95 | 有效历史的最大权重 |

## 历史管理

每台 Camera 拥有独立的颜色和线性眼深双缓冲。分辨率改变、帧间隔过大或相机矩阵突变时拒绝历史。时域阶段使用 Motion Vector 和前帧视图空间深度拒绝无效历史；随机射线来源变化不会直接清空历史。空间阶段使用 5×5 几何权重重建。

## 调试

Debug 提供 Screen Source、Planar Source、Sky Source、Trace、Spatial、Temporal 和 History Weight。前三项显示主导来源；History Weight 中白色表示历史正常累积，黑色表示历史被拒绝或当前像素没有表面。

## 已知限制

- 当前从 Feature 的全局 Roughness 生成射线，尚无逐像素材质粗糙度输入。
- 当前 SSR 后端使用固定世界空间步进；DDA 与 HiZ 将作为同一几何接口的后续优化接入。
- SSPR 的轴对齐法线判断是适用性启发式，并不证明几何严格共面；正交相机禁用 SSPR，并直接使用 SSR 或 Cubemap。
- Cubemap 由 Feature 显式指定，暂未直接读取 URP Reflection Probe Atlas。
- 当前历史 RT 为普通二维纹理，尚未声明 XR 支持。
