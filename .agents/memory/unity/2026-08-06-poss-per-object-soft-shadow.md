---
name: poss-per-object-soft-shadow
description: POSS 逐物体软阴影 — Shadow Atlas + Compute Shader 屏幕空间解算 + PCF 软边缘
date: 2026-08-06
metadata:
  type: project
---

# POSS — Per-Object Soft Shadow

## 做了什么

在现有 PCSS 4-Cascade CSM 基础上，新增逐物体软阴影系统 POSS。为标记了 `POSSComponent` 的动态物体渲染独立的高精度软阴影，与 CSM 共存。

## 架构

```
POSSComponent (标记组件，OnEnable注册/OnDisable注销)
  └── POSSManager (场景单例，维护 List<POSSComponent>)
        └── POSSFeature (RendererFeature, [ExecuteAlways])
              ├── POSSSShadowCasterPass (AfterRenderingShadows)
              │     └── 每物体光空间正交投影 → RFloat Atlas Tile
              └── POSSResolvePass (AfterRenderingTransparents)
                    └── Compute Shader 屏幕空间解算 + PCF → R8 _POSS_ShadowTexture
```

## 关键设计决策

1. **Shadow Atlas + Tile Grid**：单张 RFloat 纹理，Grid 布局，每个物体一个 Tile（256²），最多 16 物体
2. **专用 ShadowCaster 材质**：`Hidden/POSS/ShadowCaster`，不依赖物体自身材质。`material.FindPass()` 方案被废弃——默认材质没有 shadow caster pass
3. **PCF 而非 Blur/PCSS**：Unity 风格固定半径 Poisson disk（2px / 4 taps），不做 blocker search 和 variable penumbra
4. **组件极简化**：`POSSComponent` 无任何可配参数，仅标记+注册。投影距离固定 10m 硬编码
5. **深度比较**：Atlas 经 scale-bias 后 `[0,1]`，reversed-Z `occluder > receiver - bias` 判 lit。Clear=black (0.0=far=无遮挡)

## 产物

`Assets/Mine/Shaders/PostProcess/POSS/`
- `POSS.compute` — 屏幕空间解算 + PCF
- `POSSFeature.cs` — RendererFeature（Caster + Resolve Pass）
- `POSSComponent.cs` — 标记组件（极简，仅注册/注销）
- `POSSManager.cs` — 全局管理器
- `POSSShadowCaster.shader` — 光空间深度写入
- `POSSTemplate.shader` — 场景物体模板
- `POSS.md` — 文档

## 与 PCSS 的关系

互补：CSM 处理全局阴影，POSS 处理特定动态物体近距离高精度阴影。POSS 物体通过 `shadowCastingMode=Off` + `staticShadowCaster=true` 从 CSM 剔除。

**Why:** 静态光照场景中少数动态角色需要高质量阴影，全场景 CSM 浪费 GPU 带宽。
**How to apply:** 给动态物体挂 `POSSComponent`，Renderer 加 `POSSFeature`，接收面 shader 采样 `_POSS_ShadowTexture` 并用 `min(csmShadow, possShadow)` 合并。
