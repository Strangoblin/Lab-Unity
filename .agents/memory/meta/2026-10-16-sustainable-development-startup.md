# 2026-10-16 — 可持续开发判断接入启动流程

## 依据与结果

用户要求把可持续开发的讨论整理到 agent 体系，使新会话在项目开发开始时即可取用。沿用 agent 只负责索引、取用与记录的边界，完整方法归现有 auto-manager skill，不新增角色或 skill。

正文：[可持续开发判断](../../skills/auto-manager/capabilities/sustainable-development.md)。适用于第一项新功能、能力扩展与结构重构，在设计或首次实施前使用；没有同类实现时同样适用。

## 实施范围

- [Unity 角色](../../agents/unity-developer/AGENT.md) 增加条件取用入口。
- [skill 入口](../../skills/auto-manager/SKILL.md) 在知识加载后、首次实施前接入；[能力索引](../../skills/auto-manager/AutoMode.md) 登记正文。
- [Production](../../skills/auto-manager/modes/production.md) 将判断并入修改计划；[Research](../../skills/auto-manager/modes/research.md) 并入假设；[Experiment](../../skills/auto-manager/modes/experiment.md) 在形成阶段 Plan 时纳入。
- 正文覆盖真实基线、稳定约定与暂定选择、近期变化走查、职责与状态归属、复用形态与落点、有区分力的验证。
- 明确 agent 方法 / 模板、项目共享实现、功能自身三种归属；模板变化须检查已有产物，共享实现变化须检查实际调用方。
- 局部小修按实际影响简化；同一任务复用已有判断，仅更新变化的假设。结论并入已有计划，不要求独立文档，不新增门禁、审批或授权。

以上为 1 份新增正文与 6 份入口 / 模式文档的修改。本记录和同库索引用于归档，正文不在角色或模式中复制。

## 验证

- 两个独立新上下文 agent 完成只读冷启动演练：
  - 新功能场景从项目路由找到判断、生产工具模板及 NoiseGenerator 实际消费者；区分 GUID / 所有权约定与当前单资产选择；多输出需求保留未知，未预建框架。
  - 文字小修沿用现有设计，没有展开架构设计、额外复用工作或启动 Unity。
- 独立只读审查未发现阻断：三个模式在首次实施前接入，正文唯一，小修减负与既有授权边界一致。
- OpenCode CLI 实际调用一次，模型 `opencode-go/deepseek-v4.1-flash`，对 7 份实施文档只读核验：83 个相对链接，0 失效；主 agent 审阅任务差异。
- 架构 `verify.py --strict` 通过；7 份实施文档的 `check_norm.py` 逐文件通过。
- 四套隔离 MCP 测试全部通过，真实 state / audit 指纹未变。报告位于任务临时目录 `tmp/sessions/2026-10-16-mcp-tests-vpyf9gdy/output/mcp-isolated-52ae66f6620c/report.json`。
- 归档后的本记录 / 索引规范、架构链接与本次精确差异的空白检查通过。

## 回退点

- 输入：`refs/codex/checkpoints/2026-10-16-sustainable-development-before-30156d8234`（`ca28c4c03570af3e096498dd4b074d5614219f49`）。
- 结果：`refs/codex/checkpoints/2026-10-16-sustainable-development-after-30156d8234`，其父提交为上述输入 checkpoint。
- 两者均为仅包含本任务路径的局部快照，需按文件提取；未切换当前分支、改动工作区索引或混入其他脏改动，也未推送。
- 输入保留 6 份文档及 meta 索引修改前的实际内容；结果另含新增正文与本记录。

## 限制与后续条件

- 冷启动只覆盖上述两个模拟场景，不代表所有任务都会正确执行；后续以真实开发中的取用与结论质量继续观察。
- 没有修改 Unity 业务资产、MCP 策略、平台权限或全局配置，也未运行 Unity。
- 小修演练仍受既有知识预加载规则影响；本次仅简化可持续开发判断，未扩展到优化存量预加载流程。
- 新真实变体出现时，按正文更新当前基线、模板或已有领域记忆；不要把未知需求提前固化为接口或公共限制。
