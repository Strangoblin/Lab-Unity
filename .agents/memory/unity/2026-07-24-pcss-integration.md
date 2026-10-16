---
name: pcss-shadow-integration
description: PCSS 软阴影 — 4-cascade shadow mapping + blocker search + penumbra + variable PCF
date: 2026-07-24
metadata:
  type: project
  branch: unity6
---

# PCSS 软阴影开发

## 技术方案

- **PSSM 4-cascade split** → tiled atlas → blocker search → penumbra estimation → variable PCF
- **Shader 组织**：`Assets/Mine/Shaders/PostProcess/PCSS/`
  - `PCSS.compute` — 主 compute shader（mask + blocker search + penumbra + PCF）
  - `PCSSFunction.hlsl` — 工具函数与声明
  - `PCSSTemplate.shader` — 阴影投射 shader 模板
  - `PCSSFeature.cs` — C# RenderGraph Feature
- **后处理**：Unity 6 Blitter API（非旧版 Graphics.Blit），纹理绑定 `_MainTex` → `_BlitTexture`

## 关键架构决策

- 使用 CustomShadowCaster pass 写入阴影深度（非 Unity 内置 ShadowCaster）
- Shadow bias 在 vertex shader 中计算（_ShadowDepthBias + _ShadowNormalBias）
- 屏幕空间 blocker search + penumbra 估算（PCSS 标准算法）

## 当前状态

PCSS 最终集成调试中。
