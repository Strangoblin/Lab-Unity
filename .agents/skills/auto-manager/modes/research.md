# Research Mode

适用 Shader 调参、渲染效果与假设验证。每次修改一个可归因的假设，编译后由人工观测再进入下一轮。
共用门禁和授权以 [执行契约](../capabilities/execution-contract.md) 为准。

## 流程

承接开发入口已完成的模式选择、recipe 与工作区登记，不重新初始化；已有知识与设计结论直接复用，变化时更新。

1. R1 按 [知识预加载](../capabilities/knowledge.md) 读取门禁必读和目标相关材料，输出/同步 G1.5。
2. R2 说明本轮假设和改动点；新增能力或改变结构时将 [可持续开发判断](../capabilities/sustainable-development.md) 并入本轮假设（已有结论复用，普通调参按影响简化）；需要脚本或新增文件时按 [脚本决策](../capabilities/script-decision.md) / [落点](../capabilities/file-placement.md) 取用资源；通过写入门禁修改。
3. Editor 已连接时 R3 按 [编译](../capabilities/compile.md) 快速验证一次；错误报告定位与建议，不自动修复。
4. 编译通过报告实际覆盖；请用户在 Editor 观察目标输出。需要 Shader Pass/载体证据时按 [渲染检查](../capabilities/shader-validation.md)。
5. 人工确认本轮观察结果后再决定下一轮；确认效果达成或放弃方向后收尾。

## 验证和退出

- 不自动进入 Play Mode；用户已有明确要求时沿用其授权。日志/编译不能证明画面质量。
- 不追加实体备份；重大变更仍保护既有工作与 Git 基准。不自动整理无关功能代码。
- Editor 不可用时 R1/R2 后报告代码结果与验证缺口，详见 [可用性](../../unity-editor/references/availability.md)。
- 错误/超时的停止条件见 [恢复与退出](../capabilities/recovery.md)。任务输出按 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 收尾并报告路径。
