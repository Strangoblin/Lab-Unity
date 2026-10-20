---
name: global-opencode-go-route
description: 用户要求让所有 agent 都能调用 opencode：全局补薄路由条目（Codex / Claude 两侧各一份逐字节相同的路由 skill + 三个明文路由入口），正文仍唯一留在 Unity/Lab 仓库；修订 2026-10-15「全局不设入口」的取舍，并修正其「已加路由」实际未落地。
date: 2026-10-17
---

# 全局可达性：让所有项目的 agent 能找到 opencode 链路

用户要求：检查 `.agents/skills/codex-opencode-go` 相关内容，复制到全局配置，使**所有 agent 都知晓调用 opencode**。

## 裁决修订（对 2026-10-15 的补充）

2026-10-15 的结论是「单一正文留在仓库 + 跨项目改用明文路由，不恢复副本、不建软链」。本次核查发现该决定的**明文路由只执行了一半**：

- `~/.claude/CLAUDE.md` 的路由存在（Blender 路由 + codex-opencode-go 路由）。
- `~/.Codex/AGENTS.md` 当时记录为「已加一行路由」，实测**长期为 0 字节**，内容从未落地（原因未定位）。

本次不改「唯一正文在仓库、不存第二份正文」这条原则；改的是**可达性形式**：全局补**薄路由条目**（路由 + 本机要点），不复制正文。全局仍然只有一份正文，`codex-opencode-go` 的正文漂移风险保持为零。

## 全局改动（机器级，不在 git 内）

| 路径 | 形态 | 字节 |
|---|---|---|
| `~/.codex/skills/opencode-go/SKILL.md` | 新建：路由 + 本机要点表 + 委派边界（CRLF、UTF-8 无 BOM） | 2796 |
| `~/.codex/skills/opencode-go/agents/openai.yaml` | 新建：skill 面板元数据（CRLF） | 210 |
| `~/.claude/skills/opencode-go/SKILL.md` | 与 Codex 侧**逐字节相同**（sha256 `d17ae77a…`） | 2796 |
| `~/.codex/AGENTS.md` | 0 字节 → Codex 全局路由（opencode 路由 + 项目目录） | 670 |
| `~/.claude/CLAUDE.md` | 追加一行明文路由 | 532 |
| `~/.claude/MEMORY.md` | 追加「## OpenCode Go / opencode CLI」路由节 | 595 |

- skill 名用 `opencode-go`（**不是** `codex-opencode-go`），避免与仓库正文同名导致「运行时技能列表出现两份」（2026-10-15 的旧症状）。
- 薄条目只写三件事：正文绝对路径、本机（Windows）要点表、委派边界；正文（config / catalog / 传输 / 完整排障表）一律读仓库文件，条目里明确「本文件不另存副本，冲突以正文为准」。
- 全局 side 未新建软链或联接点；`~/.codex/skills/opencode-orchestrate`（2026-10-05 遗留的 junction，指向 `Lab/.agents/skills/opencode-orchestrate`）本次未动。

## 仓库改动（本次提交）

- `.agents/skills/codex-opencode-go/SKILL.md`：
  - 头部补一行：全局侧只保留薄路由（列出 5 个入口），**仍不存在第二份正文**。
  - 新增「## 全局可达性与本机（Windows）现状（2026-10-17 实测）」：全局入口表 + 与 macOS 手册的差异表 —— 凭据是 `OPENCODE_GO_API_KEY`（本机未设 `OPENCODE_API_KEY`）、`opencode.json` 显式定义 provider（「不要覆盖 baseURL」在本机不适用）、`ocw` 为 macOS 路径本机不存在、PowerShell 的 stderr 假失败。
- `.agents/skills/README.md`：索引行由「（通用手册见全局权威）」改为「（唯一正文随仓库 git 留档；全局仅薄路由条目）」 —— 旧表述与 2026-10-15 之后的实际状态（正文早已在仓库）矛盾。

## 验证

- `.codex/tests/06_architecture/verify.py --strict` → **PASS**，全项 0 finding（`absolute_path_hits=0`、`skill_drift_files=0`、`architecture_symlink_errors=0`、`agent_ownership_errors=0`）。
- 两个仓库文件保持 CRLF / UTF-8 无 BOM；未把机器绝对路径写进仓库（`C:\Users` 扫描 0 命中）。
- 本机链路实跑：`opencode run -m opencode-go/deepseek-v4.1-flash "Reply with exactly: OK"` → 退出码 **0**、输出 `OK`（≈9s）；PowerShell 仍包出 `NativeCommandError` 外观（已记入手册，属外观而非失败）。
- `.mcp/tests/run_isolated.py` 四套契约 **4/4 PASS**（必须带 `--python .mcp/.venv/Scripts/python.exe`，Windows venv 无 `bin/python`），结束后真实 `.mcp` state / 审计指纹未变。

## 边界与代价

- 薄条目含**本机快照事实**（CLI 版本、环境变量名、模型清单、机器级配置路径），机器配置变化时会过期；条目已注日期并声明「以正文为准」，同一张表也写进正文，两侧同步修订。
- 未触碰 Unity 资产 / Editor / `.mcp` 判定代码 / policy / hooks。
- 回退：删除上表 6 个全局条目即可（全局侧不在 git 内）；仓库侧 `git revert` 本次提交。

## 坑（本机工具链，值得记住）

- 本机 PowerShell 的 PATH 里**没有 `grep`**；缺它时隔离 MCP 的 `test_recipes.py` 与 `test_directory_deletion.py` 会以「grep 失败：无法排除 GUID 是否仍被引用」失败（表现为误判 DENIED）。把 `C:\Program Files\Git\usr\bin` 加进 PATH 即 4/4 全绿 —— 是环境问题，不是回归，别据此怀疑指南改动。
- PowerShell 5.1 把中文**管道**给 native 进程（`$s | python -`）走 `$OutputEncoding`（默认 ASCII），非 ASCII 静默变 `?`，会写坏文件；应改用 `[System.IO.File]::WriteAllText`（命令行字面量经 argv 走 UTF-8）或先落脚本文件再执行。
- 首次写入因此产生过两个坏文件（`~/.codex/AGENTS.md`、`~/.claude/MEMORY.md`），已按字节核验（`?` 计数 0、无 BOM）后重写。
