---
name: repo-root-files-have-no-zone
description: 仓库根级文件不在任何分区，v4 时被判 denied；v5 起「git 保护 = 可随意编辑」，已入库的根级文件（AGENTS.md、.mcp.json 等）由 git 兜底放行，问题已解决。
date: 2026-10-28
---

# 仓库根级文件没有分区 —— v5 已由 git 兜底解决

## 原问题（v4，已失效）

v4 分区表（`.mcp/config/zones.json`）只列目录前缀：`tmp`、`.mcp/trash`、`Assets/Mine`、
`.agents/{memory,agents,knowledge,skills}`、`.mcp`、平台目录。**仓库根级散文件落在表外**，
被判 `denied`：改 `AGENTS.md` 的门禁措辞被 hook 以 `ZONE_DENIED` 拒绝，`.mcp.json`、
`.gitignore` 同理。当时的结论是「项目的第一个入口文件恰好在这个模型够不到的地方，
需要用户决定是否给根级文件一个区」。

## 解决（v5，2026-10-28 用户裁决）

用户直接给了更简单的判据：**「只要处于 git 保护，就可以认为 agent 可以随意编辑」**。
判定因此不再问「是哪个目录」，改问「有没有兜底」：

- `zones.effective_zone()` 对 `denied` 与 `asset` 区查一次 `git ls-files`；
- 已入库 ⇒ 视为 `system` 区放行并写 `record`，`AGENTS.md` / `.mcp.json` / `.gitignore` 全部可改；
- 未入库（含新建文件）⇒ 仍按原规则拒绝，资产区的还得落在 `cli.py open --workspace` 沙箱内。

所以**不需要**给 `zones.json` 加精确路径条目 —— 那套格式扩展方案作废。

## 残留边界

- 新建的根级文件没有 git 兜底，仍会被拒；要新建就由用户指定目录后 `cli.py expand`。
- 资产区即使被 git 兜底仍记作 `asset`（不改判 `system`），否则 `close` 的编译/验证义务会静默消失；
  见 [2026-10-28-mcp-v5-git-protected-writes.md](2026-10-28-mcp-v5-git-protected-writes.md)。

## 相关

- v4 整体改造：[2026-10-28-mcp-v4-business-management.md](2026-10-28-mcp-v4-business-management.md)
- v5 改造：[2026-10-28-mcp-v5-git-protected-writes.md](2026-10-28-mcp-v5-git-protected-writes.md)
- 同类边界（`Assets/` 非 Mine、`Library`、仓库外）确实应该拒绝，不需要改。
