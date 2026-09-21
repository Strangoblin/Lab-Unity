---
name: SpecularGI 统一回退链与时域稳定
description: SSSR 主追踪、SSPR 与 Cubemap 分层回退的实现，以及随机反射闪烁诊断
date: 2026-09-21
---

# SpecularGI 统一回退链与时域稳定

## 完成内容

- 新增 `Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/`，包含 Feature、Shader、Sampling、Trace、Filter 和技术文档。
- 架构由平级 SSPR/SSR/SSSR 改为有优先级的辐射链：镜面或 GGX 采样 → SSR 几何首命中 → SSPR 远景候选 → Cubemap 环境回退。
- SSR 几何结果不包含颜色和 BRDF；SpecularGI 私有 HLSL 负责采样、回退和滤波。本阶段没有把 SSR 裁切文件迁入 `Special/HLSL`。
- 明确禁止 `SampleSH` 充当镜面天空回退；Sky 来源使用显式 Cubemap 和 roughness mip。
- 时域历史按 Camera 隔离，保存颜色与前帧线性眼深双缓冲；无 Motion Vector、分辨率改变、帧间断或相机突变时拒绝历史。
- 增加 `HistoryWeight` 调试视图，用实际权重判断 Temporal 是否工作。

## 闪烁根因

旧 StochasticSSR 默认在 1/4 分辨率进行 trace/resolve，并使用最多 25 个空间样本；其 miss 由 `SampleSH` 提供稳定的非黑颜色，时域也几乎无条件累计。

新实现初版 Medium 使用 1/2 分辨率和 9 点滤波，且曾按来源拒绝样本、使用 3×3 颜色硬钳制。对于每像素单条随机 GGX 射线，这些条件会保留更多独立噪声，并在整片 miss 时把历史钳成黑色。最终调整为：

- Low/Medium/High trace 分辨率为 1/8、1/4、1/2。
- 空间阶段使用 5×5、25 点深度/法线权重重建。
- 来源变化不直接拒绝历史，不对 1 spp 结果做颜色硬钳制。
- Temporal 保留 Motion Vector 和前帧视图空间深度拒绝。

当前场景使用 `Skybox/Procedural`，没有可提取的 Cubemap；Feature 的 Sky Cubemap 为空时，SSR miss 必然为黑色。若需要完整 `SSSR → SSPR → SkyBox` 链，必须显式绑定 Cubemap，或单独实现 Procedural Skybox 到 Cubemap 的捕获更新。

## 验证

- Unity C# 编译通过；SpecularGI Shader 强制导入为 0 error / 0 warning。
- SampleScene 中运行完整 RenderGraph 管线，无 Shader、Metal 或 RenderGraph 错误。
- `HistoryWeight` 实测：Temporal Blend=0.98 时表面区域持续使用历史；黑色区域主要为天空/无表面。
- 用户最终确认视觉效果正确。
- 验证期间未调用 `AssetDatabase.SaveAssets()`；临时 Feature 和调试状态均已恢复。

## 后续边界

- 当前 SSR 后端为固定世界空间首命中；DDA/HiZ 可在 `SpecularGITrace.hlsl` 接口下继续接入。
- 当前使用全局 roughness，尚未接入逐像素材质粗糙度。
- 当前历史纹理为普通二维 RT，未声明 XR 支持。
