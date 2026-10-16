---
name: render-benchmark-skill
description: 薄unity-render-benchmark流程，默认同步墙钟比较相对耗时；角色reference、双端发现与入口同步，不操作Unity/改业务代码；离线评审和隔离MCP通过，strict存量历史路径21项未改。
date: 2026-10-14
---

# Unity 渲染相对耗时薄 skill

用户要求进入meta模式，供后续Metal等环境中其它效果的耗时比较使用，重点不是精确GPU计时。本阶段未操作Unity Editor、未改Assets、门禁状态/策略/账本或已有SSGI计时脚本，未提交/推送。

## 归属与边界

- 新入口 `.agents/skills/unity-render-benchmark/SKILL.md`（31行）：A/B路由、作用范围、默认同步墙钟、路径检查/重复/波动判读和交付边界。只在需要纯GPU/逐Pass时间时选择对应工具；不强迫相对对照任务启用复杂Profiler。
- 知识合同 `.agents/agents/unity-developer/references/standard/rendering/render-benchmark.md`（49行）：计时段、条件、顺序/预热、原始块与相对解释。8/12/3是已验证范例建议，不设统一样本/百分比显著性硬阈值。
- skill引用现有SSGI `run_timing.sh`/CameraChecks/验证器，明确是效果特有的RAY/DDA适配器，不是任意效果或单遍/多遍的即用入口；需要新适配器时走unity-developer功能任务。本阶段没有提取通用C#采集库，也不复制业务实现/可执行代码到MD。
- skills与rendering索引、Unity角色manifest、unityctl CLI和render-debug互相导航已接入。Claude新建相对目录软链；Codex现有 `.codex/skills` 目录别名自动发现，同源正文没有副本。
- P1/P2/P3：两份新增MD各不超过80行、逐步披露、查重仅已有SSGI采集例与渲染Debug流程，正文/协议分工而不重复完整算法；不增加生产门禁或改变策略。

## 验证与限制

- skill-creator quick_validate通过。系统Python与项目MCP虚拟环境都缺PyYAML，因此只在本任务临时venv安装验证依赖，不改项目/全局依赖；临时环境可重建。
- 七个本轮源文档的相对引用、绝对路径/大小写扫描及Claude/Codex canonical alias身份检查通过；git diff whitespace通过；新增MD长度31/49行。
- OpenCode flash一次只读离线forward评估：小差异与交叉块波动不能宣称GPU提速、非SSGI不能直接套旧runner/改原配置、平滑PNG不证明更快且单遍/多遍需适配器。主会话复核，不采纳统一波动阈值公式建议（场景依赖且无统计检验），不复制已在协议说明的unsupported分支。没有真实引擎采集或新的GPU结果。
- default架构验证通过，MCP隔离30 recipe /5 knowledge /23 deletion定义的三套件通过；生产state/policy/friction和两份SSGI源指纹保持。
- 全库strict仍报21个 `/Users/` 命中，全部来自本阶段未修改的2026-10-13 Unity历史证据JSON（gate-contract-report与checker-controls），没有本轮源文档命中。保持原始历史证据与验证器不变；不能宣称全库strict全绿。可另开任务评估历史证据扫描合同，当前不顺手改扫描策略。

任务计划、worker评估与验证输出在 `tmp/sessions/2026-10-14-render-benchmark-skill-r1/`；必要摘要与日志见 [证据](evidence/2026-10-14-render-benchmark-skill/README.md)。功能下一步可按skill另开单遍/两级GI适配与同步墙钟对照任务，不隐含本轮授权。
