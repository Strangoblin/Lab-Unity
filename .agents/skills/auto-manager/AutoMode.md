# AutoMode — 开发流程与资源

承接统一入口选定的 Unity 开发任务。这里负责一次性启动，再交选定模式编排；下列资源按需读取，不是顺序必读清单。

## 一次性启动

1. 取用 [Unity 角色](../../agents/unity-developer/AGENT.md) 与任务相关上下文；已加载的内容直接复用，不重读整库。
2. 读取 [执行契约](capabilities/execution-contract.md)，按 [模式选择](capabilities/mode-selection.md) 确定模式，输出 `[G1] Mode + Reason`。任务开始仅初始化一次 `gate_set_recipe(mode)`。
3. 按执行契约通过 g_entry 并登记用户已授权的工作区；已有明确授权直接沿用，资源位置不等于写入授权。缺少必需授权时再按契约询问。
4. 进入选定模式，按其实际阶段加载知识、形成设计与实施验证。模式及后续迭代不重新初始化入口；知识与设计结论已有则复用，范围或假设变化时更新。

Research 的假设、Production 的修改计划与 Experiment 的阶段 Plan 分别承载本轮设计判断。
首次新功能、能力扩展和结构重构在各模式实施前取用可持续开发能力；局部小修按实际影响简化。
需要 Editor 验证时，在模式验证阶段按 [unity-editor](../unity-editor/SKILL.md) 检查当前可用性；尚未检查不等于未连接。
可用性只决定验证深度，不替代模式选择、业务授权或实际证据。

## 按需能力与模板

| 资源 | 何时取用 |
|---|---|
| [知识预加载](capabilities/knowledge.md) | 选定模式的知识阶段，申报真实已读材料 |
| [可持续开发判断](capabilities/sustainable-development.md) | 首次实施前明确基线、约定、暂定选择、变化边界与验证 |
| [完整性核验](capabilities/completeness.md) | Production 设计阶段，对照本次实际范围 |
| [脚本决策](capabilities/script-decision.md) | 需要复用或新增脚本 |
| [文件放置](capabilities/file-placement.md) | 从资源配置与授权确定实际落点 |
| [Git 基准](capabilities/backup.md) | 重大改动或迭代前保留可比较来源 |
| [编译](capabilities/compile.md) | 按模式编译与处理错误 |
| [运行验证](capabilities/runtime.md) | 核对运行状态、行为与日志 |
| [场景配置](capabilities/scene-setup.md) | 在授权范围内配置验证场景 |
| [渲染验证](capabilities/shader-validation.md) | 选择 2D / 3D 载体、检查 Shader Pass 与证据 |
| [恢复与退出](capabilities/recovery.md) | 失败重试、人工决策与停止条件 |
| [清理](capabilities/cleanup.md) | 任务产物与存量整理 |
| [联网方案](capabilities/web-search.md) | Experiment 查来源、版本与阶段方案 |
| [模板](templates/README.md) | 从 Standard / Script / Shader 家族选择实际骨架 |

各模式在使用处指向所需能力；能力再取用模板、脚本或专门 skill。不因资源可见就加载全部文件。
项目路径按执行契约通过资源 ID 解析，不从模板目录推导工作区。

## 新内容归属

任务分类维护在本 skill 的 SKILL.md；开发模式选择维护在 mode-selection，阶段编排归 modes。
单项操作归 capabilities，生成骨架归 templates；可执行代码归实际负责的 skill。
agent 不内嵌操作流程，知识事实留在领域知识库；任务产物按既有生命周期收尾，再交角色记录。
