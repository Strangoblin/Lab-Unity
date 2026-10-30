---
name: mcp-v4-business-management
description: 用户裁决把 .mcp 从门禁式改成业务式：入口观测代替申报、出口按证据验收、删除改为可恢复留底；整线拆除 v3 门禁工具（不留退役壳），并同步全部活动文档与验证脚本。
date: 2026-10-28
---

# MCP v4：门禁式 → 业务式

> 本文记录 v4 当时的形态。同日后续的 v5 已改动其中三处：记忆历史只读、资产区沙箱硬边界、
> 移动走 `cli.py move` —— 见 [2026-10-28-mcp-v5-git-protected-writes.md](2026-10-28-mcp-v5-git-protected-writes.md)。

用户提出四件事：**入口让 agent 读到该读的、用该用的工具、改到正确的文件，工作区内自由操作**。
不再用门禁线检测 / 限制新的业务线，也不保留 `write_gated` 一类旧工具（两者都是明确裁决，不是建议）。

## 判断依据（来自 1261 条 friction 记录）

| 观察 | 说明 |
|---|---|
| 仪式开销比 0.093 | 仪式已不痛 —— 继续加减仪式没有收益，问题不在仪式 |
| 5 次真实误拦全在 `Assets/Mine` 内部 | 沙箱切到叶子目录，跨目录改共享库即被拦：**检查放错了位置** |
| 收尾写记忆 2 次 `WORKSPACE_NOT_ALLOWED` | 记忆库在工作目录边界之外，结构性必然撞墙 |
| CLI 679 / MCP 309 | 写入通道倒挂：内容要先落临时文件再 `--from`，每次多一次往返 |

结论：改模型而非调档位 —— **入口少管，出口多验；能留痕就不拦；能留底就不拒。**

## 变更

### 判定

- `.mcp/config/zones.json`（新）+ `zones.py`：分区表变成数据，`asset` / `memory` / `system` / `scratch` / `denied` 五区。
  顺序是语义（`.mcp/trash` 必须排在 `.mcp` 之前）。`scope.path` / `scope.workspace` 被 `scope.zone` / `sandbox.asset` 取代。
- 记忆沙箱：`.agents/memory/**` 随时可写，已落盘的 dated 记录与 `evidence/` 只读（`memory.history-immutable`），索引可改。
  ~~（v5 已退役该检查，既有记录可原地改）~~
- `profiles.py`：7 条业务线（shader/compute/script/tooling/benchmark/meta/research），**完成义务是数据**。
- `close.py`（新）：出口验收按证据而不是申报 —— knowledge 用 PostToolUse 观测的真实 `Read`；compile 读
  `unityctl logs`，拿不到记 `UNAVAILABLE` 而不冒充通过；verify 要求产物晚于最后一次写入；memory 只在有实质改动时要求。
- `validation/trash.py`（新）：删除 = 搬进 `.mcp/trash/<id>/files/` + manifest（原路径 / git 状态 / GUID 引用 / sha256 /
  目录清单），`cli.py restore` 取回。`tracked-dirty` 与 `guid-referenced` 两条硬拦因此不再需要。

### 通道

- **平台 `Write` / `Edit` 是唯一写通道**，`hooks.py` 在 PreToolUse 判定；**没有 `cli.py write`**。
- `hooks.sh` 是唯一传输层，两平台都注册 `bash <repo>/.mcp/bin/hooks.sh <Event>`；两份 `guard-bash.sh` 进垃圾桶。
  Bash 红线并进 `hooks.py`，`verify.py` 的 `platform_hook_drift` 从「两份逐字一致」改成「都指向同一传输层且旧副本没有回来」。
- 失败语义：`onFailure: block`（超时/崩溃）+ `hooks.sh` 判空（进程活着但吐非 JSON）。**资产区失败即拒，记忆/暂存区失败即放行。**

### 拆除

