---
name: opencode-go-model-v41-flash
description: opencode-go 换 deepseek-v4.1-flash；catalog 是模型元数据的唯一来源（key 是 slug），上游 2026-10 起新增 x-opencode-session 头
date: 2026-10-08
---

# 2026-10-08 — opencode-go profile 换 `deepseek-v4.1-flash`

## 可行性（先验后改）

- 上游存在：`/zen/go/v1/models` 共 **37** 个（旧文档写 36），该模型 created ≈ 2026-10-07。
- **直连可用**，无区域限制、无需代理；responses 格式兼容。
- 结论：可行 → 已改。`model = "deepseek-v4.1-flash"` 同时写进 `~/.codex/config.toml`
  与 `~/.codex/profiles/opencode-go/config.toml`（两点一线，profile 是切换脚本的落点）。
  `review_model` 仍留 `deepseek-v4-flash`。

## catalog 纪律（本次真正学到的东西）

`model_catalog_json` → `~/.codex/model-catalogs/opencode-go.json` 是**模型元数据 + VSCode 面板模型列表的唯一来源**。

- 换到 catalog 里没有的模型 → warning `Model metadata for X not found`，且按 **fallback 元数据**走
  （context window 失真 → 直接影响压缩时机），面板也不显示。
- 补条目：**key 是 `slug` 不是 `id`**；克隆同族条目改 `slug` / `display_name` 即可。此法消警（实测）。
- ⚠️ 上游 `/models` 只回 `id/object/created/owned_by`，**不带窗口大小** → catalog 里的数值是手工填的。
  `deepseek-v4.1-flash` 的 131072 / 95% 是**从同族 `deepseek-v4-flash` 克隆的假设**，未证实；真值不同则要改。

## 上游变更：`x-opencode-session` 头（2026-10）

裸 curl 打 `/v1/responses` 现在必回 `{"error":"MissingSessionID"}`，**与 key/模型无关**；
加 `-H "x-opencode-session: $(uuidgen)"` 即通。codex 自身**不发这个头也能通**（未被上游拦），
所以「裸 curl 失败」不等于链路坏了——旧 skill 的验证命令曾因此静默失效，已修。

## 附带发现：0.159.2 新增两个被忽略的 key

`codex exec` 每次刷：`disable_response_storage`、`mcp_servers.node_repl.type` **已被忽略**（unrecognized）。
留着只刷警告，未擅自删（属用户配置）。

## 改动与备份

| 文件 | 备份 |
|------|------|
| `~/.codex/model-catalogs/opencode-go.json`（条目前插） | `.bak-20261008` |
| `~/.codex/config.toml`（line 1 model） | `.bak-20261008-model` |
| `~/.codex/profiles/opencode-go/config.toml`（line 1 model） | `.bak-20261008-model` |

验证：配置可加载、`codex exec` 回 `model: deepseek-v4.1-flash` + `OK` 且**无 metadata 警告**、
`codex doctor` 报 `model deepseek-v4.1-flash · opencode-go`。

## 同步

`.agents/skills/codex-opencode-go/SKILL.md` 与 `~/.claude/skills/codex-opencode-go/SKILL.md`
两处同步改（模型可用性表 + 排障表 4 行：metadata / MissingSessionID / 忽略 key / 模型计数）。

相关：[[codex-transport-toggle]]（同日另一条轴）
