---
name: starrynight-wheat-bands
description: 2026-09-02 — TheStarryNight 收尾：麦田多层横带架构定稿 + SKY_/MOUNT_/WHEAT_ 三分类命名 + mat 键迁移
date: 2026-09-02
metadata:
  type: project
---

# StarryNight 麦田前景：架构定稿与三分类命名收尾

## 麦田最终架构（设计决策，勿回退）
- 前景 = 3 条全宽长横带，行号裁剪区间 WHEAT_ROWS[i] 交叠（i 越大越靠前覆盖）
- 无坐标旋转（rotated-crop 视觉不佳被弃用）；带间差异 = 列波相位 WHEAT_ROW_OFFSET[i] 错开（与山水 MOUNT_ROW_OFFSET 同构）
- 行纹循环位移：`frac(列号 × WHEAT_V_STEP) / 行数` —— 超一行格回卷，位移恒 < 1 行格 → 砖形错位、不整体倾斜/旋转颜色（曾用无界 shear，出现"只旋转了颜色"）
- "行门像素漏出" = 有意保留：错动相位进入行门判定 → 带缘随列漏 ≤1 行（5 列循环）；用户明确要求保留，勿再"修复"
- 顶缘参差门无 time（静止）：WHEAT_RAG_AMP × 行数 × (0.5 + 0.5 sin(distU × WHEAT_RAG_FREQ × 2π))
- 山水/麦田同构镜像：MOUNT_ROWS↔WHEAT_ROWS（层裁剪区间）、MOUNT_ROW_OFFSET↔WHEAT_ROW_OFFSET（行波相位）、LAYER_COUNT = 3

## 命名三分类（2026-09-02 定稿，旧名已全清）
- SKY_ 星空：SKY_RING_SCALE / SKY_POINT_COUNT / SKY_POINTS / SKY_POINT_RADII / SKY_ANGLE_BLEND；属性 _SkyColorA/B/C（原 _BaseColorSkyA/B/C）
- MOUNT_ 山水：MOUNT_ROW_SCALE / MOUNT_COL_SCALE / MOUNT_COL_STEP / MOUNT_ROWS / MOUNT_ROW_OFFSET / MOUNT_LAYER_COUNT；属性 _MountColorA/B（原 _BaseColorRowA/B）
- WHEAT_ 麦田：WHEAT_SCALE / WHEAT_COL_SCALE / WHEAT_ROWS / WHEAT_ROW_OFFSET / WHEAT_LAYER_COUNT / WHEAT_RAG_AMP / WHEAT_RAG_FREQ / WHEAT_V_STEP；属性 _WheatColorA/B（原 _BaseColorLineA/B）
- 共享（无前缀）：ROW_WAVE_AMP / ROW_WAVE_FREQ（山水行带波；麦田转置 = 竖笔触扭摆）；属性 _SectorCount / _Softness / _Random / _Speed

要点：
- _Random 是全局色彩扰动，不是星空专属——ComputeRowColor（山水+麦田）与 ComputeSkyColor 共用
- _Speed 全局动画速度；_SectorCount 全图案密度乘子（扇区数 + 山水/麦田格点数）
- SDF 库依赖声明已纠正：库函数只引用 SKY_POINT*/SKY_ANGLE_BLEND / ROW_WAVE_* / MOUNT_COL_STEP / _Random / 天空色；MOUNT_ROWS 等区间数组仅 shader frag 使用
- .mat 顺手清理旧架构残留键（_RingCount/_RowWave/_Rows/_AngleBlend 等 17 个）

## 保留值（用户手调）
- WHEAT_ROW_OFFSET = {0, 2, 4}；带区间 {0.1-0.4, 0.2-0.5, 0.3-0.6}
- 材质调色全部保留：WheatA(0.877,0.860,0.302)、MountA(0,0.787,0.536)、SkyA(1,0.779,0.288)、_Speed=0.2、_Random=0.18

## 验证
- 强制 reimport（ImportAssetOptions.ForceUpdate）+ ShaderUtil.GetShaderMessages → []（clean）；旧名 grep 无裸引用
