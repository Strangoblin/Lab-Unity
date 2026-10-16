---
name: 2026-08-04-water-caustics-screen-independent
description: 水体焦散屏幕无关化 — ddx/ddy 基底分析 + 常数 sceneDet + 世界空间 corrDet 差分
date: 2026-08-04
---

# 水体焦散屏幕无关化（Water.shader ComputeCaustics）

## 背景

FFT 水体焦散最初用 ddx/ddy 计算 Jacobian（`sceneDet = |cross(ddx(scenePosWS), ddy(scenePosWS))|`、`corrDet` 同样方式）。缺陷：
1. **格状伪影** — ddx/ddy 屏幕空间导数导致像素块状感
2. **随视距明暗漂移** — 相机拉近变暗、拉远变亮、绕视变亮

## 核心洞察：ddx/ddy 是屏幕基底

`ddx(scenePosWS)` 读作"场景位置随**屏幕坐标**的变化率"——世界空间输入 ≠ 世界空间导数：

```
sceneDet = |cross(ddx, ddy)| = 屏幕上 1 个像素在场景表面上的世界面积
```

天然含相机因子：像素面积 ∝ **d²**（视距）+ **1/cosθ**（掠射角）。物理正确的平行光焦散 Jacobian 只由水面几何 + 光线方向决定，**与相机无关**——相机因子全部是污染。

## 最终方案（用户验证通过）

```hlsl
float eps = _CausticsScale * (1 + sceneDepDf * 0.1);   // 深度自适应步长
float3 corrDDX = DDX_CorrectHit(...);                    // 世界空间 eps 差分 (±eps 采法线→折射→命中)
float3 corrDDY = DDY_CorrectHit(...);
float  corrDet = max(length(cross(corrDDX, corrDDY)), 1e-4);

float intensity = 0.0001 * _CausticsIntensity / corrDet;  // sceneDet 常数化
float confidence = dot(surfaceNor, correctNor) * 0.5 + 0.5;
intensity *= confidence;
intensity *= dot(mainLitDir, sceneNorWS) * 0.5 + 0.5 * sceneDepDf;  // 光线方向 + 深度 mask
```

- **sceneDet 取常数 0.0001**：平面水底在世界空间差分下 Jacobian 恒等于 1（场景→水面映射是纯平移），`0.0001` 是标定后的等效值。测试确认 0.0001 正好，亮度不再随视距变
- **corrDet 保持世界空间**：完全捕捉水面曲率（±eps 处法线差 → 折射方向差 → 命中点分离）

## 已知局限（未来水底非平面时）

常数 sceneDet 丢失水底**坡度适配**（原 1/cosθ 摊薄补偿）。现有计算能适配：
- ✅ 水深（scenePosWS/scDist 真实采样）
- ✅ 水面曲率（corrDet 差分）
- ❌ 水底坡度/曲率（hitP/hitN 锚定同一 scenePosWS，等价于"过锚点的水平面"）

恢复方案：在 ±eps 世界偏移处**重投影回屏幕 UV 采样真实深度**重建世界空间 sceneDet（既有坡度适配又无相机项）。

## 关键函数

- `DDX_CorrectHit`/`DDY_CorrectHit`（Water.shader ~220-252）：±eps 世界空间差分，替代 ddx(correctHit)/ddy(correctHit)
- `ComputeCaustics`（~257-284）：Snell 折射 Jacobian，最终返回纯量 intensity（色散 grad/tint 已移除）

## 教训

**混合基底必然相机相关**：分子（屏幕基底）× 分母（世界基底）→ 相机因子约不掉。要屏幕无关，所有项必须同基底。见 rules/shader-development.md。
