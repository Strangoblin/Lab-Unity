---
name: screen-trace-library
description: 通用 RAY/DDA/HiZ 几何库与按需 RenderGraph 深度金字塔；Metal GPU 验证完成，真实 SSGI 接线待下一阶段。
date: 2026-10-12
---

# 通用步进库第一阶段

用户批准“预计最后也是使用 DDA，先实现通用库”。只新增 `Assets/Mine/Special` 公共能力；未修改 SSGI、SSSM、Renderer 或场景源文件，未提交/推送。Git 基准 `f9b3ad8`，其他任务的大量脏改动保持。

## 实现与权威入口

- `Assets/Mine/Special/HLSL/RayStepFunction.hlsl`：单位世界射线、距离区间/均匀取样、near/far/视口裁切、透视正确插值、网格区间与深度 slab 求交。
- `Assets/Mine/Special/HLSL/ScreenSpaceTraceFunction.hlsl`：同签名 RAY / DDA / HiZ，返回 UV、原始起点世界距离、正眼深、退出原因与查询计数。无颜色/BRDF/法线偏移/时域策略。
- `Assets/Mine/Special/Rendering/ScreenSpaceTrace/ScreenSpaceDepthPyramid.cs/.compute`：lazy RenderGraph 服务，R32 正眼深 min 金字塔，NPOT floor/ceil 覆盖，天空为有限 far，单张 mip 纹理逐级 Compute。
- [完整契约与复现命令](../../../../Assets/Mine/Special/Rendering/ScreenSpaceTrace/README.md)；验证源码归同模块 `Validation~/`，旧 `SST_Trace` 快照只用作算法回归基线。

## 不要丢失的契约

- DDA 遍历真实深度网格，当前固定单位单元，不提供 stride>1。HiZ 只在能证明整个区间在最近表面之前/全天空时跳过，否则下探至 mip0。缺失 HiZ 可由消费者选 DDA，但 miss / BUDGET 不自动重跑 RAY。
- RAY 保留均匀世界步长和 6 次二分流程；near 前/视口外起点直接拒绝。DDA / HiZ 先裁切，命中距离仍相对原始世界起点。
- 金字塔缓存只存在本次相机录制的 ContextContainer；Reset/Dispose 清空。SourceRevision 是调用方的阶段/内容版本，不会自动检测深度改写。句柄不跨帧/相机/图录制存储。
- 只有 resolved、single-slice Tex2D；服务明确拒绝 MSAA/XR 数组。HLSL 的 TEXTURE2D_X 宏并不能证明整条管线支持 XR。动态有效区通过精确网格尺寸、实际分配尺寸与 UV scale/bias 表达。
- 网格方向分类采用两 ULP 坐标，不增加射线 progress；最外单元以已裁切端点为准，避免 NPOT 缩放舍入导致终点停滞。slab / HiZ skip / DDA 起点拒绝使用一致的小眼深精度容差，处理相切与 raw 转换误差。
- HLSL 带 out 参数的多个查询不能放在三目表达式两侧：验证中实际观察到被另一后端覆盖。改为 if/else 后才分离出 DDA / HiZ 各自结果。

## 验证证据

Unity 6000.3.14f1 / URP 17.3.0 / macOS Metal，Editor 与 bridge 在宿主运行。

- 最终 runner **39 组 / 69,432 个断言通过**；[精选 GPU 结果](evidence/2026-10-12-screen-trace-library/metal-results.json)。独立 CPU 世界距离/像素锥体求交对照 DDA / HiZ 命中、距离、UV、眼深；raw 旧 RAY 的命中、距离、UV 回归通过。
- 6 种尺寸、5 种深度表示；NPOT/1×N/薄单元/深度阶跃/全天空/invalid depth；朝向相机/水平垂直/负方向/零投影/near 与 tMin 裁切；裁剪视口/Y 翻转；0 厚度、无效射线、预算与 missing pyramid。
- reflection-hit / shadow-occlusion 两种 fragment 消费者的三后端输出，以及真实相机回调中 RenderGraph mip 导出、同阶段共享、revision/尺寸失效、重复录制和 MSAA/XR 拒绝通过。
- 负控制故意给 GPU DDA 距离 +0.5，独立断言确实失败：expected 2 / actual 2.499999。[控制与审查说明](evidence/2026-10-12-screen-trace-library/validation.md)。规范检查通过，收尾 Unity error 日志为空。
- `Validation~/run.sh` 已从归档源码复跑，临时 RuntimeFixture 源码/metas 经门禁自动清理。运行输出保留在关联 task session；可复建源码不依赖 staging。

## OpenCode 委派

按用户刚更新的规则，使用 `ocw -m opencode-go/deepseek-v4.1-flash` 在隔离快照内执行两个有限审查，只允许写 REVIEW.md。接口审查完成；较大算法审查仅 Read 后退出、无报告，缩小到 NPOT 归约后一次重试完成。实际共三次调用，未让 worker 写 Assets 或绕过门禁。

主 agent 独立复核：Reset 生命周期与 SourceRevision 为显式调用方契约，不是自动失效服务；整纹理 ReadWrite 是本版有意串行依赖；MSAA/XR 描述符拒绝与 mip SRV/UAV 用真实 GPU / RenderGraph 结果验证。worker 的“任意 mapping 越界靠 clamp”说法不准确，源码越界按天空处理。报告不直接作为正确性证据。

## 下一阶段

先让真实 SSGI 的 SST 入口适配公共库，再启用 DDA，保持 Diffuse 采样分布与 4/6/8 射线数、Specular 回退/置信度/滤波、AO 行为；后端变更需处理历史失效。SSSM 可作为第二个实际效果接线候选。

本次只有库与合成消费者验收，**不声称 SSGI 已切 DDA、旧效果全平台等价或 GPU 已提速**。真实深度绑定/Y 方向、XR、最终画质与总 GPU 时间需后续验证。原计划在 `tmp/sessions/2026-10-12-ray-step-plan-a1/staging/PLAN.md` 标明实施范围与未完成接线。

门禁自省出现 region/新文件落点的 review 提案，未自行改策略；多进程共享 workspace 磁盘状态曾被另一任务覆盖，已有本任务 Special 授权从 MCP 同源状态恢复后再写入，未扩大目录。
