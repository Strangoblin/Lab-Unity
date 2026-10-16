---
name: diffuse-weak-temporal-diagnosis
description: 静止镜头/时缓0.5的Diffuse抖动归因：RAY与DDA帧差几乎相同，冻结采样降逾99.9%，共享时域有效；单改depthSigma仅改善9.3%，待对照Specular宽核空间重建。
date: 2026-10-13
---

# DiffuseGI 弱时缓静态抖动

用户确认 [旧 SSR 框架 DDA](2026-10-13-ssr-framework-rebuild.md) 当前目测无误；在继续步进升级前，要求查看 DiffuseGI 的历史相似抖动，并对照之前 SpecularGI 修复。补充现象：**镜头完全静止也明显，Diffuse Temporal Blend=0.5**。

## 当前状态与历史关联

Research，只诊断，不改生产算法。当前 Live Renderer 已由用户切为 SSGI active / MaskRBR inactive，Medium、Diffuse debug，恢复后的原时缓为 0.9；此前计时的 MaskRBR on / SSGI off 是历史配置，不能代替本轮读取。

[历史 Specular 修复](2026-09-21-speculargi-unified-fallback.md) 的宽核 5x5 几何重建仍在 `SpecularGIFilter.hlsl`；运动重投影/前帧眼深拒绝与取消硬颜色/来源拒绝已保留在共享时域。不是该修复被当前步进重构覆盖。Diffuse 使用两个 5-tap H/V 阶段，其原始输入支撑可达 5x5，不能误说只有 10 个原始样本；差异在窄高斯、单 texel 跨度和 depthSigma=0.15，Specular Medium 使用 radius=2、重尾空间权重和等效眼深尺度 0.5。

Diffuse 每像素 6 条 cosine 半球射线，按全射线数平均、miss贡献0，逐帧旋转会重采样不同命中/场景辐亮度；alpha 不作时域来源拒绝。Specular 是不同 BRDF/方向分布，当前 Cubemap 也为空，不能把反射回退或按命中数归一化搬给 Diffuse。

## 受控回读

原场景、当前 SceneView pose，**1284x722** 的临时 Game/Base Renderer/Feature 克隆，原配置不变。32 次预热 + 16 次采样 / 组，8 组；都在同一个 Editor frame 内执行，shader `_Time` 恒定，实际内部采样帧号32..47或冻结为0。不是16个真实时间推进帧或实时FPS测试。

回读生产 `SSGITemporalFilter` 自有持久 Diffuse color/depth RTHandle，线性 HDR，排除后续透明层/Volume覆盖，不读帧末已归池的全局纹理。所有组共813,211个有效表面像素，实际6条射线/48步预算、后端关键字、blend和frame sequence均核对。指标是相邻请求亮度差的表面像素RMS；数字不是主观“抖动百分比”。[完整报告](evidence/2026-10-13-diffuse-weak-temporal/jitter.json)。

| 条件 | 亮度帧差 RMS |
|---|---:|
| DDA，blend=0，正常推进采样 | 0.006842540 |
| DDA，blend=0，冻结采样 | 0.000003948 |
| RAY，blend=0，正常推进采样 | 0.006843860 |
| RAY，blend=0，冻结采样 | 0.000002786 |
| DDA，blend=0.5 | 0.002636304 |
| RAY，blend=0.5 | 0.002635702 |
| DDA，blend=0.9 | 0.000489197 |
| DDA，blend=0.5，仅depthSigma 0.15→0.5 | 0.002390618 |

**结论**：当前受控场景支持逐帧方向/命中/辐亮度重采样噪声为主，RAY/DDA几乎一样；冻结采样降逾99.9%（不完全为零），不是DDA特有卡顿/步进不稳定。推进组的 `_SSGIHistoryValid` 都为1，时缓0.9相对0.5显著降低帧差，证明时域整体确实生效；不能由此声称所有像素的深度confidence恒为1。单独放宽depthSigma仅降约9.3%，不据此直接改默认值。

## 下一步与边界

优先在临时对照中采用 Specular 已验证的较宽几何空间重建，保留 Diffuse 的半球估计量/总射线分母/黑miss语义，先不增加射线数、步进预算或时缓，不把冻结噪声作为修复。该候选尚未实施/证明效果；必须同时检查静止噪声、轮廓漏光/串色及运动拖影，用户人工确认后再采用。当前不重写共享时域，也不继续 HiZ/其它步进升级。

OpenCode `opencode-go/deepseek-v4.1-flash` 一次只读审计已完成，主 agent 对照实测裁决：独立 Diffuse 未请求 Motion 是另一条潜在接线问题，不是当前统一 SSGI 的根因；当前无Cubemap，不能称Specular miss稳定非黑；没有公开HistoryWeight界面不等于无法反射/回读历史；无Motion也不能排除输入动画引发的闪烁。报告在任务 worker/AUDIT.md，未把建议当GPU证据。

临时驱动前两次分别因 CopyFrom 保留 SceneView 类型、Unity Vector4 JSON 自引用失败，不是生产shader/SSGI报错，均未作为成功证据。最终明确 `cameraType=Game`、time数组转原始float后8组通过，原设置/Renderer引用与Active/scene dirty恢复检查通过。Probe.cs是复用现有CameraChecks脚手架的专属诊断输入，已归档用于该版复现，不是新的生产验证权威或退役论文源码。[证据用途与入口](evidence/2026-10-13-diffuse-weak-temporal/README.md)。

SampleScene/PC_Renderer/DiffuseGIFunction/SSGITemporal四个SHA1与开工一致，原Live时缓0.9/DebugDiffuse和Active保持，Edit Mode，未保存资产/场景、未提交/推送。E5 report产生review提案不改policy，隔离recipe/knowledge/deletion契约验证另存任务日志；Editor/bridge保持运行。
