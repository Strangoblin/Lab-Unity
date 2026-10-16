---
name: ssr-framework-rebuild
description: 通用 DDA/HiZ 回归旧 SSR 增量框架，SSGI 预算与 RAY 对齐；Metal 库/相机正负控制与临时 SSGI-only 同步计时通过，论文逐格版仅留文档参考。
date: 2026-10-13
---

# 通用步进库：旧 SSR 框架重构

## 决策与范围

用户认为旧 SSR 的 RAY 基础及 DDA/HiZ 升级已验证可行，批准以该框架重构公共库；论文参考逐格/slab 源码退役，只留文档参考，未来按需求逐步升级。承接 [逐格库](2026-10-12-screen-trace-library.md)、[SSGI 接线](2026-10-12-ssgi-dda-integration.md) 与 [卡顿排查](2026-10-13-ssgi-dda-wall-timing.md)，这些文件保持历史快照，不能作为当前算法/预算合同。

- 原地改 `RayStepFunction.hlsl` / `ScreenSpaceTraceFunction.hlsl`，不改 metadata 或三个公共 Trace 入口；删除原文件内逐格/slab 算法，不保留并行实现/源备份。RAY 保留现有 SSGI 世界距离取样与 6 次二分，不复活历史不安全 RAY。
- DDA 预计算屏幕位置、`1/w`、`eyeDepth/w`、`distance/w` 增量，循环加法；采样穿越后有界二分。末样本精确覆盖裁切片段终点，不恢复旧 `_StepSize=0.2` 缩短覆盖。
- HiZ 复用增量，以 `2^mip` 放大步幅，疑似穿透回退/降级；已有正眼深 min 金字塔服务保留。粗层仅提示降级，不保证完整候选区扫描或真实首命中。SSGI 暂不接生产 HiZ。
- 保留 near/far/视口裁切、正 w、单位世界射线与原起点距离语义，几何/着色分离。基础采样覆盖完整可见参数范围，但可能跳过像素/薄前景；不是逐格版的等画质优化。
- SSGI 默认仍 DDA。Diffuse RAY/DDA 为 24/48/64，Specular 为 32/64/96；删除 DDA 专属预算覆盖。不改序列化后端 enum/字段、AO、方向采样、BRDF、空间/时域滤波与合成。
- DDA 结束无命中是 MISS/LEFT_VIEW，不返回 BUDGET；HiZ 回退仍可 BUDGET。DDA/HiZ 查询不超过 `maxSteps+refinementSteps+3`，不恢复嵌套扫描或自动 RAY 重跑。

权威合同、退役设计摘要与文献链接在 [公共库 README](../../../../Assets/Mine/Special/Rendering/ScreenSpaceTrace/README.md)，实际效果接线见 [SSGI.md](../../../../Assets/Mine/Shaders/PostProcess/SSGI/SSGI.md)。

## 修复与验收

Production，沿用已批准 Special/SSGI 工作区和既有门禁状态，所有 Assets/Mine 写入走 gate_cli；未 reset 生产门禁，也未自行批准扩展。Git 基准 `f9b3ad8`，存量巨大 dirty tree 保持，本轮未提交/推送。

两个数值问题在验证中修复：raw 深度编码的约 1e-6 误差会让表面起点误判成前方，DDA/HiZ 起点增加眼深编码余量，RAY 精确比较不变；二分必须保留增量实际取样的完整 low/high 状态，不能从累加 progress 重投影 high，否则像素边界舍入可让已观察到的穿越丢失。

Unity 6000.3.14f1 / URP 17.3 / Apple M2 / Metal：

- [库正控](evidence/2026-10-13-ssr-framework-rebuild/library-results.json)：39 组 / **89,095 断言**；CPU 独立世界坐标裁切/固定采样/二分及 HiZ 回退 oracle、5 种深度编码、6 尺寸、有效区/Y 翻转、无效深度、刚性变换、query bound、fragment 消费者和实际 RenderGraph 金字塔。oracle 验增量算术与当前规则，不证明 HiZ 完整几何覆盖。
- [相机正控](evidence/2026-10-13-ssr-framework-rebuild/camera-results.json)：39 组 / **1,152,441 断言**；透视/正交、实际 uniform/关键字、三档统一/独立入口、稀疏 DDA 漏薄前景控制与 dense 控制、逐 GI 历史/多相机/尺寸失效、原 Renderer 引用/Active 恢复。
- 冻结 RAY 生产回归，Diffuse Low/Medium/High 最大 RGB 差异为 0 / `1.52587891e-5` / `3.05175781e-5`，Specular 都为 0；统一/独立信号及 AO 后端隔离都为 0。是当前测试容差内回归，不宣称 XR/非单位动态有效区位精确。
- 库距离 +0.5、SSGI 距离 +0.5、错误后端历史接受三个负控制均被识别，分别见归档 JSON。另 [CLI/report 检查器控制](evidence/2026-10-13-ssr-framework-rebuild/checker-controls.json) 21 项：3 个真实正控接受，18 个失败 CLI/缺 payload/空断言/空矩阵/缺数组/错预算/非有限时间/缺分辨率/错 isolated 模式/未恢复状态拒绝。
- Bash/Python 语法、限定路径 `git diff --check`、退役函数/预算残留检查通过；runner 自建 RuntimeFixture/metas 已清理。编译无错误，但冻结基线和已有其它代码仍有警告，不能宣称全项目零消息。

