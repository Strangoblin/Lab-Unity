# MCP 使用事实（判定核 + 证据账本）

> **写入一律用平台原生 `Write` / `Edit`（PreToolUse hook 判定）；删除走 `cli.py delete`，其余（移动、改名、批量处理）用原生命令。**
> 没有 `cli.py write`，也没有 MCP 服务（`.mcp.json` 为空）—— 会话里不会出现 `mcp__*` 工具。
> **权威来源**：`.mcp/README.md`（架构、理由、规则数据）。**本页只做操作速查，不重复理由。**

## 命令

| 命令 | 作用 | 何时 |
|------|------|------|
| `cli.py open --task … --profile … --workspace …` | 声明任务、沙箱、业务线；返回 playbook | **任务第一步**，只一次 |
| `cli.py expand --workspace … --reason …` | 用户指出新地址后扩充沙箱（对话确认，命令只留痕） | 中途用户加目录 |
| `cli.py status` | 当前会话、分区视图、策略档位 | 不确定边界时 |
| `cli.py close` | 出口验收，按 profile 核对证据 | 收尾 |
| `cli.py delete <路径…> --reason …` | 删除 → `.mcp/trash/`（可恢复） | 删文件/目录时 |
| `cli.py move <源> <目标> --reason …` | 批量改名 / 跨目录搬，`.meta` 成对 | 一次搬多对时 |
| `cli.py trash` / `cli.py restore <id>` | 查垃圾桶 / 取回 | 撤销删除 |
| `cli.py report` | 摩擦账本聚合，产出策略提案 | 维护时（人读后决定） |

`status` / `report` 之外都返回 JSON；退出码 `0` 通过 / `1` 被拦或未达出口 / `2` 用法。

```bash
python3 .mcp/bin/cli.py open --task "修复 SSC 云边缘" --profile shader \
    --workspace Assets/Mine/Shaders/PostProcess/SSC --workspace Assets/Mine/Special/HLSL
# 用平台 Write / Edit / mv 直接改
python3 .mcp/bin/cli.py close
```

## profile 与出口义务

| profile | 必读 | 出口义务 |
|---|---|---|
| `shader` / `compute` | shader 结构规范 + HLSL / compute API | knowledge, norm, compile, verify, memory |
| `script` / `tooling` | 脚本结构规范 | knowledge, norm, compile, memory |
| `benchmark` | 渲染基准契约 | knowledge, compile, verify, memory |
| `meta` | — | memory |
| `research` | — | 无 |

义务逐项查证据：`knowledge` 由 PostToolUse 记录的真实 `Read` 满足；`compile` 读 `unityctl logs`；
`verify` 要晚于最后一次写入的产物；`memory` 只在本次有实质改动时要求。

## 分区

| zone | 路径 | 规则 |
|---|---|---|
| `asset` | `Assets/Mine/**` | 沙箱内自由写；已入库的沙箱外也放行（git 兜底）；新建 / 未跟踪仍须沙箱 |
| `memory` | `.agents/memory/**` | 随时可写，**既有记录也能原地改** |
| `system` | `.agents/{agents,knowledge,skills}`、`.mcp`、平台目录 | 可写，只记录 |
| `scratch` | `tmp/**`、`.mcp/trash/**` | 自由 |
| 分区表之外 | `AGENTS.md`、`.mcp.json`、`.gitignore` 等 | **已入库即放行**（git 兜底）；未入库拒 |

分区表是数据：`.mcp/config/zones.json`。加目录、换平台、调整哪几个区吃 git 兜底（`git_protected.allow_zones`）都改数据。

## 被拒时的处理

| error | 含义 | 处理 |
|---|---|---|
| `SANDBOX_NOT_SET` | 资产区还没开沙箱，且文件未入库 | 跑 `cli.py open --workspace <用户指定目录>` |
| `SANDBOX_VIOLATION` | 未入库文件落在本次沙箱外（含新建） | 沙箱内重试；确需另一个目录，**请用户在对话里确认**后 `cli.py expand` |
| `ZONE_DENIED` | 不在分区表内，且不在 git 里（没有兜底） | 请示用户；生成目录、仓库外都属此列 |
| `NORM_VIOLATION` | 结构规范或落点 | 按 `violations[].check` 修正后重试 |
| `MISSING_REASON` | delete / move / waive / expand 没给原因 | 补 `--reason`（进审计） |
| `MOVE_UNSAFE` | 目标已存在 / GUID 被占 / 批内链式 | 替换目标直接写；`GUID_TAKEN` 先解除占用；链式拆两批 |
| `TRASH_UNSAFE` | 软链或作用域根 | 改走明确目标；软链不跟随、不搬运 |

**误拦不要就地改策略**：记原因交 meta 复核；行为疑问用 `.mcp/tests/run_isolated.py` 验证。

## 提醒

- **失败语义**：资产区与未入库路径失败即拒（判定核崩了也拒），记忆区与暂存区失败即放行。看到 `hook 判定失败` 先查 `.mcp/lib/hooks.py` 能否手工运行。
- **Bash 只封三条红线**：`git stash --all`、`rm -rf Assets/Mine/`、删除证据账本。
  **移动 / 改名 / 其他操作直接用原生命令**（`mv` 会顺带搬 `.meta`，GUID 不变）；
  只有**删除与清空**会被引导去 `cli.py delete`（留底可 restore）。**不通用拦截** Bash 写入其他区 —— 约束来自规则本身，不是 hook 兜底。
- **审计**：`writes.jsonl` / `reads.jsonl` / `deletes.jsonl` / `moves.jsonl` 是证据来源；`close` 只认账本，不认申报。
- **策略可调**（`.mcp/config/policy.json`）：只能放宽不能收紧；agent 不自行改级别。
- **历史工具**：`gate_set_recipe` / `gate_pass` / `gate_cli.py` / `write_gated` / `check_norm.py` 已移入 `.mcp/trash/`，可恢复但不应启用。
