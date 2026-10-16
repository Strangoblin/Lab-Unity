---
name: Write Channel Rule — Unity Gate vs Framework Libraries
description: 体系库写入不经 Unity MCP；修正执行契约里「唯一通道」表述与一条不成立的 hook 断言。
date: 2026-10-16
---

# 体系库写入通道规则修正

## 触发

生产类工具会话收尾时，unity-developer 按 AGENT.md 要求写 memory，得到
`write_gated` → `PATH_NOT_ALLOWED`；改用 `gate_request_workspace(".agents/memory/unity")` 也被拒
（`WORKSPACE_NOT_ALLOWED`：目标不在基础作用域内）。记录只能暂存 `tmp/sessions/.../notes.md`。
用户据此要求：查清以前写 memory 的方法，如新结构已不适合写入则更新写入规则。

## 调查 — 边界是刻意的，不是回归

- **可追到 2026-09-10**：`meta-developer` 与 `.agents/**`、`.mcp/**`、`.claude/**`、`.codex/**` 刻意绕开 Unity 门禁，
  理由是避免「门禁授权修改自身」的循环依赖；`g_entry` 只接受 `unity-developer`。
  见 [2026-09-10-meta-mcp-bypass](2026-09-10-meta-mcp-bypass.md)。
- **合同测试钉住这个边界**（三条）：`test_recipes.py::test_meta_bypass_boundary`（meta-developer 角色 + `.agents/agents/meta-developer/AGENT.md` 均 DENIED）、
  `test_knowledge_paths.py::test_mine_write_scope`（`.agents/agents/meta-developer/AGENT.md` DENIED）、
  `test_roslyn_paths_use_only_canonical_skill_root`（旧 `agents/.../roslyn` 路径 DENIED）。
- **实际通道 = 平台文件工具 / Bash，已实测**：`echo x > .agents/memory/unity/.write-probe.tmp` 成功。
  `.codex/hooks/guard-bash.sh` 只封三条红线（C1 `git stash --all`、C2 `rm -rf Assets/Mine/`、C3 `workspace approve|reject`），
  **并不通用拦截** Bash 的 rm/mv/cp/写入型 sed/tee/重定向。
- **迁移事实**：`.agents/memory/` 整目录 git `tracked=0`（无任何提交），而旧位置 `.agents/agents/<role>/memory/` 有提交历史
  → 2026-10-14 归三库时记忆被搬动、**尚未提交**。

## 结论 — 结构没坏，规则与事实不符

`execution-contract.md` 旧文写「唯一操作通道」，并把 Bash 写入说成被平台 hook 拦截。两处都错：

| 旧表述 | 实际 |
|---|---|
| 「唯一操作通道」 | 只对 Unity 受管资产成立；体系库是另一个域，刻意走门禁之外 |
| 「平台 deny/hook 也拦截 Bash 的 rm/mv/cp/写入型 sed/tee/重定向」 | hook 只封 3 条红线，该断言**不成立** |

这正是 `enforcement-policy.md` 自己警告的形态：**说出来的流程与被执行的流程分离**。
上一次会话因此卡住，是规则失真而非权限故障。

**决策：不拓宽 `ALLOWED_PREFIXES`。** 拓宽会同时破坏上述 3 个合同测试与「门禁不授权自身变更」的设计，
等于把两个域混成一个。`agent-maintenance` 的「不自行放宽平台权限」在此照办。

## 改动

| 文件 | 变更 |
|---|---|
| `skills/auto-manager/capabilities/execution-contract.md` | 「唯一操作通道」→「操作通道 — 按域分」：受管资产走门禁三通道；体系库走平台工具并说明这是 `test_meta_bypass_boundary` 钉住的刻意边界；删除不成立的 hook 断言，改为列出 hook 真正封的 3 条，并点明约束来自规则本身 |
| `knowledge/meta/architecture.md` | 「项目集成」补一段通道按域划分 + 边界依据 |
| `memory/README.md` | 新增「写入规则」表：通道 / 位置 / 命名 / 索引同步 / 内容 / 归档 |

以上三处是**表述修正与规则显式化**，判定代码、policy、`ALLOWED_PREFIXES`、平台权限一律未动。

## 验证

- `python3 .codex/tests/06_architecture/verify.py --strict` → `PASS`（`skill_drift_files=0`、`platform_hook_drift=0`、`absolute_path_hits=0`、`phase2_contract_errors=0`、`architecture_symlink_errors=0`）。
- `python3 .mcp/tests/run_isolated.py` → 4 套合同全部 PASS，且 `real .mcp state/audit fingerprints unchanged`（生产审计未被测试污染）。
- 改动的 3 个 `.md` 均过 `check_norm.py`。

## 遗留与风险

- **`.agents/memory/` 仍是未跟踪**：记录只有进入提交才算归档；目前「未跟踪」等价于删除不可恢复。
  归三库的搬家何时提交由用户决定，本次未代为 commit。
- **体系库写入没有审计与内容规范检查**（门禁不覆盖，这是设计）。是否值得为体系库单开一个
  *不挂在 Unity MCP 上* 的检查通道，属 meta 决策；不要塞回 Unity gate。
- `ALLOWED_PREFIXES` 未变，故 `scope.path` 仍是 `block` 且 `retirable: false`，本次不触发 E5 调级流程。

## 关联

- 本次同时把生产类工具的共享框架总结进 skill（新族 `templates/script/production-tool/`、
  消费者骨架、`templates/script/README.md` 与 `window/` `baker/` `generator/` `templates/README.md` 的
  「输出路径初始为空」旧规则修正为固定登记路径、资源 ID `script.production-tools` 登记）。
  实现与验证证据见 [unity 记录](../unity/2026-10-15-production-tool-framework.md)。
