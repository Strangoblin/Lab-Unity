# 项目资源输入契约

通用角色和技能按资源 ID 取用项目内容，不自行推导具体资产目录。资源位置与可写权限分别管理。

## 接口

项目集成层提供资源解析器；本仓库实现为 `.mcp/validation/project_resources.py`，位置绑定为 `.mcp/project-resources.json`。

| 用途 | 接口 |
|---|---|
| 查询可用绑定 | `python3 .mcp/validation/project_resources.py list` |
| 取仓库相对路径 | `python3 .mcp/validation/project_resources.py path <resource-id>` |
| 指定项目 / 绝对路径 | `--project-root <root>` / `--absolute` |
| Python 工具 | `resource_path(id, project_root=..., must_exist=True)` |

解析器拒绝绝对配置、路径穿越与逃逸软链；未知 ID、缺失配置和不可用资源明确失败。只读位置映射，不写资产或创建默认资源。`list` 可列出已绑定但当前不存在的资源。

## 资源语义

| ID | 含义 |
|---|---|
| `asset.root` | 当前项目业务资源根；实际授权仍由门禁独立校验 |
| `category.PostProcess` / `category.Render` / `category.Graph` / `category.Script` | 各功能族候选落点；须与用户工作区取交集 |
| `library.hlsl.shared` | 项目通用 HLSL 库候选入口 |
| `unity.debug-output-feature` | 可选项目调试显示适配器 |
| `benchmark.ssgi.adapter` / `benchmark.ssgi.contract` | 项目已有计时适配器及合同 |
| `hash.*` | 既有项目 Hash 验证器的当前源绑定 |
| `validation.hash.guide` / `validation.hash.adapter` | 项目 Hash 迁移合同验证器的入口文档与运行器 |
| `validation.production-tools.adapter` | 项目生产工具五窗口回归适配器 |

移植到其他项目时更换绑定或提供等价解析器，不修改通用 skill。功能专属接口说明仍由实际功能文档维护，技能只依赖资源的用途与输入契约。

## 权限边界

资源配置不能改变 `.mcp/validation/project_paths.py`、policy 或平台权限。取得路径不是写入授权；用户任务范围、写删移门禁与人工批准要求仍照原规则执行。缺少绑定不得回退到更宽的资产根。
