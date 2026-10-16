---
name: agent-skill-boundaries
description: Agent 改为索引/取用/记录，执行资源归 skill；51 个旧入口经确认切换软链，项目资源绑定与权限分离。
date: 2026-10-14
---

# Agent / Skill 职责与项目资源解耦

用户明确要求 agent 相当于大脑，只负责索引、取用与记录；skill 集合对应脚本与工作流，并让 `.agents` 与业务资产尽量解耦。

## 实际变化

- 角色入口缩为 unity-developer 51 行、meta-developer 50 行；范围、知识/技能路由、判断与记录留角色。操作流程、模式、Editor、完整性与恢复归 skill。
- 新 agent-maintenance 持有体系维护脚本、工作流与角色模板 asset；原角色模板 reference 留事实索引。auto-manager 持有 Unity 模板；unity-editor 持有 CLI 与场景脚本；render-debug 持有取像脚本/流程；feature-reuse 明确隔离项目 Hash 合同验证器。
- 51 项存量源在当前会话列出精确清单，用户明确回复“允许按清单切换为兼容软链”；整批源 SHA-256 复核后原子替换相对软链。目录壳和平台既有入口保留，正式编辑位置只有 skill。活动 references/接口引用同步，历史 memory 与不可变 fixtures 不改写。
- 新职责契约 `interfaces/agent-skill-boundary.md`、资源输入契约 `interfaces/project-resources.md`；位置绑定为 `.mcp/project-resources.json`，只读解析器拒绝未知 / 缺失 / 越界，不影响授权。
- 通用执行入口不默认写死业务目录；模板输出由用户选择，Debug / HLSL / SSGI 适配器通过资源 ID 取得。事实来源、历史快照与 Lab 专属 Hash 消费合同保留原式路径，未声称完全可移植。
- 架构扫描固化 51 别名登记、拒绝角色实体工具、断链 / 越界别名，并按 canonical 目录核验登记 Markdown 别名；dated evidence 延续历史豁免，但活动 memory 内偷放脚本仍拒绝。
- 保留旧 camera `USE <basename>` 查询兼容与精确旧路径权限；不给整个 render-debug 目录新增写权限。位置配置不成为写权限表。
- root / Claude / Codex 入口与运行时角色薄适配同步；旧 Editor skill 改成独立 legacy 索引，避免重名重复流程；用户的项目级偏好同步全局两平台 memory。

## 验证与限制

- strict 架构通过：断链、漂移、机器路径、大小写、所有权错误均 0。
- 所有权 19、扫描 8、资源绑定 5、隔离 runner 9 控制通过；MCP 四套件隔离通过，生产 state / 审计指纹不变。
- 17 个 canonical 模板 API / 词法核验通过；场景/取像脚本、Hash 三脚本和不可变 fixtures 字节保持，Hash 项目根与新 fixture 定位正确。
- skill-creator 通过新维护技能及四个 canonical Unity 技能；auto-manager 原有 Claude frontmatter 扩展不在 Codex 校验器 allow-list，保持既有发现兼容。不更改系统 Python，仅 uv 临时提供 PyYAML。
- OpenCode `opencode-go/deepseek-v4.1-flash` 实际一次完成限定 meta 文档与注释修改；主会话复核 task-only diff。并行技能 worker 容量失败后恢复一次成功。
- 独立只读请求演练补齐：完整角色模板归技能，cleanup 交角色回写，已有计时报告分析明确跳过 Editor / 适配器 / 重新采集。
- 没有 Unity Editor 业务操作、资产改动或新的 Shader/GPU 结论；门禁严格度与平台 hook 保持。仓库已有大量未提交改动，未自动提交或 stash 他人工作；Git 基准 `f9b3ad8`，本任务 before 快照与 task-only patch 保留在规范会话 staging。

## E5 与后续

现有摩擦报告 470 次真实操作、仪式比 0.104；`norm.region-added`、`placement.root.new` 的零命中提出复核。仅记录待办，没有调整策略；后续须按能力契约与人工确认独立处理。
旧软链仍是兼容入口，退役须另列清单；不为目录整洁删除未知资源。项目专属 Hash 工具可在后续实际复用需求下提取参数合同，本轮保持其验证语义。

必要切换/验证摘要见 [证据](evidence/2026-10-14-agent-skill-boundaries/README.md)；可重建输出在 `tmp/sessions/2026-10-14-agent-skill-boundaries/`，不将整个工作区归档。
