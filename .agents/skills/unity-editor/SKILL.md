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
2. 需要脚本先查 [Roslyn 库](scripts/roslyn/README.md)，按 [脚本决策](../auto-manager/capabilities/script-decision.md) 复用或新建；脚本用文件编辑工具写入。
3. 根据任务模式执行编译/运行；日志、结构和视觉质量分别报告。画面质量由人工在 Editor 判断。
4. 场景/资源地址来自用户输入或 `python3 .mcp/validation/project_resources.py path <id>`；配置不赋予额外修改权限。
5. 仅恢复本流程改过的状态；关闭/重启 Editor 需考虑未保存内容和任务授权。

完整相机 PNG 用 [unity-render-debug](../unity-render-debug/SKILL.md)，相对耗时用 [unity-render-benchmark](../unity-render-benchmark/SKILL.md)。
临时工作按 [产物规范](../../knowledge/meta/interfaces/task-artifacts.md) 归属；可复用 Editor 操作代码归本 skill。
