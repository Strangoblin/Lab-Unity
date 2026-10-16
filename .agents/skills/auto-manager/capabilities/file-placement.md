# 文件放置

G3 将文件类型、任务类别、用户工作区与项目资源配置合并为实际落点；资源映射不替代写入授权。
一次性脚本/计划/截图/日志按 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 放任务目录。

## 解析正式源码目录

使用 `python3 .mcp/validation/project_resources.py path <id>`；`list` 可查看配置 ID。

| 类别 | 资源 ID | 文件落点 |
|---|---|---|
| 全屏后处理 | category.PostProcess | <已解析根>/<effect>/ |
| 3D 材质 | category.Render | <已解析根>/<effect>/ |
| 视觉效果 | category.Graph | <已解析根>/<effect>/ |
| C# 逻辑 | category.Script | <已解析根>/<module>/；Editor 文件在模块 Editor/ |
| 通用 HLSL | library.hlsl.shared | 按 [公共函数归属](../templates/shader/hlsl/README.md#公共函数归属) 判断，不因扩展名直接入库 |
| Roslyn 脚本 | — | 按 [脚本决策](script-decision.md) 选择 skill 脚本库或任务 staging/ |

.compute / 私有 .hlsl 与所属 Shader 同目录；功能项目公共流程留在功能目录。
将候选路径与本次 `gate_set_workspace` 边界对照；跨目录需要现有扩展流程。缺少映射时报告缺失 ID，由用户指定目录，不能默认为历史资产根。

## 输出与检查

输出 `G3: File Placement`，字段为 `FileType / Category / TargetDir / Exists`；`Exists` 是实际查看目标目录后的结果。
同名文件非任务目标时先核对用户范围；不覆盖其他工作。写入按 [执行契约](execution-contract.md) 验证内容规范、项目落点和授权边界。
