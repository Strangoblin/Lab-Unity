---
name: interaction-system
description: 正交交互系统完整搭建 — Manager/Processor 架构 + Verlet 波动方程 + 移动模拟域重投影 + 深度比较输入
date: 2026-07-30
metadata:
  type: project
---

# 交互系统 — 完整搭建

## 架构

```
UniversalInteractionManager           ← 总控（共享 originRT + 正交矩阵）
  └── IUniversalInteractionProcessor  ← 接口（Processor 自管输出 RT）
        └── WaterInteractionProcessor  ← Verlet 波方程实现
```

**关键决策**: RT 管理从 Manager 下放到 Processor。Manager 只管理共享的 `originRT`，每个 Processor 创建/绑定/释放自己的输出 RT。

## 数据流

```
相机 (正交 CustomRenderer) 渲染交互物体 → _InteractionOriginTex (R8→RFloat)
  → Compute Shader (CSWave) 读取 origin + WaterTex + WaterPTex
    → Verlet 积分: h_new = 2h_curr - h_prev + c * Laplacian
    → 物体下压: depth > 0 处 h → -depth
    → 双写: WaterTex = h_new, WaterPTex = h_curr
  → Debug Shader 采样 _InteractionWaterTex (双向热力图: 蓝=凹, 红=峰)
```

## RT 命名约定

- `_InteractionOriginTex` — Manager 管理，相机渲染目标
- `_InteractionWaterTex` — WaterInteractionProcessor 管理，波方程输出
- `_InteractionWaterPTex` — WaterInteractionProcessor 管理，Verlet h_prev

## 关键踩坑

### VP 矩阵不同步
`SyncMatrices()` 手动构造 V/P 矩阵但未赋给相机，导致 shader 采样偏移。
**修复**: 同步 transform + orthoSize 给相机，再从相机回读实际矩阵。

### ClearRT 时序
`ClearRT` 在 `Update()` 开头，但 compute 也在 Update 跑，相机还没渲染 → compute 读到全零。
**修复**: Process() 在 ClearRT 之前，compute 读上一帧相机输出。

### Verlet 双写代替 CopyTexture
`CopyTexture(WaterTex→WaterPTex)` 在 dispatch 前 → h_curr==h_prev → 动量项丢失。
**修复**: compute 内双写 WaterTex(新) + WaterPTex(旧)，无需 CopyTexture。

### LinearEyeDepth 与负 near 不兼容
正交相机 near=-10 经过 GPU 矩阵后是正向深度编码，LinearEyeDepth 按反向 Z 解码 → 值全错。
**修复**: 直接对比原始 depth buffer 值，乘 (far-near) 恢复世界单位。

### CFL 稳定性
物理波速 c 在 dt 波动时打破 CFL 条件 → 格子纹。
**修复**: 回到像素空间系数，归一化到 areaSize=10 基准。

### 移动域重投影方向
`deltaPixels = -worldDelta * scale` 符号反了。
**修复**: 物体 +X → 旧数据在 +X 像素 → deltaPixels 为正。

### 边界能量滞留
RT 边缘硬截断 → 波能量堆积 → 拖尾。
**修复**: smoothstep 边界淡出（8% 边缘 = 20px 渐变带）。

## 模式修改

- Research Mode 不再自动进入 Play Mode（人工观测无法闭环，浪费时间）
- `unity-developer.md` 模式对比表更新
- `modes/research.md` 流程精简为 R1→R2→R3

## 文件清单

| 文件 | 职责 |
|------|------|
| `IInteractionProcessor.cs` | 接口: Initialize + Process + BindGlobalTextures + Release |
| `InteractionManager.cs` | 总控: originRT + 正交相机 + 全局属性 + Follow Target |
| `WaterInteractionProcessor.cs` | 水波 Processor: WaterTex + WaterPTex + 像素重投影 + 波速归一化 |
| `WaterInteraction.compute` | CSWave kernel: Verlet 积分 + 物体下压 + 边界淡出 |
| `InteractionDebug.shader` | 双向热力图可视化 (蓝=凹, 黑=0, 红=峰) |
| `InteractorObject.shader` | 互动物体: DepthOnly + DepthDifference (Cull Front 背面深度比较) |
| `CustomRenderer.cs` | 正交相机渲染管线: DepthOnlyPass + DrawObjectsPass, 深度纹理改名 Custom |
