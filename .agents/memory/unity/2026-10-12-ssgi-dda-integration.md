---
name: ssgi-dda-integration
description: SSGI 默认 DDA 接入公共库，保留 RAY 与 AO，逐 GI 配置历史失效；39 组真实相机验证与两个负控制通过，性能选档仍待测量。
date: 2026-10-12
---

# SSGI DDA 接线

承接 [公共库阶段](2026-10-12-screen-trace-library.md)，用户继续后完成 SSGI 接线。工作区请求 `ws-a981d5d9` 由人工批准后才写入；未 reset 门禁、改策略或借 worker 扩权。Git 基准仍为 `f9b3ad8`，未提交/推送。

## 实现与不变项

- `SSGI/ScreenSpaceTrace.hlsl` 保留 SST 辅助与追踪签名，追踪适配共享 RayStep / ScreenTrace；只有追踪 shader 显式 include 公共库。默认 DDA 为枚举 0，Ray 为 1；局部 Trace 关键字在 PassData 快照后于执行绑定，Trace target 4.5。
- 两个模块统一与独立路径都使用本模块 Settings。DDA 初始 Low/Medium/High 为 128/256/512 深度单元；原 RAY 分别 Diffuse 24/48/64、Specular 32/64/96，原 1/8、1/4、1/2 分辨率和 4/6/8 漫反射射线保持。
- DDA 网格来自有效相机深度与物理源尺寸，不使用低分辨率 Trace 尺寸；实际相机矩阵/Y 约定与旧 SST 一致。没有金字塔请求，也不在预算耗尽后自动重跑 RAY。
- 历史签名从实际 PassData 参数构造，精确比较后端、几何、采样/空间参数与资源引用；Specular 包含 roughness/Cubemap/mip，独立 Diffuse 包含距离衰减。只拒绝相关 GI 的旧颜色资格，不清空/重分配历史；AO、三个独立 Material 与共享深度双缓冲保持。
- `AOFunction.hlsl`、`DiffuseGIFunction.hlsl`、`SpecularGISampling/Trace/Filter.hlsl` 改前改后 SHA1 相同，包含已有未提交采样改动。没有修改 BRDF、采样分布、置信度、空域与合成算法，没有保存场景/Renderer 资产。

权威合同、预算与运行步骤见 [SSGI 说明](../../../../Assets/Mine/Shaders/PostProcess/SSGI/SSGI.md) 与 [公共库](../../../../Assets/Mine/Special/Rendering/ScreenSpaceTrace/README.md)。

## 实际验证

Unity 6000.3.14f1 / URP 17.3.0 / macOS Metal，Edit Mode 的真实 Game camera `SingleCameraRequest`、RenderGraph、当前项目 shader/Feature，不是 CPU 模拟 shader。

最终从功能 `Validation~/` 归档源码复跑：**39 组 / 1,152,437 断言通过**，[精选报告](evidence/2026-10-12-ssgi-dda-integration/metal-camera-results.json)。主要覆盖：

- 189×101、321×179、97×61 透视/正交，实际深度网格与物理源均匹配请求尺寸；平面世界距离/UV、near/离屏起点裁切、原起点距离、单像素薄前景、显式 BUDGET。薄物体控制允许稀疏 RAY 命中远背景，只要求它漏掉薄前景，不误将“没有漏掉所有几何”当作 DDA 失败。
- 统一/独立 × DDA/RAY × Low/Medium/High 的实际输出、步数/射线数 uniform 与后端关键字。冻结接线前 RAY shader/SST 基线：Diffuse 最大 RGB 差异 3.05175781e-5，Specular 为 0；两个入口相同空间信号及 AO 后端隔离差异为 0。
- GI 后端/几何/滤波/质量/roughness/环境变化只失效目标通道一轮，下一轮恢复；强度/混合权重不失效；RTHandle 不重新分配；精确 key 字段比较、双相机隔离与尺寸失效。
- runner 自建临时 Renderer/preview scene，不替换/重建原 Renderer；最后检查原实例、Feature 引用与 Active 保持，场景 dirty 状态保持。未保存原配置、未停 Editor/bridge；临时 RuntimeFixture 源码/metas 全部经门禁清理。

