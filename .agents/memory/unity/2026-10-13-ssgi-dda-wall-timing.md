---
name: ssgi-dda-wall-timing
description: M2 Metal 真实 SampleScene Medium 配置重现 DDA 延迟增加，漫反射主导；GPU 同步墙钟对照与预算控制、可复现计时入口归档，生产预算未改。
date: 2026-10-13
---

# SSGI DDA 性能排查

用户反馈缺失对话曾怀疑 DDA 预算过多并准备手动计时。本轮只读查当前 rollout 和 history/state SQLite，未恢复这些用户/agent 消息或计时结果；不据此声称反馈从未发生，不修改聊天数据库。以用户提供的线索重新排查。

## 实际状态与结果

- 已不再是 2026-10-12 接线时的“Renderer 未绑定”状态：本轮打开工程后，PC_Renderer 已有 Active SSGIFeature，另外 DebugOutput/MaskBlur inactive；SampleScene/Main Camera，Edit Mode，scene dirty=false，Apple M2 / Metal，642×366。
- 实际 Medium AO + 双 GI、强度均 1，原 Temporal 和后处理保持。DDA 两路各上限 256；RAY Diffuse 48、Specular 64，即约 5.3 倍/4 倍（单元与距离样本单位不同）；Diffuse 为 6 条射线。先读取实际配置，不用昨天的快照代替今天状态。
- 完整条件和最终 12 项中位数/P95 见功能 [Timing.md](../../../../Assets/Mine/Shaders/PostProcess/SSGI/Timing.md)。最终 642×366：双 RAY 3.644 ms，双 DDA256 6.395 ms；1920×1080：双 RAY 9.358 ms，双 DDA256 22.273 ms。
- 1080p 仅 Diffuse 改 DDA 为 20.958 ms，仅 Specular 改 DDA 为 10.373 ms，延迟增加主要来自 Diffuse。DDA128 两路为 13.500 ms，DDA48/64 控制为 8.240 ms。预算是明确的性能杠杆，但没有计数器证明所有射线耗尽；编译期 cap 也可能改变优化，不能把结果唯一归因为某条指令或精确平均步数。
- 这些数值是整个 `SubmitRenderRequest → ReadPixels` 的 GPU 同步墙钟，不是 GPU timestamps / Trace Pass 时间，不是实时 Game View 帧率。提交段同样可能包含驱动阻塞，不标为纯 CPU 成本。

## 源码与验收

沿用已批准 Special/SSGI 工作区和 Experiment，不 reset 门禁或自行批准。新增功能 `Validation~/` 的 Timing 方法、`run_timing.sh` / `prepare_timing.py` / `check_timing.py`，不新增生产配置字段或算法。入口和条件见 Timing.md。

6 组 × 2 尺寸 × 3 轮，8 次预热、12 次测量，共 36 块 / 432 同步样本；轮换顺序，真实原场景、临时相机/Renderer/Feature，计时外分配 RT / 回读对象。检查实际 shader / 后端关键字 / tier uniform / 6 条射线，第一条测量 ray frame 都是 8。两次归档源码入口复跑保持同方向。最终 [完整报告](evidence/2026-10-13-ssgi-dda-wall-timing/timing.json)，[第一次成功复跑](evidence/2026-10-13-ssgi-dda-wall-timing/timing-prior-replay.json)，[开工配置](evidence/2026-10-13-ssgi-dda-wall-timing/current-config.json)。

初次动态 Roslyn 类型使用 JsonUtility 时，报告有 passed/元数据但遗漏 blocks；检查器拒绝，不作为计时证据。改用已实际探测支持的 Newtonsoft.Json，补严格非空/完整矩阵检查。失败 CLI、零 blocks、真实缺数组报告三类控制均被拒绝，控制输入和检查器位置在 task PLAN 中登记。

本轮新增方法后从既有 `Validation~/run.sh` 复跑，原 39 组 / 1,152,437 断言、距离与历史两个负控制通过。生产 RayStep/ScreenTrace/SST/Controls/三个 GI Features、PC_Renderer/SampleScene 开工与收尾 SHA1 相同；新增源码只有验证与说明。原 Settings、Renderer 实例/Feature 引用/Active 与 dirty 校验通过，所有两个 runner 的 fixture/metas 门禁清理；没有保存资产或场景，未提交/推送。Editor 和本轮启动的 bridge 留运行、Edit Mode。

固定镜头 PNG 是实际最终合成显示证据，不能由“看起来相近”证明等画质：[RAY](evidence/2026-10-13-ssgi-dda-wall-timing/Ray-Ray-642x366.png)、[DDA256](evidence/2026-10-13-ssgi-dda-wall-timing/DDA256-DDA256-642x366.png)、[DDA48/64 控制](evidence/2026-10-13-ssgi-dda-wall-timing/DDA48-DDA64-642x366.png)。全部六张运行图 IHDR 已核对尺寸，当前尺寸的三张已打开；这里仅归档当前尺寸三张。JSON/PNG 属于本 dated memory，依赖本轮 SampleScene/SSGI/URP/Metal，用于此版本历史对照；新的运行输出回 tmp/sessions，不累积到功能源目录。

OpenCode 一次 `opencode-go/deepseek-v4.1-flash` 隔离四份快照审计完成，报告在 `tmp/sessions/2026-10-13-ssgi-perf-a1/fixtures/hotspot-review/AUDIT.md`。主 agent 修正“AO 也用 DDA 预算”错误；源码中最多若干除法不等于 GPU 实际执行同等数量，编译器可能折叠/提升。审计没有替代真实计时。没有运行中 worker。

E5 门禁自省形成报告、未改策略；隔离 recipe/knowledge/deletion 套件通过，真实 state/audit 指纹保持。没有架构/适配层变动。

## 后续边界

生产上限仍 128/256/512，默认 DDA 保持。简单砍成 RAY 数字会缩短 DDA 可搜索范围、提前 BUDGET，不是跨度增加；没有等质量或运动稳定性验收，不自动应用该控制。优先优化逐格计算的不变量/轴边界增量，保留薄物体与透视首命中合同，再统计实际 steps / budget 比例并定档。生产 HiZ/XR/动态有效区及连续运动仍未验收。

任务目录 `tmp/sessions/2026-10-13-ssgi-perf-a1/` 的 PLAN 归属所有试验与控制输入，必要日志/审计保留；不清理其他会话、用户脏文件或缓存。长期源在功能 Validation~/ 和 Timing.md，精选证据在本 memory evidence，不依赖 staging 执行。
