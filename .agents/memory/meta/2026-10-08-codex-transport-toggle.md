---
name: codex-transport-toggle
description: codex 官方 provider 的 WS→HTTPS 传输开关落地；「改名 provider 会丢 OAuth」是错的——requires_openai_auth 保得住
date: 2026-10-08
---

# 2026-10-08 — codex 传输轴（cx-http / cx-ws）与「改名 provider 丢 OAuth」结论的推翻

## 起因

官方 provider 默认走 Responses WebSocket。本机 ClashX 开普通规则模式（非 TUN）时 WS 客户端不认
系统代理 → 每次会话先付 **15s 握手超时 + 最多 5 次 `Reconnecting...`**，才降级到 HTTPS。
需求：跳过这段成本，直接走 HTTPS，同时保住官方 ChatGPT 账号（不换 opencode-go）。

## 复测确认的硬事实（codex-cli 0.159.2）

- `[model_providers.openai] supports_websockets = false` **仍被拒**：
  `model_providers contains reserved built-in provider IDs: 'openai'.`
  （2026-09-14 的 cx-http/cx-ws 就是栽在这里，当时的撤销是对的）
- `responses_websockets` / `_v2` 特性开关在 `codex features list` 里已标 **removed**。
- 官方对症杠杆 `respect_system_proxy` 是 under development——它**修 WS 让握手成功**，不是换 HTTPS。

## 关键修正：「改名 provider 丢 OAuth」不成立

skill 里 2026-09-14 那条「改名 provider 复制官方 ❌ 换名后 ChatGPT OAuth 认证模式丢失」是**未经实测的推断**。
实测：**指名官方同一后端 + `requires_openai_auth = true`，OAuth 认证完整保留**——请求打到官方后端，
返回的是业务错误（账号额度用尽）而非 401。故唯一可行解就是改名 provider。

```toml
model_provider = "openai-http"
[model_providers.openai-http]
base_url = "https://chatgpt.com/backend-api/codex"
wire_api = "responses"
requires_openai_auth = true     # ← 保住 ChatGPT OAuth
supports_websockets = false     # ← 关掉 WS 握手
```

对照实测：WS 路径 5 次 `Reconnecting...` 后才降级；openai-http 路径**零重连**，doctor 报
`✓ websocket Responses WebSocket is not enabled for the active provider`。

## 实现要点（踩坑点）

- **托管块必须插在第一个 `[table]` 之前**：TOML 裸键归属最近的表头，追加到文件尾会把
  `model_provider` 变成最后一个 `[projects.*]` 表的键。switch-codex.sh 用 python 按"首个 `[` 行"定位插入。
- **校验器只能用 `codex features list`**：`codex doctor` 在配置加载失败时静默回退默认配置继续跑，
  只在顶部留一行 `✗ config`——2026-09-14 就是被 `sed` 过滤掉这行才误判成功。切换流程内置
  校验 + 失败自动回滚（mktemp 备份）。
- 传输轴**只对 official 生效**（opencode-go 是自定义 provider，本就不走 WS），且随 profile 持久化：
  cx-off → cx-on 往返后仍是 https。
- 轴与轴正交：`cx-on/cx-off` 管账号，`cx-http/cx-ws` 管传输，`cx-status` 两支都显示。

## 验证矩阵（11 项全过）

临时 `CODEX_HOME` 隔离测试：status 初始值、切 https、托管块位置、config 可加载、
doctor 传输行、幂等、切回 ws 后标记清除、opencode-go 模式下的守卫拒绝、profile 往返持久化。
实机 `~/.codex/` 未改动（用户当时处于 opencode-go 模式）。

## 待观察

- 该账号额度 2026-10-08 15:42 前用尽，**成功一轮对话的端到端验证留待额度恢复**；
  传输与认证已由「服务端返回业务错误而非 401」证明。
- 自定义 provider 下 `failed to refresh available models: request timed out` 出现、官方 provider 下不出现——
  是否影响 VSCode 面板模型列表未验证。

## 同步

`.agents/skills/codex-opencode-go/SKILL.md` 与通用版 `~/.claude/skills/codex-opencode-go/SKILL.md`
两处同步改（§网络传输 + 排障表 WS 行）；两版 diff 仅剩描述/头部/项目专属节。
