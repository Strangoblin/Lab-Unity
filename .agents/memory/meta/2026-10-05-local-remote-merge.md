---
name: local-remote-merge
description: 保留 Windows 本地软链检查与 OpenCode skill，合并远程 agent 体系更新。
date: 2026-10-05
---

# 本地与远程合并

- 保留本地 `opencode-orchestrate` skill、Claude 发现软链和 Git 软链指针读取逻辑。
- 保留远程 `codex-orchestrate` 退役与门禁更新；Phase 7 的 Claude 跟踪文件基线为 26。
- 共享内容仍以 `.agents/` 为权威源；未复制正文到平台适配层。
- 本机 `.codex/config.toml` 独立保存并恢复，不提交机器路径。
- 架构 strict 检查通过；本地 Terrain 功能文件的规范检查通过。
- Windows MCP 测试需 UTF-8 环境和 Git 的 `usr/bin`（提供 grep）。
- 配置上述环境后，测试仍在 `test_write_gated_placement_wired` 失败：
  预期 `NORM_VIOLATION`，实际 `PATH_NOT_ALLOWED`；未扩大合并范围修复门禁。
- Unity Editor 未运行，未验证编译与运行效果。
