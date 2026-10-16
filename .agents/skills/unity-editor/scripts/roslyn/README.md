# Editor Roslyn 脚本索引

| 脚本 | 用途与副作用 |
|---|---|
| [scene-query.cs](scene-query.cs) | 查询当前场景层级与组件数量 |
| [pipeline-check.cs](pipeline-check.cs) | 查询渲染管线与 Quality Level |
| [scan-temp-objects.cs](scan-temp-objects.cs) | 按名称查候选对象；名称不构成删除/整理依据 |
| [scene-organize.cs](scene-organize.cs) | 按名称匹配调整层级并标脏；仅在已核对精确对象且任务授权时执行 |

执行签名及程序集约束见 [Roslyn](../../references/cli/roslyn.md)。
完整相机 PNG 脚本由 [unity-render-debug](../../../unity-render-debug/SKILL.md) 集合。
新增通用 Editor 脚本在本目录登记；功能专属适配器留功能目录，通过项目资源接口发现。
