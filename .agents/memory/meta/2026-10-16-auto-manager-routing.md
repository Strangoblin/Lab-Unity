# 2026-10-16 — auto-manager 首次分流与内部层级路由

## 依据

用户要求检查 auto-manager 是否适配现有层级节点式索引：外部需求先进入统一入口，再进行内部路由，避免角色与总清单直连内部能力形成多套流程。

只读审查发现两处偏离：Unity 角色直接指向可持续开发叶子，并允许 Editor 等操作从技能清单自行分流；auto-manager 主入口与三个模式又重复要求入口、知识和设计动作，Experiment 的 Plan / 知识先后顺序也不够明确。

## 当前路由与改动

```text
Unity 任务 → auto-manager/SKILL.md
  ├─ 普通知识咨询 → 按需知识 / 记忆
  ├─ 独立操作或专项分析 → 对应 skill
  └─ 开发实施 → AutoMode → mode-selection → 选定 mode
                                      → 按需能力 / 模板 / 专门 skill
```

- [统一入口](../../skills/auto-manager/SKILL.md) 只选择任务分支；更新 description，使其覆盖 Unity 操作与验证。具体目标优先于泛化咨询，已有对照报告的判读走 benchmark。
- [开发节点](../../skills/auto-manager/AutoMode.md) 统一上下文、模式选择、一次 recipe / g_entry / 工作区登记；模式只负责自身的知识、设计、实施与验证阶段。需要 Editor 验证时再检查连接，未检查不等于未连接。
- [Production](../../skills/auto-manager/modes/production.md)、[Research](../../skills/auto-manager/modes/research.md)、[Experiment](../../skills/auto-manager/modes/experiment.md) 复用启动状态与已有结论，不再重复入口。可持续开发判断仍在各模式首次实施前，Experiment 在形成 Plan 时使用。
- [角色清单](../../agents/README.md)、[Unity 角色](../../agents/unity-developer/AGENT.md)、[技能清单](../../skills/README.md) 的首次任务分派收口到 skill 入口；角色不再直达可持续开发叶子。
- [架构事实](../../knowledge/meta/architecture.md) 记录首次分派与真实依赖的区别。知识 / 模板契约和 skill 间实际协作保留直接引用，不为每次依赖反跳总入口。

独立操作与已有报告分析不初始化完整开发 recipe、不展开通用设计检查。需要新增 / 修改受管源码、任务脚本、适配器或持久资产时，补齐开发分支后实施，原操作目标作为验证。明确点名专门 skill 或已分派任务沿用当前分支；实际写入、场景 / Renderer、Play 和重启仍受原授权与执行约束。

功能文档修改明确进入开发分支，局部文字修正按实际影响简化；本次没有为小修新增门禁豁免。

## 验证

- 两路独立只读审查分别核对外部入口与内部顺序，最终未见阻断或必然循环。
- 新上下文演练覆盖新增 CurlNoise / Texture3D、功能文档小修、Editor 状态查询、已有 A/B 报告分析、报告分析升级采样适配器，以及点名渲染调试。确认开发只初始化一次，操作与分析不预读完整设计；演练发现的文档小修分支、Editor 检查时机与报告优先级歧义已补清并复核。
- OpenCode CLI 实际调用一次，模型 `opencode-go/deepseek-v4.1-flash`，完成仓库文档相对链接与角色 / 总清单下钻的只读检查；主 agent 复核随后编辑及最终精确差异。
- 架构 `verify.py --strict`、本次文档逐文件规范与相对链接检查通过；最终任务差异的空白检查通过。
- 四套隔离 MCP 契约测试通过，真实 state / audit 指纹未变。报告：`tmp/sessions/2026-10-16-mcp-tests-m68j8rc9/output/mcp-isolated-df1b089f300e/report.json`。
- skill frontmatter 的 YAML 解析、description 与既有其余字段保真检查通过。通用 skill-creator validator 不支持既有 `user-invocable` / `argument-hint`，仍不能全量通过；未为迁就检查删改这些平台字段。

## 回退点

- 输入：`refs/codex/checkpoints/2026-10-16-auto-manager-routing-before-90149c014f`（`f8920a5c14031e8479e160adb1909f5e83949d08`）。
- 结果：`refs/codex/checkpoints/2026-10-16-auto-manager-routing-after-90149c014f`，父提交为上述输入 checkpoint。
- 仅保存本任务文档修改前后的局部快照，按文件提取；不切换当前分支、不修改工作区索引、不混入其他脏改动，未推送。

## 限制

演练为文档驱动的只读模拟，不等于真实业务、Editor 或渲染验证。本次仅修改共享体系文档，未修改业务资产、MCP 判定 / 策略或平台权限。

专项技能中编译状态查询示例、相机画面与 Game View 的区别、具体 Shader 检查范围，以及既有知识预加载的小修成本仍按原流程处理；不因统一入口而宣称这些细节已改进。
