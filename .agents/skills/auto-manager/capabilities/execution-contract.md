# Unity 执行契约

本文件统一所有模式的沙箱、通道与出口验收。策略档位由 `.mcp/config/policy.json` 与判定核决定，skill 不自行降级。

2026-10-28 起，`.mcp` 是**判定核 + 证据账本**，不是门禁（见 [MCP 使用事实](../../../knowledge/unity/mcp-gate-usage.md)、
`.mcp/README.md`）。入口只声明一次，写入走平台原生工具，交付由 `close` 按证据核对。

## 两种分类，不要混

| 分类 | 取值 | 回答 |
|---|---|---|
| **mode**（工作方式） | Research / Production / Experiment | 怎么推进这一轮（见 [模式选择](mode-selection.md)） |
| **profile**（业务线） | shader / compute / script / tooling / benchmark / meta / research | 该读什么、出口该死磕什么证据（见 `profiles.py`） |

mode 不产生工具调用；profile 决定 `open` 返回的 playbook 与 `close` 核对的义务。

## 一次性打开

```bash
python3 .mcp/bin/cli.py open --task "<一句话说明这次做什么>" --profile shader \
    --workspace Assets/Mine/Shaders/PostProcess/SSC \
    --workspace Assets/Mine/Special/HLSL
```

- `--workspace` 是**用户指定的沙箱**，可重复；跨目录改共享库（如 `Special/HLSL`）在同一次 `open` 里一并给出，不要逐个申请。
- 没有目录授权先问用户，不自行决定。`asset.root` 是资源配置位置，不等于写入授权。
- 沙箱管的是**新建与未跟踪的资产**：已入库的文件有 git 兜底，改错 `git checkout` 取回，不受沙箱拦截。
- 记忆库（`.agents/memory`）与体系库本来就随时可写，也不必声明。
- 再次 `open` 覆盖上一次会话，会重置证据起点；同一任务不要重复调用。

用户中途指了新的地址（对话框里说「那个目录也一起改」）→ 用 `expand` 扩充，**不要重新 `open`**
（`open` 会重置证据起点，等于用扩充换豁免）：

```bash
python3 .mcp/bin/cli.py expand --workspace Assets/Mine/Special/HLSL --reason "用户确认: 共享库要一起改"
```

`--reason` 写用户确认的原话 —— 这条命令不设「申请/批准」两段，确认发生在对话里，命令只负责留痕。

## 写入通道

**平台原生 `Write` / `Edit`**，由 PreToolUse hook 判定。没有 `cli.py write`。
读、写、移动、改名都用原生工具：`mv` 会顺带搬 `.meta`，GUID 随文件走，本来就安全。
**只有删除要进垃圾桶**（`cli.py delete`：留底、`.meta` 成对、可 `restore`）。

| 区 | 规则 |
|---|---|
| `Assets/Mine/**` | 沙箱内自由写入；**已入库的**沙箱外也放行（git 兜底）；未入库的新建仍拒 |
| `.agents/memory/**` | 随时可写，**既有记录也能原地改** |
| `.agents/{agents,knowledge,skills}`、`.mcp`、平台目录 | 可写，只记录 |
| `tmp/**`、`.mcp/trash/**` | 自由 |
| 分区表之外（`AGENTS.md`、`.mcp.json`、`.gitignore` 等） | **已入库即放行**（git 兜底），按体系区记录；未入库拒 |
| `Library`、`Temp`、仓库外、未跟踪的新文件 | 拒绝，需人工 |

**git 保护 = 可随意编辑**：判据不是「agent 可靠」，而是内容有版本库兜底 ——
改错一条 `git checkout` 就回来，拦着它反而让 agent 够不到项目入口文件。

## 过程节点（文档约定，不是闸）

下列输出是**思考结构**，帮助把决策说清楚；没有任何工具核对它们，`close` 也不看申报。
真正被核对的只有证据 —— 别把输出当成通行证。

| 节点 | 输出 | 作用 |
|---|---|---|
| 模式 | `Mode Selection — Mode: <mode> | Reason: <why>` | 决定推进方式 |
| 知识 | 按 [知识预加载](knowledge.md) 读取 profile 的必读项 | 读取由 hook 观测，无需申报 |
| 脚本 | `Script Decision: NeedScript / LibraryCheck / Match / Decision / Path` | 避免造同功能脚本（[脚本决策](script-decision.md)） |
| 落点 | `File Placement: FileType / Category / TargetDir / Exists` | 避免乱放（[文件放置](file-placement.md)） |

`G0`–`G3`、`gate_set_recipe`、`gate_pass`、`g_entry`、`g_knowledge`、`write_gated`、`delete_gated`、`move_gated`、
`gate_cli.py`、`check_norm.py` 均已移除（源码在 `.mcp/trash/`，可 `cli.py restore` 取回，不要重新启用）。

## 出口验收

```bash
python3 .mcp/bin/cli.py close
python3 .mcp/bin/cli.py close --waive verify --reason "纯逻辑改动，无需画面验证"
```

`close` 按 profile 的完成义务逐项查证据：`knowledge`（PostToolUse 观测到的真实读取）、`norm`（本次写入的违规）、
`compile`（最后一次写入之后的 Unity 日志无 error）、`verify`（晚于最后写入的验证产物）、`memory`（有实质改动时
dated 记录 + 索引更新）。缺失项默认 block；`close.verify` / `close.memory` 为 warn。豁免必须给 `--reason` 并留痕。

**没有实质改动就不必写记忆**：`close` 会跳过 `memory`。纯只读任务用 `--profile research`。

记忆记录可以原地改：结论修正直接写进原文件，不必新建副本再在索引里指向前者 —— 但索引仍要跟上。

## 边界

- 禁止 `git stash --all`；重大改动前按 [backup.md](backup.md) 明确 Git 基准和脏改动。
- 沙箱的**来源**必须在用户那边：agent 不自行选定或擅自扩大 `--workspace`；`expand` 只能写用户指过的目录，
  且必须带 `--reason`（用户原话）留痕。agent 也不得改 `policy.json` 为本次任务放行。
- 证据账本（`.mcp/{friction,writes,reads,deletes,moves}.jsonl`、`session.json`、`trash`）不可删除。
- 项目资源用 `python3 .mcp/lib/validation/project_resources.py path <id>` 解析。
- 存量删除按根 AGENTS.md 准备精确清单并取得确认；runner 自建可重建 fixture 按 [产物规范](../../../knowledge/meta/interfaces/task-artifacts.md) 自动收尾。
- 疑似误拦交 meta 体系维护复核（[maintenance](../../agent-maintenance/references/maintenance.md)），不就地改策略。
