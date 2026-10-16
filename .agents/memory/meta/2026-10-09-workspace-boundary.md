---
name: workspace-boundary
description: 任务级工作目录边界 + 「agent 提出、人工批准」的可执行分权（批准入口对 agent 封闭）
date: 2026-10-09
---

# 2026-10-09 — 工作目录边界与「批准权在人」的机制牙齿

## 需求

> 「为了防止 agent 任意放置文件，需要在一开始就确认工作文件夹，例如指定
> `/Users/xiaokangji/Unity/Lab/Assets/Mine/Scripts` 则操作只允许在这里完成，
> 如有过程中有多工作区需求，可以由 agent 提出并人工批准。」

## 设计

**两层作用域，方向不同**：基础作用域（`validate_path`，既有）回答「框架允许写哪里」；
工作目录（新）回答「**这次任务**只许动哪里」—— 是**收窄**，不是放宽。

- `gate_set_workspace(path)`：任务开始声明。path 必须先落在基础作用域内（`check_workspace_roots`），
  否则 `WORKSPACE_NOT_ALLOWED`。**边界只收窄**这个方向由校验保证，不能靠自觉。
- 生效范围：`policy.json` 的 `workspace.applies_to`（默认 `Assets/Mine/`）。
  `tmp/` 等暂存区不受约束 —— 暂存不是「放置文件」。
- 三操作全约束：write / delete / move；**移动两端都要在边界内**（搬进搬出都改了边界外的内容）。
- 级别走 policy（`scope.workspace`，`block`，`retirable: false`）—— 与安全类同档，不因 agent 可靠而放开。

## 「人工批准」必须做成真的分权，而不是一句约定

这是本轮最重要的结构判断。若批准入口也是一个 MCP 工具，agent 就能自己批准 ——
「人工确认」退化为装饰。故拆成三面：

| 面 | 归属 | 机制 |
|---|---|---|
| 登记 | agent | `gate_request_workspace(path, reason)` → `NEEDS_APPROVAL`，**不授权** |
| 批准 | 人 | `gate_cli.py workspace approve/reject`（CLI 通道） |
| 封闭 | 平台 | `guard-bash.sh` **C3** 规则：agent 的 Bash 调 `workspace approve/reject` 直接 exit 2 |

批准写磁盘，MCP server 进程**经 `workspace_view()` 合并磁盘 roots** —— 否则会出现
「用户批了、agent 重试仍被拒」（roots 取并集 = 只增不减的安全方向；pending 以磁盘为准）。
请求 / 批准 / 拒绝全部落 `friction.jsonl`：自批会留痕。

通则（已写进 `references/enforcement-policy.md` §5.5 与 `rules/meta-architecture.md`）：
**凡「agent 提出、人批准」的动作，批准入口应当是 agent 够不到的通道 + 审计，而不是 skill 里的一句要求。**

## 落地清单

代码：`project_paths.py`（`check_workspace` / `check_workspace_roots` + 抽出 `ALLOWED_PREFIXES` 与 validate_path 同源）、
`gate_center.py`（`workspace` 状态 + set/request/approve/reject + `workspace_view` 磁盘合并 + 持久化/恢复/reset）、
`judgement.py`（`_workspace_deny` 统一施加于三操作 + `workspace_from_file`）、
`server.py`（`gate_set_workspace` / `gate_request_workspace` + status 带 workspace）、
`gate_cli.py`（`workspace set|request|approve|reject|show`）、`policy.json`（`scope.workspace` + `workspace` 段）、
`.claude/hooks/guard-bash.sh` + `.codex/hooks/guard-bash.sh`（C3）。

文档：`auto-manager/SKILL.md` + `modes/{production,experiment,research}.md`（新增 **[G0.5] 工作目录确认**）、
`mcp-gate-usage.md`（工具/流程/DENIED 码/边界节）、`.mcp/README.md`（边界节 + 分权表）、
`enforcement-policy.md`（控制律加「权限类」+ §5.5）、`rules/meta-architecture.md`、meta `AGENT.md` 清单、
`.claude/CLAUDE.md`、`.codex/AGENTS.md`（规则 7）+ `INTERFACE.md`、`interfaces/project-structure.md`。

测试：`test_workspace_scope`（未声明/越界/tmp 豁免/收窄/删移同约束/reset 清空）、
`test_workspace_request_approval`（reason 必填/非法目标/request≠grant/CLI 批准生效/reject 移出/show）、
`test_workspace_policy_required`；`test_write_gated_placement_wired` 补声明；
`test_restart_recovery` 增加工作目录恢复断言。

## 顺带修掉的一个问题

测试套件写的是**生产**摩擦账本 —— 跑几次测试就把 E5 信号淹没在噪声里。
已改为写 `tempdir/mcp-friction-test.jsonl` 并在收尾删除；生产账本（`.mcp/friction.jsonl`）清空重建。

## 验证

- `.mcp/tests/test_recipes.py` 全绿（新增 3 组 + 2 处补充断言）；`test_knowledge_paths.py` PASS
- `.codex/tests/06_architecture/verify.py` PASS（断链 0）
- hook 实测：`workspace approve` → exit 2；普通 `gate_cli write` → exit 0（两平台 hook 一致）
- 端到端实测：未声明 → `WORKSPACE_NOT_SET`；越界 → `WORKSPACE_VIOLATION`；request → 仍拒；
  CLI approve → **运行中的 server 立即放行**（磁盘合并生效）；tmp/ 豁免

## 偏离与未做

- `workspace.required` 默认 **true**（用户明确要求「一开始就确认」）——这改变了行为：
  未声明工作目录时，`Assets/Mine/**` 的写入会被拒。已在 skill 流程加 [G0.5] 兜住。
  若要回到宽松态，改 `policy.json` 一处即可（数据，不改代码）。
- pending 以磁盘为准 → 若用户在本机 reject，运行中的 server 视图可能短暂保留该 pending 项
  （不会授权，approve 才是授权路径）；未加实时失效通知。
- 工作目录未做「读写分离」：读不受限（用户关心的是放置，不是查看）。
