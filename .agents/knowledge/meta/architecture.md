# Agent 架构约定

本文件是目录职责、角色格式和引用约定的唯一事实来源；操作步骤由 agent-maintenance 持有。

## 四个落点

| 落点 | 内容 |
|---|---|
| `agents/` | 角色清单和 AGENT；定义范围、取用入口、记录职责 |
| `memory/` | 当前上下文、按日期的结论、必要历史证据；按 meta / unity 归属 |
| `knowledge/` | 事实、规范与合同；meta / unity 为领域，external 只登记来源 |
| `skills/` | 工作流、脚本、命令参考、模板与技能局部说明 |

平台只维护配置、权限和明文路由；`.mcp` 持有实际门禁和项目资源绑定。Unity 运行时内容归功能，不依赖 agent 库。

## 单向取用

各平台入口 → `agents/README.md` 清单 → 角色及所需 memory / knowledge / SKILL。清单直接给入口，不强制逐层读取总 README，也不预加载三套库。
每个概念只写一处正文。索引向正文路由，正文不为“被索引”添加反向引用；相关资源确有依赖时才链接。
实体路径唯一；全部入口为普通文本，不使用软链、发现镜像或旧资源逐文件跳转壳。

Unity 任务首次分派收口到 auto-manager 的 SKILL.md，再由内部节点选择开发模式或专门 skill；角色和总清单不直接路由到其 modes / capabilities / templates。模式持有阶段顺序，能力持有操作细节；已分派任务不因内部依赖反跳入口。知识与模板的契约引用、skill 间的真实执行依赖可直接连接，不把所有引用强制改成逐层跳转。

## 角色格式

AGENT 只含元数据 name / description（平台支持时可加 tools / model）以及“范围、取用、记录”三点，不再追加重复自描述/manifest。
取用字段直接提供 memory、knowledge、skill 的规范路径；角色不包含执行命令、实现代码或模式/编译/恢复流程。
记录区声明何时记、归哪一库、证据与未验证项；实际流水线由技能完成，角色消费结果回写。
完整角色模板由 agent-maintenance 的 assets 持有，不在知识库再保存一个模板壳。

## 路径与历史

活动链接按所属文件目录使用相对路径；工具知识 ID 以 [解析合同](interfaces/knowledge-paths.md) 为准。移动活动文件同步其读者，不写机器绝对路径。
历史 dated memory、证据与不可变基线保留原字节。历史路径表示当时位置，旧相对链接需结合迁移记录回放，不作为当前查找或写入配置；不恢复软链弥补历史点击路径。
入口保持短；细节按需读取。按内容独立性拆文档，不因行数硬拆成许多交叉引用小文件；完整实现放 skill 脚本/模板。

## 项目集成

资源位置按 [输入合同](interfaces/project-resources.md) 取得，与写入授权分离；取得位置不授权修改。实际权限和人工批准仍由原门禁 / 平台机制执行。

写入通道按域分：Unity 受管资产（`Assets/Mine/**`）走 `.mcp` 门禁三通道；体系库（`agents/` `memory/` `knowledge/` `skills/`）
走平台文件工具，不经 Unity MCP —— 边界由 `.mcp/tests/test_recipes.py::test_meta_bypass_boundary` 钉住，
拓宽门禁作用域来"解决"体系写入是把两个域混成一个。
策略强度与判定实现分离，不用目录迁移改变 block/warn/off。资产 GUID、业务功能与专属验证器由项目功能管理。
临时与长期产物遵循 [生命周期合同](interfaces/task-artifacts.md)。
