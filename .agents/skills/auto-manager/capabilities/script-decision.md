# 脚本决策

G2 在实现前判断是否需要脚本，再查对应 skill 的实际脚本索引。决策是流程输入，g_script 门禁已退役；写入时经 script_decision 注解记录。

1. 不需要脚本 → NONE。
2. 查 [Editor Roslyn 库](../../unity-editor/scripts/roslyn/README.md) 或目标 skill 的 scripts，确认功能、输入、程序集依赖和副作用是否匹配；有等价脚本 → USE，不新写一套。
3. 无等价脚本且可复用 → CREATE reusable，放负责该操作的 skill/scripts；通用 Editor 脚本在 `.agents/skills/unity-editor/scripts/roslyn/`。
4. 一次性脚本 → CREATE tmp，放 `tmp/sessions/<date-task-id>/staging/<name>.cs`。

输出 `G2: Script Decision`，包含 `NeedScript / LibraryCheck / Match / Decision / Path`。LibraryCheck 必须来自实际索引/目录查询，不能未查库就声称“无等价脚本”。

相机 PNG 由 [unity-render-debug](../../unity-render-debug/SKILL.md) 集合 [capture 脚本](../../unity-render-debug/scripts/camera-offscreen-capture.cs)。
新增复用脚本更新其所属 skill 的脚本索引；功能专属适配器留功能目录，通过资源接口关联。
临时脚本按 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 收尾，`tmp/.reusable/` 为遗留目录，不再作为新库。
