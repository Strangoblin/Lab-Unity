---
name: ssr-dda-fisheye-w-sign-flip
description: SSR DDA 鱼眼扭曲根因 — 近平面 w 过零导致齐次插值奇异，非 DDA 算法缺陷，是观测坐标系差异
metadata: 
  node_type: memory
  type: project
  originSessionId: 1507405f-145a-45b7-ac12-44eeced28127
  modified: 2026-08-07T10:58:20.952Z
---

# SSR DDA 鱼眼扭曲：根因分析

## 症状

DDA 模式在近距离出现鱼眼扭曲：外圈正常、中圈黑色真空环、内圈环形反向映射。Ray3D 模式无此问题。

## 根因

射线端点 `endCS.w` 在反射方向指向/背离摄像机时趋近零或变负。DDA 用齐次坐标 `K = 1/w` 做线性插值，`w → 0` 时 `K → ±∞` 导致步长爆炸、深度比较失效。

**Why:** 透视投影 `UV = (clip.xy / clip.w) * 0.5 + 0.5`，`endCS.w < 0` 时 `endS` 仍可落在 [0,1]（负负得正），DDA 在合法 UV 两端之间直线插值，但 K 中途穿越 `w=0` 奇异点。Ray3D 在世界空间步进，每步独立投影，`clip.w` 变号时 `S` 自动跳出 [0,1] 触发 `break`，天然保护。

**How to apply:** 同一根射线在不同坐标系下的盲区是互补的——DDA 在中心暴露（鱼眼），Ray3D 在边角暴露（越界）。不能修复一种消除盲区，只能混合：`endCS.w / startCS.w` 比值安全时走 DDA，危险时回退 Ray3D。

## 关联

- [[ssr-dda-ray3d-hybrid-stepping]] — 混合步进方案（待实现）
- [[ssgi-screen-space-architecture]] — SSGI 架构中"反射采样算法 ⟂ 步进策略"的解耦设计
