---
name: task-artifacts-lifecycle
description: Lab 存量产物审计、会话落点与隔离测试生命周期
date: 2026-10-10
---

# 会话产物与 Lab 整理

用户要求审计整个 Lab：无用文件删除、有用文件归档，并查明对话产物落点。用户以“无误，可进行”批准精确清单；本批已执行并核验。

## 规范落点

新增 [task-artifacts.md](../../../interfaces/task-artifacts.md) 为共享唯一契约；新产物归 `tmp/sessions/<date-task-id>/`，按 staging / fixtures / output / evidence 用途分流。

复用源码归角色 scripts 或功能 Validation，必要历史证据归角色 memory/evidence；归档验证工具必须带必要基线并实际复跑。停止新增 `.codex/tmp`、`tmp/.reusable`、Screenshots 与资产 `.backup_*`；存量唯一资料先保留。

统一根入口、两平台适配、角色收尾、auto-manager 的脚本决策/场景/落点/清理/备份与各模式；旧 cleanup 的扩展名批删、永久临时库、自动场景整理和双备份流程已删除。回退采用 Git，保留已有脏工作。

独立场景审查补齐 E5 的隔离 runner 路由、收窄旧「任何删除都确认」到存量清理，并明确续接遗留任务的新产物进入规范会话目录；不重复请求同一清单授权。

## 测试基础设施

- 架构扫描以仓库根相对路径判层，剪枝复制项目、旧 tmp、环境缓存与 suite 输出；真实断链/漂移/硬编码控制仍被检出。报告可用 `--output-dir` 写入任务目录。
- MCP 新增 `tests/run_isolated.py`：独立项目/Git/TMPDIR，排除生产 state/审计/缓存；runner 自有 fixture 在 finally 清理，失败可显式保留。
- Codex 汇总 runner 使用唯一 mktemp 退出码文件和 trap，修复并发覆盖。

验证：扫描控制 8/8 与架构 strict 全绿；MCP runner 回归 9/9、隔离契约 30+5 及 registry 自检通过；生产 state 与三份审计 SHA256 前后一致。Bash 副本控制 10/10，语法及 diff 检查通过；未跑会写真实 Assets 的 Codex suite。

原 skill frontmatter 含 Claude 的 `argument-hint` / `user-invocable`，官方 quick_validate 不识别；保留原字段，解析原 YAML 并验证兼容投影通过。没有为验证修改发现元数据。

## 审计与执行结果

精确清单与执行证据归 [正式审计目录](evidence/2026-10-10-lab-structure-audit/README.md)；路径、Git 状态、SHA256、引用、目标和保留理由均逐文件记录。已缺失的前任务文件单列，不算本批清理；本次暂存、复制归档与可重建输出在收尾核验后清理，仅留会话指针。

按 [批准清单](evidence/2026-10-10-lab-structure-audit/cleanup-plan.md) 清理 248 个无用/可重建文件（27,689,525 B），71 个旧源（51,780,139 B）在归档核验后移除；113 项正式归档及 4 个文档更新完成。75 个明确保留、247 个活跃/待判材料复核保留，15 个先前缺失路径不计本次删除。执行记录见 [execution-report.json](evidence/2026-10-10-lab-structure-audit/execution-report.json)。

78 个空目录已清理，14 个非空目录保留；SSC 的空备份目录当时因删除通道只支持文件而保留，补齐目录删除后已按同一份清单清掉（见 2026-10-10-gate-directory-deletion.md）。生产 state/审计按正常门禁操作更新，没有 reset。Hash 当前合同与统计、dense 不可变 CPU 参考在正式路径复跑通过；Unity 资源刷新显示 compilation succeeded。架构 strict 与活动链接检查通过。

Weather Validation、Gradient TEST、TestAuto 测试 fixture、环境缓存和生产门禁状态保留。旧 Shader rename 计划含 Git 未收录快照，需提取唯一证据后才能处理；历史截图仅逐字节相同者可判重复。仍服务于未提交工作的基线保留原位。

Hash 归档同时保留原始输入与实际 runner；当前调用合同通过，后续 Shader 修改导致的历史整文件漂移单列，不能伪报历史快照检查通过。无 Unity/Metal/GPU 验证。

MaskBlur 补齐 10 份去重外部 RGBA 输入（仅复制）并冻结 CPU 参考；六个大型 NPY 已实际逐字重建，改为保存固定 SHA/shape/dtype 与报告值，生成数组可在归档核验后删除。旧 shader-rename 保存完整映射、155 个已回读 Git blob 与 13 个唯一快照；字段值重建通过。AO 历史探针已从旧回退包找回，正式文档修正随归档执行。

P1-P3：规范只保留一个权威源，入口链接路由；执行源码独立于 skill；cleanup/backup 由 195/53 行缩至 24/18 行，不创建新 skill 或重复文件体系。
