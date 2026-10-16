# 知识路径合同

稳定 ID 为 `domain/<相对该领域根的路径>`；仓库路径统一 POSIX 相对形式。

| domain | 实体根 |
|---|---|
| `meta` | `.agents/knowledge/meta/` |
| `unity` | `.agents/knowledge/unity/` |
| `rules` | `.agents/knowledge/rules/` |

解析必须唯一、存在；未知和重复明确失败，不猜替代文件。basename 只作唯一命中的兼容搜索，工具优先使用完整 ID。
实现为 `.mcp/validation/knowledge_paths.py`：从脚本 / 项目结构、Git 根或有效 PROJECT_ROOT 确定项目根，不写死机器路径。
结果包括 project_root、knowledge_id、canonical_relative_path、resolved_path；审计使用仓库相对位置。历史记忆路径不参与当前知识 ID 发现。
