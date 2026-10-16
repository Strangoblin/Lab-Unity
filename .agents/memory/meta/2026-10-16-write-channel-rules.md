# 写入通道按域分 + 生产类工具框架入库

## 用户目标

1. 生产类工具若不统一框架，则总结并放入 skill。
2. 查看以前写入 memory 的方法；如新结构已不适合写入，可进入 meta 模式更新写入规则。

## 事实：门禁从未覆盖体系库，但文档说反了

- `write_gated` 对 `.agents/memory/unity/*.md` 返回 `PATH_NOT_ALLOWED` —— 这是 **2026-09-10 的刻意裁决**（[meta-mcp-bypass](2026-09-10-meta-mcp-bypass.md)：`meta-developer` 与 `.agents/**` 绕过 Unity MCP，避免门禁授权修改自身的循环依赖），并由 `.mcp/tests/test_recipes.py::test_meta_bypass_boundary` 与 `test_knowledge_paths.py` 钉住。**不是三库收敛引入的回归**，作用域在收敛前后都是 `Assets/Mine` + roslyn 脚本 + `tmp`。
- 真正失效的是**规则文本**：`execution-contract.md` 原标题「唯一操作通道」并断言「平台 deny/hook 也拦截 Bash 的 rm/mv/cp/写入型 sed/tee/重定向」。实测 `.codex/hooks/guard-bash.sh` / `.claude/hooks/guard-bash.sh` 只封三条红线（`git stash --all`、`rm -rf Assets/Mine/`、`gate_cli.py workspace approve|reject`），**不通用拦截 Bash 写入**。于是「门禁不覆盖体系库」+「以为 hook 会兜底」两条合起来 = 会话在 memory 写入上停滞，只能回头找用户确认。`memory/README.md` 当时完全没有写入规则。
- 旁证（未处理）：`.agents/memory/**` 当前 **0 个文件被 git 跟踪**，三库收敛的移动未提交 —— 记忆只存在于工作区。

## 变更

| 文件 | 变更 |
|---|---|
| `skills/auto-manager/capabilities/execution-contract.md` | 「唯一操作通道」→「操作通道 — 按域分」：显式写清「Unity 受管资产走门禁三通道」与「体系库用平台文件工具、不经 Unity MCP」；修正 hook 表述为实际三条红线；注明不要拓宽 `ALLOWED_PREFIXES` 来"修"体系写入 |
| `knowledge/meta/architecture.md` | 「项目集成」补写入通道按域分，指向钉住该边界的测试 |
| `memory/README.md` | 新增「写入规则」表：通道 / 位置 / 命名 / 索引 / 内容 / 归档 |
| `skills/auto-manager/templates/script/production-tool/` | **新增**：`README.md`（框架契约、三件事分工、采用步骤、与其他族边界）+ `production-tool-window.cs`（消费者骨架，不是框架副本） |
| `templates/script/README.md`、`templates/README.md` | 新增 Production Tool 族；把「输出路径初始为空、由用户选择」改为「固定资产路径，Save = Load 同一来源」 |
| `templates/script/window/README.md` | 同上修正，并指向 production-tool 基类；保留手写形态给不产出资产的窗口 |
| `templates/script/baker/README.md`、`generator/README.md` | 窗口侧改为优先继承 production-tool 基类 |
| `.mcp/project-resources.json` + `knowledge/meta/interfaces/project-resources.md` | 新增 `script.production-tools` 绑定，避免 skill 硬编码实现路径 |

架构选择：**不拓宽 `ALLOWED_PREFIXES`**。把 `.agents/**` 纳入门禁等于把两个域混成一个，并回退 2026-09-10 的裁决、打断两条既有回归测试；`gate_request_workspace` 也要求目标先落在基础作用域内，故体系写入没有门禁通道可言 —— 正确做法是把既有的「直接维护 + 独立验证」写成规则。

## 验证

- `python3 .codex/tests/06_architecture/verify.py --strict` → PASS（断链 0、skill drift 0、platform hook drift 0、绝对路径 0）。
- `python3 .mcp/tests/run_isolated.py` → 四套件全 PASS，且**真实 `.mcp` state/audit 指纹未变**（生产状态未受影响）。
- `python3 .mcp/validation/check_norm.py` 对本次全部改动文件通过。
- `python3 .mcp/validation/project_resources.py path script.production-tools` → `Assets/Mine/Scripts/ProductionTools`。

## 可复用结论

- **边界说不清时先读测试**：`.mcp/tests/` 里钉住的是裁决本身（含负断言），比散落的散文规则可靠。本次若照「报错就修配置」的直觉放宽白名单，会静默回退一个已裁决的架构边界。
- **文档断言要与 hook 实现对齐**：散文里「平台会拦」这类断言必须能指到具体规则；`.codex/tests/06_architecture/verify.py` 已有 `platform_hook_drift` 守两平台 hook 逐字一致，但没有守「文档对 hook 能力的描述」——这是本次缺口的形态。
- 体系库写入的验证配方（memory 记录也适用）：改完跑 `06_architecture/verify.py --strict` + `.mcp/tests/run_isolated.py`，两条都是隔离或只读，不动生产状态。

## 限制与后续

- `.agents/memory/**` 未纳入 git：记忆是结论的唯一副本，未跟踪即未归档；提交由用户决定，本次未动其脏改动。
- 未做：若日后要让门禁覆盖体系库，需先改 `.mcp/tests` 两条负断言与 2026-09-10 裁决，属独立决策，不在本次范围。

## 补充（同一会话的后一段）

- **体系库写入没有审计与内容规范检查**（门禁不覆盖，这是设计而非缺口）。是否值得为体系库单开一个
  *不挂在 Unity MCP 上* 的独立检查通道，属 meta 决策；不要塞回 Unity gate。
- `ALLOWED_PREFIXES` 未变 → `scope.path` 仍是 `block` 且 `retirable: false`，本次不触发 E5 调级流程。
- **自身事故一条**：同一会话因上下文分段，后半段重复写了同一批 memory —— 自造重复文件（`-rule` 与 `-rules` 并存）
  并覆盖了自己前一段写好的 unity 记录。教训：收尾前先确认记录是否已存在（读领域索引，或 `stat -f '%SB'` 看目标创建时间），
  再决定写；`cat >` 是无提示覆盖。