`server.py`、`gate_center.py`、`gates/`、`validation/{judgement,gate_cli,deletion,write_gated,delete_gated,move_gated,check_norm}.py`、
`state.json`、`test_recipes.py`、`test_directory_deletion.py` 全部进 `.mcp/trash/`（可 restore，不是删除）。
`.mcp.json` 置空，`.codex/config.toml` 去掉 `unity-gate` MCP 注册，`pyproject` 名从 `unity-gate-mcp` 改为 `unity-mcp` 并去掉 `mcp` 依赖。

## 实现期抓到的四个真 bug（都会静默违背四件事之一）

1. `knowledge` 从未出现在任何 profile 的 obligations 里 —— `close` 在必读文件没读时也会 `OK`，即「入口管了、出口没人查」。
2. `close.check_memory` 先按 `zone in (asset, system)` 过滤再匹配记忆记录，记忆区写入永远数不进去 —— 记忆义务永不通过。
3. 账本只记信号名（`deny.MEMORY_HISTORY_IMMUTABLE`）不记 check id，`friction_report` 会把**确实拦过**的检查说成
   「从未命中」，进而提议退役一条正在工作的检查。修法：`zones.check_id_for_error()`，每条 deny 都落 `check=`。
4. Bash 守卫用「命令里出现 `Assets/Mine` 且有 `rm`」判定，会拦下 `rm -f tmp/x && cli.py delete Assets/Mine/...` ——
   而那条命令正是它推荐的写法。改成按 `&& || ;` 切段、跳过含 `cli.py` 的段、只判破坏性动词后的参数区。

另外：`close.*` 被列为「从未命中」，只是因为它从未被评估（没跑过收尾）。报告现在把它们单列为「未评估」。

## 文档同步

`execution-contract.md`（G0–G3 门禁链整篇重写为 open/write/close）、`AutoMode.md`、`knowledge.md`、`script-decision.md`、
`file-placement.md`、`mode-selection.md`、三个 modes、`cleanup.md`、`mcp-gate-usage.md`、`architecture.md` §项目集成、
`enforcement-policy.md`（控制律按 v4 检查项重写 + 记录 2026-10-28 拆除依据）、`harness-patterns.md`、
`template-conventions.md`、`maintenance.md` E5（报告命令改 `cli.py report`）、`agent-maintenance/scripts/*`、
`codex-opencode-go/SKILL.md`（`--auto` 绕过的是判定而非某个工具）、`agents/README.md`、`memory/README.md`、
`rules/meta-architecture.md`、`rules/README.md`、`interfaces/{project-resources,task-artifacts}.md`、
`debug-resource-layout.md`、两个模板 README。

三个 asset 区验证脚本（`SSGI/Validation~/run.sh`、`run_high_budget.sh`、`ScreenSpaceTrace/Validation~/run.sh`）
此前调用 `gate_cli.py write|delete`，已改为 `cp` 落 fixture + `cli.py delete` 收尾。`check_norm.py` 单文件自查随之消失。

## 验证

- `python3 .mcp/tests/run_isolated.py` → 4/4 PASS，生产账本与垃圾桶指纹未变。
- `python3 .codex/tests/06_architecture/verify.py` → PASS，`platform_hook_drift=0`、`mcp_hardcoded_claude_files=0`、
  `skill_drift_files=0`、`agents_broken_markdown_links=0`、`absolute_path_hits=0`。
- 删除通道在拆线前做过真实往返：`server.py` 删除 → restore → 再删除，内容逐字节一致。
- 双平台 hook 已验证：PreToolUse 允许/拒绝形状正确；判定核坏掉时资产区 deny、记忆与暂存区 allow。

## 未验证 / 后续

- 出口验收的 `compile` 依赖 `unityctl logs`；本次未在 Editor 连接状态下跑过完整 `close`，只跑了隔离用例。
- `.codex/config.toml` 与本机 `.claude/settings.local.json` 不在 git 里，改动只在本机生效，其他机器需各自同步。
- 拆除后的 `pyproject` 已无依赖，`.mcp/.venv`（43MB，已 gitignore）可删；未删，避免影响他人本地环境。
- 回退：`.mcp/trash/20261028-125504-a0cbb8` 等四个桶 + `git revert`；v3 源码另有一份在 `/tmp/v4-v3-archive/`（仓库外，重启即失）。
