# 场景配置

通过 [unity-editor](../../unity-editor/SKILL.md) 的 Roslyn 命令在当前 Editor 查询或配置场景。
执行脚本前按 [script-decision](script-decision.md) 查库；复用入口见 [场景脚本索引](../../unity-editor/scripts/roslyn/README.md)。

临时脚本执行：`unityctl script execute -f tmp/sessions/<date-task-id>/staging/setup_xxx.cs`。
多程序集或 eval 约束先查 [Roslyn 命令](../../unity-editor/references/cli/roslyn.md)。

## 资源与状态

- 可查询组件、设置公开字段、调用 public 方法、创建本任务需要的 GameObject/组件。存量删除/层级调整沿用任务授权。
- 场景本身已有未提交改动（Git `M` / 编辑器 dirty）时只读查询或改副本；就地改并 `SaveScene` 会覆盖用户工作，判定与例外见 [unity-editor 场景改动边界](../../unity-editor/SKILL.md)。
- **`SaveScene` 会把运行时状态一起序列化**：跟随目标的位置、捕获相机的位置与 `orthographicSize`、Manager 的 `_areaSize`、Manager 内部创建的 RenderTexture 等。存完立刻 `git diff <场景>`，只该出现本任务真想改的行；出现别的就把作者值写回去再存，且**派生状态要一起写回**（相机跟着跟随目标走，只改一处会留下不一致）。实例：一次保存把 `_areaSize` 12→20、跟随探针 y 0.30→0.14，两个验证从全绿变红，硬台阶稳定出现在域内 uv≈0.6 而非边界。
- Shader/Material/ComputeShader/RenderTexture 字段优先绑定已存在且契约匹配的项目资源；地址通过资源接口或用户输入解析，不先造空资源再回填。
- 调试期临时资源引用可用 `DEBUG_REF` 标识，记录原值；设计结束恢复/清空本任务临时引用，不清除其他人的字段。
- 创建本任务测试物体时即挂在功能父容器下，例如 InstanceManager/FishTest；存量物体先查用途，不按 Test/Temp 名称自动改层级。
- 查询候选不等于整理授权。`scene-organize.cs` 有修改层级/标脏副作用，核对精确对象与授权后才可使用。

## 收尾

按 [cleanup](cleanup.md) 与 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 处理自建对象、脚本和必要证据。
可复用操作脚本归对应 skill 的 scripts，角色只记录关联与验证结论。
