---
name: mcp-v5-git-protected-writes
description: 用户裁决 v5：记忆可原地改、沙箱可由 agent 申请扩充、移动走原生 mv、git 保护即可随意编辑；判定从「是哪个目录」改成「有没有兜底」。
date: 2026-10-28
---

# MCP v5：从「按目录定责」到「按兜底定责」

v4 上线当天就被两条真实拒绝打回：`AGENTS.md` → `ZONE_DENIED`，既有记忆记录 →
`MEMORY_HISTORY_IMMUTABLE`。用户给的四句原话就是 v5 的设计输入：

> 「记忆沙箱同样需要可随意编辑」「沙箱可以在用户指定某一个地址后，由 agent 申请扩充（对话框确认就行）」
> 「除了读写，移动等其他操作也尽量让 agent 用原生工具自己跑，删除用垃圾桶倒也行」「整个项目大部分都处于 git 保护，只要处于 git 保护，就可以认为 agent 可以随意编辑」

## 判据替换：兜底原则

v4 问「这个路径属于哪个区」；v5 问 **「改错了能不能回来」**：

```python
def _git_protected(rel, zone):
    return zone in git_allow_zones() and is_git_tracked(rel)
```

- `git ls-files -z -- <path>` 命中 ⇒ 有版本库兜底 ⇒ 放行 + `record`；
- 未入库（含**新建**，新建必然没有兜底）⇒ 维持原拒绝。

用 `ls-files` 而不是 `status --porcelain`：要求 clean 会让「刚写过的文件」在第二次写入时
突然失去保护，等于把保护变成一次性消耗品。

**`effective_zone()` 只升 `denied`，不碰 `asset`。** 已入库的资产即使靠 git 放行，
`zone` 仍报告 `asset` —— `close.py` 是按 `zone == "asset"` 挑 `compile` / `verify` 义务的，
改判成 `system` 会让这两项义务静默消失。这条写进了函数 docstring，因为它不直观。

## 四项变更

| 项 | 变更 |
|---|---|
| 记忆区 | `memory.history-immutable` 整条退役（含 `MEMORY_HISTORY_IMMUTABLE`、`_CHECK_IDS`、`INTRINSIC`、`policy.json` 条目、`zones.json` 的 `historical_memory_patterns`）；既有 dated 记录可原地改 |
| 扩充通道 | 新 `cli.py expand --workspace <目录> --reason <用户原话>`：写进 `session.json` 的 `expansions[]` 和 `friction.jsonl`，**不重置 `opened_at`** —— 否则扩充能顺手抹掉证据起点 |
| 移动 | `mv` 从 bash 破坏性动词表移除，规则改成「移动/改名直接用 mv，`.meta` 一并搬」；只有删除/截断仍被重定向到 trash |
| 分区 | `zones.json` v5 新增 `git_protected.allow_zones: ["denied", "asset"]`，把「哪些区问 git」变成数据 |

`expand` 只**记录**不**审批**：v3 那套「register + 人工 CLI 审批」在 1261 条 friction 里
一次都没被用过，确认其实发生在用户看得见的对话框里。这点写进了 `enforcement-policy.md` §5.5。

## 实现细节

- 判定仍只有一处（`zones.py`）：hook 与 CLI 共用 `effective_zone()` / `evaluate_write()`。
- `is_historical_memory()` 跟着 `_history_patterns()` 一起删掉了 —— 它已无调用方，且真被调用会
  `NameError`。**退役一条规则要连同它的判定函数一起退役**，否则留下的是「看起来还在管」的死代码。
- `_FALLBACK_GIT_ZONES`（读不到 `zones.json` 时的兜底）同步成 `["denied", "asset"]`，与文件一致。
- hook 异常兜底方向按实际区分叉：`zone in ("asset", "denied")` ⇒ deny；其余 ⇒ allow。
- `record_read` 也走 `effective_zone()`，读已入库的仓库根文件因此算知识证据。
- 读 git 有 20s 超时 + 进程内缓存（hook 是热路径）；超时按「不受保护」处理（fail closed）。
- 隔离 runner 增加了 git 基线提交 —— 空仓库里 `git ls-files` 什么都看不见，v5 的保护会变成不可测。

## 踩到的坑

- `test_effective_zone_only_lifts_denied` 拿 `Assets/Scenes/RenderScene.unity` 断言，但隔离
  fixture 根本不拷 `Assets/`，`ls-files` 当然看不见 —— 断言改成 `AGENTS.md`（表内判 `denied`、
  实际判 `system`），既真实又更贴合「表 vs 实际」。
- `policy.json` 手工编辑时误删了 `"close.knowledge": {` 这一行 key，留下孤立的 level/why 块；
  由 JSON 解析报错才发现，已补回。

## 已知残留（接受，不加检查）

`mv` 放开后，把**未入库**的新资产 `mv` 到仓库外，等价于一次既没有垃圾桶、也没有 git 兜底的删除。
没有补规则，理由有两层：

1. 判它要做 `mv` 参数解析 + 源路径 git 查询，正是 v4 friction 里 5 次误拦的同类写法（bash 层语法
   分析天然脆）；
2. 方向是「拦得越少，留的证据越多」。真要做不可逆的搬运，`cli.py move` / `delete` 仍在，agents
   文档也仍然指向它。

如果这条真的被踩到，正确做法是给 `mv` 加**源未入库 → 目标在仓库外**这一条窄规则，而不是回收全部 `mv`。

## 文档同步

`AGENTS.md`（安全边界改成「只看改错能不能回来」）、`.mcp/README.md`、`execution-contract.md`、
`mcp-gate-usage.md`、`memory/README.md`、`enforcement-policy.md`（§4 兜底原则 + §5.5 + §5.7）、
`architecture.md` §项目集成、`debug-resource-layout.md`、`task-artifacts.md`、`AutoMode.md`、
`production.md`、`cleanup.md`（删除/移动通道拆开）、`file-placement.md`（沙箱只管新建与未跟踪）、
两个 AGENT.md、`codex-opencode-go/SKILL.md`。

`2026-10-28-mcp-v4-business-management.md` 与 `2026-10-28-repo-root-files-have-no-zone.md` 按
「记忆可原地改」直接修订，并在开头标明哪几条已被 v5 取代 —— 这正是 v5 想解决的那种写法。

## 验证

- `python3 .mcp/tests/run_isolated.py` → 4/4 PASS，生产账本与垃圾桶指纹未变。
- `python3 .codex/tests/06_architecture/verify.py` → PASS（`platform_hook_drift=0`、
  `mcp_hardcoded_claude_files=0`、`skill_drift_files=0`、`agents_broken_markdown_links=0`）。
- 新增用例：`effective_zone` 只升 denied、既有记忆记录原地改、已入库仓库根文件可写、
  git 事实靠问不靠猜、已入库资产绕过沙箱、未入库资产仍被拒、`mv` 走原生、
  读已入库文件算知识证据、`expand` 不重置证据起点且幂等。

## 相关

- v4 改造与依据：[2026-10-28-mcp-v4-business-management.md](2026-10-28-mcp-v4-business-management.md)
- 「根级文件没有分区」这个 v4 遗留问题由本条解决：[2026-10-28-repo-root-files-have-no-zone.md](2026-10-28-repo-root-files-have-no-zone.md)
- 策略侧：[enforcement-policy.md](../../knowledge/meta/enforcement-policy.md) §4 兜底原则、§5.5、§5.7
