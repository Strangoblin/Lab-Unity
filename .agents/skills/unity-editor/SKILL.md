---
name: unity-editor
description: Control Unity Editor from the command line and choose between unityctl for a running Editor and Unity's official Editor CLI for process-owned automation. Activate for play mode, compilation, logs, tests, scenes, builds, batch mode, or Editor command-line usage.
---

# Unity Editor 操作

本 skill 集合 Editor 连接、命令和 Roslyn 脚本；角色索引不维护操作流程。
先运行 `unityctl status`，按 [连接与可用性](references/availability.md) 判断实际通信状态。

## 选择通道

| 通道 | 适用 | 边界 |
|---|---|---|
| unityctl | 已运行 Editor 的场景、Play、日志、查询 | Bridge 与 Editor 必须同宿主 |
| 官方 Editor CLI | CI、冷导入、构建、测试、executeMethod | 自有 Editor 进程，不是向已开 Editor 发 RPC |
| 官方 -version | 安装/版本探测 | 不启动 Editor |

项目已被 Editor 打开时不再对同一路径启动第二个官方实例；使用 unityctl 或独立项目副本。
发现安装路径，避免固定 Unity 版本；官方参数查 [Unity 文档](https://docs.unity3d.com/Manual/EditorCommandLineArguments.html)。

## 操作与验证

1. 从 [CLI 索引](references/cli/README.md) 选择命令；查询前后状态使用 snapshot、logs 或 script eval。
2. 需要脚本先查 [Roslyn 库](scripts/roslyn/README.md)，按 [脚本决策](../agent-workflow/capabilities/script-decision.md) 复用或新建；脚本用文件编辑工具写入。
3. 根据任务模式执行编译/运行；日志、结构和视觉质量分别报告。画面质量由人工在 Editor 判断。
4. 场景/资源地址来自用户输入或 `python3 .mcp/lib/validation/project_resources.py path <id>`；配置不赋予额外修改权限。
5. 仅恢复本流程改过的状态；关闭/重启 Editor 需考虑未保存内容和任务授权。

## 场景改动边界

场景文件是用户正在进行的工作，不是可以就地覆盖的验证载体。按状态区分：

| 场景状态 | 允许的动作 |
|---|---|
| Git 干净（`git status` 无该文件） | 可作为任务对象修改并保存 |
| **有未提交改动**（`M`，或未跟踪但已有内容） | 只读查询；要改就复制副本或新建临时场景，验证完删副本 |
| 编辑器内 dirty（本次会话有未保存改动） | 只增加/恢复本流程引入的对象；不 `SaveScene` 收尾 |
| 用户点名要求改某场景 | 先列「对象 / 字段 / 增删」清单并取得确认，改完不 `SaveScene`，由用户自己保存 |

- 判定看 Git 与 `scene.isDirty`，不靠文件名：`_Recovery`、`0 (3).unity` 这类恢复场景同样是用户产物。
- `EditorSceneManager.SaveScene` / `MarkSceneDirty` 会把整份场景写回，覆盖用户未提交的改动；Git 只能回到最后一次提交，中间的编辑不可恢复——按 AGENTS.md 的「改错了能不能回来」判据，这属于不允许的操作。
- 只在用户场景上做的合法改动：新增本任务对象、写本流程自己创建的引用。删除存量对象、改层级、改层掩码、删组件都要先拿到清单级授权。
- 需要「改了场景才能验证」时，用 `NewPreviewScene`、复制场景或 `Additive` 临时场景，不要把实验直接落在用户场景上。

来源：2026-11-05 用 `SceneToWaterReference.cs` 在 `WaterInteractionDemo.unity`（当时带未提交改动）上删 `DepthReference`、改 `WaterSurface` 层并 `SaveScene`，未先取得授权。

完整相机 PNG 用 [unity-render-debug](../unity-render-debug/SKILL.md)，相对耗时用 [unity-render-benchmark](../unity-render-benchmark/SKILL.md)。
临时工作按 [产物规范](../../knowledge/meta/interfaces/task-artifacts.md) 归属；可复用 Editor 操作代码归本 skill。
