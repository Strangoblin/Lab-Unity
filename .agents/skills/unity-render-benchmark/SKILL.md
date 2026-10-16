---
name: unity-render-benchmark
description: 对比 Unity 渲染效果、Shader 或 RendererFeature 改前/改后的耗时，尤其是 Metal 无可用 GPU Profiler 时的同步墙钟 A/B 测评；控制条件、重复采样与判读波动，不提供逐 Pass 的纯 GPU 时间。
---

# Unity 渲染耗时对照

目标是判断同条件下哪个方案更慢/更快，而不是获得精确 GPU 指令或逐 Pass 时间。不限于 SSGI 或 Metal。

## 边界与入口

- 这是薄工作流，不是已经完成的通用 C# 步进/计时库；效果特有配置、变体切换与验证留在该效果适配器中。
- 本 skill 的使用不授权修改原 Renderer/场景、排除其它 Feature、切 Play Mode 或重启 Editor。体系维护取用 agent-maintenance；实际采集/适配器开发由本技能与 auto-manager 按任务范围完成。
- 画质/中间输出对照使用 [unity-render-debug](../unity-render-debug/SKILL.md)；Editor 连接和命令使用 [unity-editor](../unity-editor/SKILL.md)。仅分析已存在报告时不需要连接 Editor。

## 按输入选择路线

- 已有报告：读取协议的“最小原始报告”和“相对判读”，检查条件、口径与样本后直接分析；不连接 Editor、不解析采集适配器、不构造新采集脚本，也不重复采样。缺少数据明确限制结论，需要新采集时由用户任务范围决定。
- 需要采集：使用下面流程，按当前授权取得实际项目适配器。

## 采集与分析

1. 定义 A/B 及唯一被测变化，声明测的是整套实际管线还是已获授权的隔离克隆；先读取 [采集与判读协议](references/benchmark-protocol.md)。
2. 查目标功能的 `Validation~/`、现有脚本与记录，优先复用适配器。没有适配器时按协议在任务目录构造最小采集脚本，不复制一套效果实现或修改生产默认值。
3. 固定相机、分辨率、场景输入、后处理和非被测参数，检查实际执行的 Shader/关键字/预算。初始化、编译、RT 分配和截图不放入计时段。
4. 相对耗时对照默认使用明确标注的 GPU 同步渲染请求墙钟；只有需要纯 GPU/逐 Pass 时间时才改用对应工具，工具不可用就说明该项无法测量。预热、交错/反向重复 A/B，记录原始块与采样条件；不混合两种口径。
5. 先验证实际路径、完整样本与状态恢复，再比较同尺寸的块中位数、相对差和波动。轮间漂移或差异与波动相当时报告“本次未分辨差异”，不要硬判提速，也不盲目加测。
6. 输出 A/B 范围、条件、同步口径、重复/预热、原始报告路径和可支持的结论。临时状态恢复与证据归属沿用 [会话产物规范](../../knowledge/meta/interfaces/task-artifacts.md)。

## 现有适配器

SSGI 适配器与计时合同分别通过 `python3 .mcp/validation/project_resources.py path benchmark.ssgi.adapter` 和 `path benchmark.ssgi.contract` 解析：比较当前 RAY/DDA，前置条件与报告由其验证器检查。它不是任意效果或单遍/多遍对照的即用入口；需要新维度时由对应开发任务扩展适配器。本 skill 不搬迁、不复制这些效果专属脚本。

## 交付限制

同步墙钟可支持“这套配置下整次渲染请求的相对耗时”，不支持纯 GPU/Trace Pass 毫秒、连续 Game View FPS、全平台结论或等画质保证。提交段也不是纯 CPU 成本；同步段减提交段不是 GPU 时间。记录画质变化与未测项，但不把截图成功当作性能或等画质证明。