两个真正的已知错误注入：

1. GPU DDA 距离加 0.5，[控制报告](evidence/2026-10-12-ssgi-dda-integration/distance-negative-control.json)：expected 2.5 / actual 2.999999，确实报错。
2. 伪造旧历史 key，使 RAY 接受旧 DDA 历史，[控制报告](evidence/2026-10-12-ssgi-dda-integration/history-negative-control.json)：目标通道 expected historyValid 0 / actual 1，确实报错。

显示证据是 640×360、Medium、无 temporal shader、固定相机/场景/射线帧号的空间信号，不是最终艺术画质：

- Diffuse：[DDA](evidence/2026-10-12-ssgi-dda-integration/diffuse-medium-DDA.png) / [RAY](evidence/2026-10-12-ssgi-dda-integration/diffuse-medium-Ray.png)。
- Specular：[DDA](evidence/2026-10-12-ssgi-dda-integration/specular-medium-DDA.png) / [RAY](evidence/2026-10-12-ssgi-dda-integration/specular-medium-Ray.png)。

PNG IHDR 均核实为 640×360，已打开全图。来源是本任务拥有的临时 Renderer 与合成受光几何；依赖当前 Unity/URP/Metal。精选 JSON/PNG 属于此 dated memory，保留用于该接线版本历史对照；新运行输出回 task session，不累积进功能目录。

## 验证工具与委派

可复现入口：`bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run.sh`。完整源码、原 SST/两路 RAY shader 基线归同功能 `Validation~/`；原 shader 仅改验证名称/include，采样实源与生产同用且本轮未变。新 fixture 拒绝覆盖存量目录，用 EXIT trap gate-clean。

本机 `unityctl script execute` 曾在运行异常时退出码为 0。runner 必须解析 `--json` 的 success/result，交叉检查实际 `results.json` 的成功标志、断言数与必要 case，不靠 `set -e` 判断语义成功。失败响应和空断言两种假的 CLI 成功结果均被 `check_result.py` 拒绝。初期包含 pending 占位的探针报告不作为最终验收证据。

OpenCode 固定 `opencode-go/deepseek-v4.1-flash`，只读隔离快照，仅允许写审计 MD。本阶段历史预审成功；接线大审计只读后退出、无报告；一次缩小到 history 的重试完成，已向用户反馈。主 agent 保留设计、合入与 GPU 验收。报告称“精确比较是逐位相等”不准确；没有 hash 消费者或不等 hash 的有效反例，其“GetHashCode 契约 bug”不作为实际缺陷采纳，不扩大功能代码。

门禁自省只形成报告，未改策略。隔离 MCP recipe/knowledge/deletion 契约套件通过，真实 state/audit 指纹保持。已有其它任务的 dirty 文件及 memory/索引内容不回退。

## 限制与后续

- DDA 为按用户方向选择的初始默认，不是等质量性能选档通过。128/256/512 上限不保证高分辨率长射线完整覆盖；GPU 中位数/P95、预算/质量对照与内存成本未测，不声称提速。
- XR、非单位动态有效区、局部 camera.rect、其它图形 API、连续运动与快速镜头的闪烁/拖影仍未验证。实际请求的深度网格为全视口、单位映射；不能把公共库 crop 测试当作真实 SSGI 动态分辨率验收。
- 生产 HiZ / SSSM 未接线。独立 Diffuse 沿用输入请求，历史仍取决于 Renderer 是否实际提供 Motion。
- 当前 PC Renderer 未挂载 SSGI，本轮未持久化接入。开工前曾看到内存中的临时 MaskRBR Feature，C# 重载/并发任务后最终列表只剩 DebugOutput/MaskBlur inactive；没有其变化时刻/原因的直接证据，未自行恢复其它任务对象。测试 scope 的恢复检查仅证明各次验证期间的原列表保持，不证明跨 domain reload 的临时状态保持。

任务计划与审计/中间输入复用 `tmp/sessions/2026-10-12-ray-step-plan-a1/`，其中 §9 标明已完成接线与未完成性能项；正式代码/验证源与精选证据已归上述权威位置，不依赖 staging 执行。