复跑使用功能目录的 `Validation~/run.sh`，不依赖本轮 staging；新报告始终写 `tmp/sessions/`。原 RAY 基线保留在 Validation~ 作为必要控制，不是退役论文版。

## 实际计时与原配置

Live 原 Renderer 在本轮测量前已经切为 MaskRBR active、SSGI inactive，磁盘 Renderer 未变化。默认 strict timing 因此拒绝，未替用户开启效果。用户另批准一次性 SSGI-only Renderer/Feature 克隆，使用 `run_timing.sh --isolated-ssgi`：克隆开启 SSGI、排除其它 Feature，原场景和设置保留。报告显式 `isolatedSSGI=true`，记录源 Feature/Active；不是当前 Game View 配置的性能。

642×366 与 1920×1080，四后端组合，3 轮、每块预热 8/采样 12，共 **24 块 / 288 样本**；shader/48/64 uniform/6 条 Diffuse rays 和起始 ray frame 8 均检查。1080p 双 RAY 中位数 **9.290 ms** / P95 **10.274 ms**，双 DDA **9.190 ms** / P95 **10.020 ms**；差异约 1.1%，块间波动重叠，不宣称显著提速。本次未重现原逐格版倍增延迟，但新旧算法/预算/内存配置不同，不是同配置等画质 A/B。

计时是 `SubmitRenderRequest → 单像素 ReadPixels` 的 GPU 同步整次渲染墙钟，包含 CPU/驱动/等待/相机管线/回读，非 GPU timestamp、Trace Pass 或连续帧率。[全部报告](evidence/2026-10-13-ssr-framework-rebuild/timing.json)、[输入 SHA256](evidence/2026-10-13-ssr-framework-rebuild/timing-inputs.json) 及完整表格/入口在 [Timing.md](../../../../Assets/Mine/Shaders/PostProcess/SSGI/Timing.md)。两尺寸最终 PNG 已归档、IHDR 尺寸核对，642×366 两张已打开；只能作为显示记录，不证明等画质。

收尾 [Live 状态](evidence/2026-10-13-ssr-framework-rebuild/live-final.json)：Edit Mode、SampleScene dirty=false、MaskRBR active / SSGI inactive；原 Renderer 引用与 Active 的 runner 校验通过。PC_Renderer、SampleScene、SSGIFeature、SSGITemporalFilter、DiffuseGIFunction、SpecularGISampling 六个保护文件 SHA1 与开工一致。未保存场景/资产，Editor/bridge 继续运行。

## 审计、产物与后续

OpenCode `opencode-go/deepseek-v4.1-flash` 两次隔离只读审计（文档、当前 HLSL）完成，无运行中 worker；主 agent 裁决及 [最终审计](evidence/2026-10-13-ssr-framework-rebuild/worker-review.md) 已归档。补明确 HiZ 粗层提示并非命中保证、金字塔 mip0 尺寸必须等于 depthSize、hit.eyeDepth 是射线眼深、RAY 起点比较差异和 LEFT_VIEW 的 1e-4 距离容差。上述近似/前提不擅自升级成新算法；`RayStep_UniformDistance` 仍是公共基础入口，内部暂未使用不构成删 API 理由。

E5 门禁 report exit1 是 review 提案，未更改 policy；隔离 recipe/knowledge/deletion 三套通过，真实 state/audit 指纹保持。必要证据归本 unity-developer memory；用途/依赖/保留条件见证据 README，不归档退役源码或整个 tmp。已应用 staging 与 worker 新源码/文档快照均属可重建重复件，任务计划登记后清理；审计/日志/控制脚本保留在任务目录。

待用户人工确认固定/运动镜头画质，尤其薄前景与闪烁；SSGI HiZ、SSSM 接线、XR/非单位动态有效区、其它平台 GPU 收益仍是后续任务，不随本轮扩展。
