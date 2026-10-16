# 架构契约：明文入口与三库职责

本测试扫描 `.agents/`、`.claude/`、`.codex/` 和 `.mcp/`，验证明文入口、角色定义和知识 / 记忆 / 技能三库归属，不修改生产门禁状态。

运行：

```bash
./run.sh
python3 verify.py --strict
# 新会话可把报告写到任务目录（从仓库根运行）
python3 .codex/tests/06_architecture/verify.py --strict --output-dir tmp/sessions/<date-task-id>/output/architecture
```

`run.sh` 先执行扫描边界与所有权控制测试。扫描以仓库相对路径判层，排除会话目录、隔离副本、环境缓存及 suite 输出；保留 `.codex/tmp/README.md` 路由检查。真实层的断链、硬编码与漂移仍需通过。

默认与 strict 都要求真实架构源零软链、角色目录仅 `AGENT.md`，Claude / Codex 主入口及 `agents/README.md` 直接指向 `.agents/agents/README.md`；不要求额外发现目录。报告包括：

- `output/migration-inventory.json`：每个 tracked 架构文件的当前路径、语义类别、拟议目标、引用者、迁移决策和理由。
- `output/baseline-findings.json`：断链、软链、漂移、绝对路径、大小写错误、MCP 硬编码与职责错误。

`platform-whitelist.json` 只声明当前分层与四个明文路由，不保存资源 aliases 或软链发现契约。两份现有 Codex 运行时角色 TOML 允许保留短配置路由，不承载共享职责或工作流。知识稳定 ID 的根为 `.agents/knowledge/{meta,unity,rules}`；记忆归 `.agents/memory/{meta,unity}`；脚本 / 工作流归所属 skill。

历史豁免只认明确记忆库中的日期文件和 `evidence/<date-task>/`：相对引用按原目录语境及明确迁移根解释，已退役目标的历史引用保留；不改历史字节。活动知识和记忆索引始终检查断链，文件中出现 historical 等字样不构成豁免。依赖缓存、会话 fixture 与 suite 输出不属于真实架构源。

MCP 脚本授权保留 canonical Editor Roslyn 前缀和 camera 的 canonical 精确文件；旧角色路径、camera 邻文件 / 父目录及软链重定向均拒绝，Assets 范围和策略级别不变。隔离 runner 使用新知识库及 canonical 脚本，不复制生产 Assets 输入。

`output/` 被 `.gitignore` 忽略；`--strict` 额外拒绝活动断链、漂移、硬编码与绝对路径。控制测试覆盖明文路由、四层软链、职责归属、真实活动坏链接与历史字节保留。
