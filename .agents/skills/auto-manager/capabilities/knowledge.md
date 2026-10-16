# 知识预加载

通过 [Unity 知识索引](../../../knowledge/unity/README.md) 渐进取用事实知识；只读取当前任务需要的 reference、模板和实现，不扫描整个共享目录。
知识正文根为 `.agents/knowledge/unity/`，模板由本 skill 的 [templates](../templates/README.md) 提供。

## 取用顺序

1. 读取角色/领域索引并核对当前会话已读文件，避免重复读取。
2. 读取门禁高优先级结构规范：`standard/script/script-structure.md`、`standard/shader/shader-structure.md`。
3. 按下表追加任务相关材料，提取命名、注释、文件结构、API 差异与集成约束。
4. 规范声明的参考实现若与目标类型匹配，至少读取一个。实现位置由配置的 `category.*` / `library.hlsl.shared` 或用户提供路径解析；不从历史路径猜工作区。

| 任务 | 追加 reference（相对知识正文根） | 模板 |
|---|---|---|
| 普通 Shader / HLSL | standard/shader/ 与 hlsl-includes.md | ../templates/standard/shader/ |
| 纯 C# | standard/script/ | ../templates/script/ |
| 全屏后处理 | shader/postprocess/fullscreen-structure.md、blit-fullscreen.md、postprocess-differences.md | ../templates/shader/postprocess/ |
| RendererFeature / RenderPass | shader/postprocess/feature-script-structure.md、standard/rendering/render-graph.md | ../templates/shader/postprocess/ |
| 屏幕空间 Compute | shader/postprocess/compute-shader.md | ../templates/shader/postprocess/ |
| Metal / API 疑点 | platform/metal-notes.md、相关 standard/compute/ 或 rendering/ | 按需 |

长文先通过索引/标题定位，再读取相关段落；缺少必读材料应报告路径缺口，不能伪造已读记录。
Research 按目标取用，Production 读取上述门禁必读和相关领域，Experiment 结合联网方案取用库内约束；不将“全量”理解为整个知识库。

## G1.5 判定

输出 `Knowledge Check — Loaded: <真实已读文件列表> | Status: COMPLETE`，并按 [执行契约](execution-contract.md) 同步工具。

| 情况 | 实际门禁行为与后续 |
|---|---|
| 两份高优先级结构规范已读 | COMPLETE，进入下一步 |
| 漏读高优先级 | 当前策略 warn，响应 RELAXED 并记账；流程仍补读后进入设计 |
| 声明路径无法解析 | G15_UNRESOLVED_FILE，仍为 block |

知识 ID 和路径解析以 [knowledge-paths](../../../knowledge/meta/interfaces/knowledge-paths.md) 为准；只申报已读取的真实文件。
