# Roslyn 命令与约束

用文件编辑工具创建 .cs，复用 [脚本索引](../../scripts/roslyn/README.md)，一次性脚本放 tmp/sessions/<date-task-id>/staging/。

| 任务 | 命令示例 |
|---|---|
| 直接表达式 | unityctl script eval 'Application.version' |
| 对象查询 | unityctl script eval --id -1290 'target.transform.position' |
| 场景名 | unityctl script eval -u UnityEngine.SceneManagement 'SceneManager.GetActiveScene().name' |
| 文件 | unityctl script execute <path/to/script.cs>；需要时 -f <path> |
| 文件传参 | unityctl script execute <path> -- Cube 5 'My Object' |
| 类型定位 | unityctl script lookup-type <Name> |
| 成员定位 | unityctl script members <Type> [--filter X] [--static] |
| 超时 | unityctl script eval -t 60 -u UnityEditor '<expression>' |

## 多程序集与 eval

`script execute <file>` 忽略 -u，自动注入仅 System.Text 与 UnityEngine。
涉及反射、IO、URP、Tasks 等时改用 eval，并显式添加 -u；示例：

```bash
unityctl script eval -t 60 -u System -u System.IO -u System.Reflection \
  -u UnityEngine -u UnityEngine.Rendering -u UnityEngine.Rendering.Universal \
  "$(cat <已核对的脚本路径>)"
```

这里只读取已核对的脚本文本作为命令参数，不把脚本内容拼成新的 shell 命令。
eval 使用独立反射程序集，internal 成员不可达；带 lambda/局部函数的内插表达式以及 `x.Sum(p => ...)` 可能被拒。
可异步 eval：`await Task.Delay(500); return GameObject.Find("Boss") != null;`。

## Shader 错误与调试

Shader 编译错误由 ShaderUtil 持有，可能不在控制台日志中。刷新后对任务目标 Shader 读取 ShaderUtil.GetShaderMessages；Shader 名称从实际目标取得，不固定某个效果。

- RendererFeature 约定见 [feature-script-structure](../../../../knowledge/unity/shader/postprocess/feature-script-structure.md)。
- 固定调试输出资源：`python3 .mcp/validation/project_resources.py path unity.debug-output-feature`；实际配置按 [渲染调试](../../../unity-render-debug/SKILL.md)。
- [场景配置](../../../auto-manager/capabilities/scene-setup.md)、[脚本决策](../../../auto-manager/capabilities/script-decision.md) 与 [清理](../../../auto-manager/capabilities/cleanup.md)。
