---
name: grass-shadow-casting-abandoned
description: 草放弃投射阴影的完整判据，以及「草自阴影暗区钉在相机上」的真因链与已落地的接收淡出
date: 2026-11-12
---

# 草放弃投射阴影

结论先行：**草只接收主光阴影，不投射。** `GrassRenderFeature` 里的 `GrassShadowPass` 已删除，`README.md` 的「阴影」一节记录了这次裁决。

## 判据链

1. 症状：Game 视图里一块**固定在相机视角上**的暗区，Scene 视图正常。
2. 把草的 `ShadowCaster` 从阴影图里摘掉（`_ShadowAlphaClipOffset = 2`）、与「主光完全不投影」做差值，两者**逐像素一致**——暗区 100% 来自草写入阴影图的那部分。
3. 尝试过的所有旋钮都只改变形状/浓度，不改变「错位」本身：
   - 级联数（1 / 4）
   - `shadowDistance`（12 / 15 / 20 / 50 / 120）
   - `_ShadowCasterOffset`（0 / 0.02 / 0.05 / 0.5）
   - 只画最近一级级联（`DrawCascadeCount`，1 级级联下恒为空转）
   - 接收端距离淡出 `_ShadowReceiveFade`
4. 结论落在写入端而不是参数：`AfterRenderingShadows` + `DrawMeshInstancedIndirect` 追加进 URP 主光阴影图这条路，在 `DrawMeshInstancedIndirect` 的 frustum 剔除（mesh bounds 在原点、实例散布 ±50 m）与 URP 内部 slice 状态之间无法稳定对齐。继续调参只是重复猜测。

## 保留的接收侧

- `GrassWind.shader` 用 `MainLightShadow(TransformWorldToShadowCoord(positionWS), ...)`，不用 `MainLightRealtimeShadow`（后者缺 `BEYOND_SHADOW_FAR` 的距离淡出，超出阴影距离会随机变暗）。
- **不做额外的距离混合**：中途加过 `_ShadowReceiveFade`（2–8 m 淡回受光）来削「逐叶自阴影退化成整片全黑」的等距暗环，用户 2026-11-12 明确不要淡入淡出，已连同 shader 属性、CBUFFER 字段、材质序列化项一起删除。接收就是按阴影图取值。
- `GrassWind.shader` 的 `ShadowCaster` Pass、`_ShadowAlphaClipOffset`、`_ShadowCasterOffset` 保留但已无调用方。

## 测量方法上的两个坑（代价很大，务必记住）

- `unityctl screenshot window Game` 抓的是**窗口画面**，会带缩放/裁切；用它做像素 A/B 会得到假的「0 差异」。改用把主相机渲染进 RenderTexture 再落盘（`tmp/render.cs` 那套）。
- **play mode 下草的风场动画让连续两帧差 75% 像素**，任何 A/B 都会被动画噪声淹没。要做像素级对比必须在编辑态做，且一次调用内完成「改参数 → 渲染 → 还原」。

## 相关

- [unityctl 工具四坑](2026-09-20-unityctl-tool-pitfalls.md)
- [MaskBlur 分层采样暗点](2026-10-09-maskblur-stratified-darkspots.md)
