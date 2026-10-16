---
name: starrynight-angle-field
description: 2026-08-27 — 多中心角场拓扑与融合方案（max/圆均值/Voronoi/模式级混合）+ StarryNight 三文件拆分
date: 2026-08-27
metadata:
  type: project
---

# StarryNight 多中心角场：拓扑真相与融合方案

## 核心结论（可复用知识）

**带 2+ 个中心的标量角场必然断裂**（指数定理：N 源 → 至少 N-1 鞍点，鞍点处方向无定义）。任何"多中心融合成一个角度"的方案都躲不掉，只能选断线位置或掩蔽。

**连续性配方 = 圆周值场 + 周期消费**：
- 场必须是圆周值（模 2π 的量，如单位向量求和），在 [0,1] 区间坐标上操作（如 max）会把圆周降级为区间 → 每源回绕处撕裂
- 消费侧必须周期化（cos/sin、frac、整数倍切分）→ atan2 的 -π/+π 切缝不可见
- 单中心场无鞍点 → 天然连续（移植版 StarryNight 即此）

## 四种方案对比

| 方案 | 组合方式 | 断裂位置 | 适用 |
|------|---------|---------|------|
| max（原版） | 区间值取最大 | 每源的 -X 回绕处 | ✗ 弃用 |
| 圆均值（向量和） | 圆周值 ✓ | 两源连线（sum→0 鞍点），可 lerp 收敛掩蔽 | 统一漩涡系统，星少 |
| Voronoi + 2NN 软化（**现用**） | 最近+次近源，边界带内单位向量圆插值 | 硬切线塌缩为每对源之间单个鞍点（v≈0 测度零） | N 星通用，O(N) 距离成本 |
| 模式级混合 | 每源独立单中心图案 + 距离衰减混合输出 | **无**（组合发生在输出层） | N 星通用，代价 N 倍图案计算 |

**2026-08-27 决策**：方案 A（模式级混合）因「每星一次图案计算」性能开销大，暂缓。采用 **Voronoi + 2NN 软化**：记录最近+次近源，边界带（半宽 `_AngleBlend`，UV 单位，默认 0.08）内 `t = saturate(0.5 + 0.5*(d1-d2)/band)`，单位向量加权 `v = uA*(1-t) + uB*t` → atan2（弧线插值，保持圆周值）；远离边界退化为纯最近源，单源自动退化。成本仅多记一个次近 + 一次加权。注意 `lerp(float3, float4, float)` 混合尺寸会触发 Metal 隐式截断警告，需 `.rgb` 对齐。

## 径向 SDF 无融合问题

`min(距离场)` 是连续函数之 min，依然连续。融合问题只属于角场（圆周值不能"平均/取大"而不切圆）。

## StarryNight 三文件拆分（2026-08-27）

- `TheStarryNight.shader` — 入口：参数定义（define/CBUFFER）→ include → 结构体/Vert/Frag，Voronoi 角场
- `TheStarryNightSDF.hlsl` — SDF 库（径向 min + Voronoi 角场 + ComputeCell + 配色），依赖调用方先定义 `SDF_POINT_COUNT`/`_Points`/CBUFFER 参数
- `TheStarryNightPort.hlsl` — 移植版功能库（单中心连续方案，备存对比）

## 实坑：include 线性展开

自定义 include 内引用的全局必须在该 include **之前**声明，否则 `undeclared identifier`（SDF_POINT_COUNT 报错）。已补入 [[shader-development]] 规则 Include 顺序节。

## 调试手法

- 周向场色轮可视化：`0.5 + 0.5 * cos(angle + float3(0, 2.094, 4.189))`——cos 偶函数，-π/+π 同色，连续性一眼可见
- 门禁 v2（2026-08-27 起）：链统一 `[g_entry, g_knowledge]`，write_gated 内容规范检查
