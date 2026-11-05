# Production Mode

适用库内已有方案的实现、修复、重构与整理。共用沙箱、通道与出口验收以 [执行契约](../capabilities/execution-contract.md) 为准。

## 流程

承接开发入口已完成的 mode / profile / 沙箱声明，不重新 open；已有知识与设计结论直接复用，变化时更新。

1. P1 按 [知识预加载](../capabilities/knowledge.md) 读取 profile 必读和相关领域（读取由 hook 观测，不必申报）。
2. P2a 读取目标代码，新增 / 扩展 / 结构重构按 [可持续开发判断](../capabilities/sustainable-development.md) 明确设计边界（已有结论复用，局部小修按影响简化）；P2b 按 [完整性核验](../capabilities/completeness.md) 逐项对照；P2c 将相关结论并入范围内修改计划。
3. 按 [脚本决策](../capabilities/script-decision.md) 查现有库；按 [文件放置](../capabilities/file-placement.md) 解析实际落点并检查目录。
4. P3 用平台 Write/Edit 生成/修改代码（hook 在写入时判定）；重大改动先按 [Git 基准](../capabilities/backup.md) 核对回退来源。
5. Editor 已连接时 P4 [编译](../capabilities/compile.md)，按需 P5 [场景配置](../capabilities/scene-setup.md)，P6 [运行验证](../capabilities/runtime.md)。静态 Shader 可按 [载体检查](../capabilities/shader-validation.md) 选择隔离渲染；运行时行为再进入 Play。
6. P7 按 [清理](../capabilities/cleanup.md) 分类产物，报告实际证据及未验证项，再跑 `cli.py close` 核对出口义务，最后交角色记录。

## 失败与 Editor 边界

[Editor 可用性](../../unity-editor/references/availability.md) 决定验证深度；未连接可完成独立代码工作并注明未编译/运行。
修复上限和人工决策节点见 [恢复与退出](../capabilities/recovery.md)。
USE 结论不再造同功能脚本；warning 不代替流程自查。新建资产仍受沙箱与规范约束（已入库资产由 git 兜底），删除走 `cli.py delete`、移动用 `mv`。
