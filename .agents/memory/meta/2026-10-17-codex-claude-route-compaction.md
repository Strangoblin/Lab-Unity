---
name: codex-claude-route-compaction
description: 用户确认删除 .codex / .claude 的兼容壳与遗留产物，将平台层压缩为两份明文入口；修复 Codex hook 的 command / commandWindows 路由与 Windows guard 的 Python 解析失败关闭，架构 strict 与隔离 MCP 4/4 通过。
date: 2026-10-17
---

# .codex / .claude 路由压缩与 hook 修复

用户要求检查 `.codex` 与 `.claude`，把内容压缩到路由文件和必要文件；随后确认删除清单，并要求修复 hook 的 `command` 路由。

## 用户确认的删除

| 路径 | 性质 |
|---|---|
| `.codex/INTERFACE.md` | 已由 `.codex/AGENTS.md` 承接的旧入口 |
| `.codex/SKILL.md` | 旧平台技能路由壳 |
| `.codex/agents/README.md` | 旧角色发现壳 |
| `.claude/agents/README.md` | 旧角色发现壳 |
| `.codex/config.toml.bak-20260825` | 配置备份 |
| `.codex/tmp/git-ignore-audit-20261005/` | 旧审计产物 |
| `.codex/tests/06_architecture/output/` | 可重建报告输出 |

`output/` 会在 `verify.py` 默认运行时重建；最终验证改用 `--output-dir` 指向 `tmp/sessions/`，并在收尾再次清理该默认目录，保持用户确认后的状态。删除期间未触碰 Unity 业务资产，也未启动 Unity Editor。

## 压缩后的平台层

```text
.claude/
  CLAUDE.md
  settings.json
  hooks/guard-bash.sh

.codex/
  AGENTS.md
  config.toml
  hooks.json
  hooks/guard-bash.sh
  agents/meta-developer.toml
  agents/unity-developer.toml
  tests/**
  tmp/README.md
```

- `.codex/AGENTS.md` 是 Codex 唯一明文路由：先到 `.agents/agents/README.md`，再按任务直接取用 memory / knowledge / skills；补充 `.mcp` 判定核、授权边界、临时产物与维护技能入口。
- `.claude/CLAUDE.md` 只保留平台边界与 Agent 清单路由；共享正文仍唯一留在 `.agents/`，不建副本、软链或发现镜像。
- `.codex/tests/06_architecture/platform-whitelist.json` 的平台路由收缩为 `.claude/CLAUDE.md` 与 `.codex/AGENTS.md` 两条。

## Hook 路由修复

`.codex/hooks.json` 原 `command` 指向 macOS 用户路径。现改为：

```json
"command": "bash \"$(git rev-parse --show-toplevel)/.codex/hooks/guard-bash.sh\"",
"commandWindows": "\"C:/Program Files/Git/bin/bash.exe\" \"C:/Programs/Unity/Unity Project/Lab/.codex/hooks/guard-bash.sh\""
```

- 非 Windows 路由从 Git 根目录解析，仓库移动后仍可用；Windows 使用 Codex 支持的 `commandWindows` 覆盖，直接调用本机 Git Bash。
- `guard-bash.sh` 在 Windows 上不再盲调 `python3`：依次探测可用的 `python3` / `python`，找不到解释器或 JSON 解析失败均失败关闭（exit 2）。
- 两侧 `guard-bash.sh` 保持逐字一致；`platform_hook_drift = 0`。

## 验证

- `test_scan_scope.py`：8/8 OK。
- `test_ownership.py`：20/20 OK。
- `verify.py --strict`：PASS；`agents_broken_markdown_links`、`platform_hook_drift`、`phase2_contract_errors`、`architecture_symlink_errors` 均为 0。
- 隔离 MCP 四套契约：4/4 PASS；真实 `.mcp` state 与审计指纹未变。报告见 `tmp/sessions/2026-10-17-codex-claude-compress/mcp2/output/mcp-isolated-55b323fcf359/report.json`。
- Hook 实测（`commandWindows` 与 Git Bash 下的可移植 `command` 各一组）：允许命令 exit 0；`git stash --all` 与 `rm -rf Assets/Mine/...` 均被 exit 2 拦截。

## 测试环境修正

- 隔离 runner 为子进程设置 `PYTHONIOENCODING=utf-8` / `PYTHONUTF8=1`，修复 Windows 默认 GBK 输出导致的 `UnicodeEncodeError`。
- 在 Windows 且 `grep` 不在 PATH 时，runner 从 `git.exe` 位置推导 Git `usr/bin` 并仅注入子进程 PATH；生产 `deletion.py` 未改，缺少 `grep` 时仍按原逻辑失败关闭。
- `run_isolated.py` 的 AST 读取显式指定 UTF-8；fixture 不再复制已删除的 `.codex/INTERFACE.md`。

## 限制与后续

- `hooks.json` 已变化，Codex 可能把新 hook 标记为待审核；需要在 `/hooks` 中重新确认信任，否则 hook 不会执行。
- 本机 PowerShell PATH 仍不含 `grep`；这是测试环境事实，不是本次平台路由回归。生产调用若缺少 `grep`，删除门禁保持失败关闭。
- 本记录不改变任何权限或门禁级别；共享正文仍只在 `.agents/` 维护。