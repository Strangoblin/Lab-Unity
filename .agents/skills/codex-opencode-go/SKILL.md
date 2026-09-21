---
name: codex-opencode-go
description: Codex CLI/VSCode 与 opencode CLI/VSCode 扩展接入 OpenCode Go 的完整配置与排障（直连方案，2026-08-17 起无需本地代理）。涉及 Codex config.toml/auth.json 与 opencode opencode.json、模型可用性与区域限制。在任何电脑上从零搭好 → OpenCode Go 链路，或排查 "Codex could not start"、stream disconnected、403 RegionError、Insufficient balance、Missing API key 等错误时使用。
---

# Codex / opencode ↔ OpenCode Go 接入手册（直连方案）

> 2026-08-17 起 OpenCode Go **原生支持 `/v1/responses` 端点**，Codex 可直连，不再需要 opencode-go-proxy 协议翻译。配置产物：全局 `~/.codex/config.toml` + `~/.codex/auth.json`；项目级隔离用 `CODEX_HOME`（示例：`Robot/Bot/.codex/`）。

## 架构（直连）

```
Codex    (CLI/VSCode) ──responses──▶ https://opencode.ai/zen/go/v1   ← 本文主体
opencode (CLI/VSCode) ─────────────▶ https://opencode.ai/zen/go/v1   ← 见「opencode CLI」节
```

- `wire_api = "responses"`（Codex 26.803+ 唯一支持，2026-02 起 `chat`/`chat/completions` 已移除）
- OpenCode Go 直连即原生 Responses API，**无代理、无协议翻译、无"无限空白"bug**

## 两件套配置（缺一不可）

| 文件 | 位置 | 作用 |
|------|------|------|
| `config.toml` | `~/.codex/config.toml` | 模型 + provider 定义 |
| `auth.json` | `~/.codex/auth.json` | OpenAI 格式 key（`{"OPENAI_API_KEY": "sk-..."}`, chmod 600） |

```toml
# ~/.codex/config.toml — 直连模板
model = "deepseek-v4-flash"
model_provider = "opencode-go"
model_reasoning_effort = "high"
disable_response_storage = true

[model_providers.opencode-go]
name = "OpenCode Go"
base_url = "https://opencode.ai/zen/go/v1"   # ⚠️ 必须带 /v1
wire_api = "responses"
stream_idle_timeout_ms = 300000
request_max_retries = 2
stream_max_retries = 2
```

## 安装步骤（新电脑）

1. **安装 Codex CLI**：`npm install -g @openai/codex`（或 VSCode 装 `openai.chatgpt` 扩展）
2. **写配置**：创建 `~/.codex/config.toml`（用上面模板）+ `~/.codex/auth.json`（填有效 key）
3. **验证**：`codex exec "Reply with exactly: OK"` → 应输出 `OK`

## 模型可用性（2026-08 实测）

| 模型 | 状态 |
|------|------|
| `deepseek-v4-flash` | ✅ 默认（需在 opencode 工作台开启「提供商 → 启用部署在中国的模型」，2026-08-11 实测通过） |
| `deepseek-v4-pro` | ✅ 无区域限制（备选） |
| `mimo-v2.5` | ✅ 可用 |
| `gpt-5.6-luna` / `glm-5.2` / `qwen3.8-max` / `kimi-k3` / `minimax-m3` | ✅ 可用 |

切模型：改 `config.toml` 的 `model = "xxx"`。

## 项目级隔离（CODEX_HOME）

多项目用不同 key/模型时，建项目级 `.codex/` 目录：

```bash
export CODEX_HOME=/path/to/project/.codex
codex exec "..."   # 读项目级 config.toml + auth.json
```

示例：`Robot/Bot/.codex/`（独立 key + 信任目录）。

## 排障速查表

| 症状 | 根因 | 修复 |
|------|------|------|
| 扩展报 `unknown variant chat/completions, expected responses` | Codex 26.803 不支持 chat/completions | `wire_api = "responses"` |
| 上游 401 Invalid API key | auth.json key 失效 | 更新 auth.json，重新去 opencode.ai 生成 |
| 上游 403 `only available hosted in China` | 模型需中国区 opt-in | 去 opencode 工作台开启「提供商 → 启用部署在中国的模型」；不开则换 deepseek-v4-pro 等无限制模型 |
| 404 Not Found（HTML 页） | `base_url` 漏 `/v1` | 必须 `https://opencode.ai/zen/go/v1` |
| `Model metadata for X not found` warning | Codex 无该模型元数据，用 fallback | 无害，可忽略；或在 config 显式声明 `model_context_window` 等字段 |
| `Ignored unsupported project-local config keys: model_provider, model_providers` | **项目级** `.codex/config.toml` 不支持 provider 类 key（Codex 只认 user-level `~/.codex/config.toml`） | 模型/provider 只写在全局 `~/.codex/config.toml`；项目级 config 只放 trust_level 等 key |
| VSCode 面板模型选择器只有默认 gpt 模型（看不到 opencode-go 的 8 个） | `model_catalog_json` 未配置 / catalog 文件缺失（伴随 CLI 侧 `Model metadata not found` warning） | 重建 `~/.codex/model-catalogs/opencode-go.json`（每模型必含 `visibility:"list"`、`supported_reasoning_levels`、`truncation_policy` 等完整字段）+ config.toml 顶层加 `model_catalog_json = "绝对路径"` + **Reload Window**。catalog 是面板模型列表的唯一来源，与代理无关，直连也必须保留 |
| `Codex could not start` / webview 卡住 | 扩展启动需连 chatgpt.com / github.com，网络不通 | 检查外网连通性；重载重试 |
| VSCode 改了 config.toml 不生效 | 扩展用内存缓存 | Reload Window |
| 模型身份幻觉（自称 GPT） | 模型训练数据问题，正常现象 | 忽略，以 config.toml 的 model 为准 |

## opencode CLI / VSCode 扩展接入（2026-09-21 实测）

同一个 OpenCode Go 账号的**第三个客户端**（前两个是 Codex CLI 与 Codex VSCode 扩展）。

### 扩展是薄壳，先装 CLI

`sst-dev.opencode`（VSCode 扩展）**仅 28K、不含二进制**、`activationEvents: []`——它的三个命令全部只是开一个名为 `opencode` 的终端跑 `opencode --port <随机端口>`，再轮询 `http://localhost:<port>/app`、POST `/tui/append-prompt` 注入当前文件引用。**没有 CLI 它什么都做不了，且失败是静默的**（按键无反应、无任何报错）。

```bash
npm install -g opencode-ai
opencode --version        # 预期 1.18.31
```

> 本机 npm registry 是内网 `bnpm.byted.org`，不通时加 `--registry=https://registry.npmjs.org/`（单次覆盖，不必改全局配置）。
> npm 11 会警告 `allow-scripts ... postinstall`，但**实测 postinstall 仍然执行**（`bin/opencode.exe` 与平台包 `bin/opencode` inode 相同 = 硬链接成功，138MB Mach-O arm64 就是真二进制）。该警告在此场景是虚惊，**别据此判断安装失败**。

### 配置：直接用内置 `opencode-go`，**不要**覆盖 baseURL

`~/.config/opencode/opencode.json` 就两行：

```json
{
  "$schema": "https://opencode.ai/config.json",
  "model": "opencode-go/deepseek-v4-flash"
}
```

opencode **内置了两个 provider**，别搞混——这是这条链路上最容易走错的地方：

| provider | 端点 | 计费 | 说明 |
|---|---|---|---|
| `opencode` | `https://opencode.ai/zen/v1` | 按量计费 | OpenCode Zen；余额为 0 时报 `Insufficient balance` |
| `opencode-go` | `https://opencode.ai/zen/go/v1` | **订阅** | OpenCode Go，36 个模型，与 Codex 用的是同一个 |

> ⚠️ **走错 provider 的症状极具迷惑性**：模型名照样被识别（`> build · deepseek-v4-flash` 正常显示），只在请求时报 `Insufficient balance` + billing 链接。**看到这个错误不要去改 baseURL**——把模型换成 `opencode-go/<模型>` 即可。
> 覆盖 `provider.opencode.options.baseURL` 只在 **CLI 路径**有效；服务端 `SessionRunner` 用的是**逐模型的 `model.api.url`**（来自目录，不受 provider baseURL 影响），所以覆盖救不了 GUI。模型级 `options.baseURL` 实测同样无效。

### 凭据：`opencode-go` 认的是 `OPENCODE_API_KEY` 环境变量

该 provider 自带 `env: ['OPENCODE_API_KEY']` 声明：

```bash
export OPENCODE_API_KEY='sk-...'      # 持久化就写进 ~/.zshrc
opencode run -m opencode-go/deepseek-v4-flash "hi"
```

> ❌ **别用 `opencode auth login -p opencode`**——那存的是 `opencode`（Zen 按量计费）provider 的凭据，`opencode-go` **读不到**。症状是「明明登录了却不生效」；`opencode auth list` 显示 `OpenCode Zen / api` 恰恰说明**登错了 provider**。

### opencode 侧排障

| 症状 | 根因 | 修复 |
|------|------|------|
| `Insufficient balance` + billing 链接（模型名却正常显示） | 用的是 `opencode/`（按量计费）而非 `opencode-go/`（订阅） | 模型改成 `opencode-go/<模型>`；**不要**动 baseURL |
| `auth list` 有凭据但仍鉴权失败 | 登的是 `opencode` provider；`opencode-go` 读 `OPENCODE_API_KEY` | 设该环境变量，别依赖 `auth login` |
| `ModelUnavailableError: Model unavailable: opencode/xxx` | 该模型在 Zen 目录里但**不在 Go 订阅内** | 换成 `opencode-go/` 下的模型 |
| config 里 `"apiKey": "{env:VAR}"` 但变量未设置 | `{env:}` 解析为**空串**并静默盖掉下层凭据，报 `Missing API key` | 删掉那行；`opencode-go` 会自己读 env |
| 扩展按键无反应 / 无任何输出 | `opencode` CLI 未装或不在 PATH | `npm install -g opencode-ai`；确认 `~/.nvm/.../bin` 在登录 shell PATH 上 |
| `OPENCODE_SERVER_PASSWORD is not set; server is unsecured` | 正常现象 | 默认只绑 `127.0.0.1`，本机自用无碍 |

### ⚠️ 未解决：Web GUI / headless server 的会话不执行（2026-09-21 实测）

`opencode serve` / `opencode web` 起的 Web GUI（`/app`）**界面能开，但发消息不产生任何回复**：

- `POST /api/session/{id}/prompt` 返回 200，事件流依次收到 `session.next.prompt.admitted` → `session.next.prompted`，**然后断掉**——无后续事件、无消息落库、无错误日志
- 换全新端口的新实例同样复现 → **不是实例状态污染**
- **但服务端本身是好的**：`opencode run --attach http://127.0.0.1:4096` 能驱动同一个服务正常出结果 → 坏的只是**裸 HTTP `/prompt` 这条路径**（浏览器走的就是它），不是会话引擎
- 已排除：进程 cwd 正确、`opencode.db` 的 `project_directory` 正确、项目根目录无异常文件名；显式带 `model`+`agent` 建会话、订阅会话级 `/event` 均无效
- 伴随一个**未解释的现象**：服务启动约 20 秒后，日志出现一次 `bootstrapping directory=<正确路径>/j<非法字节>`，同一 run 内**先正确后变坏**，来源未定位

**CLI 与 TUI 不受影响**（`opencode run` 与 `opencode-go` 模型实测正常）。在解决前不要依赖 Web GUI。

### 快捷键与卸载

`Cmd+Esc` 开/聚焦 · `Cmd+Shift+Esc` 新会话 · `Cmd+Option+K` 插入 `@文件#L37-42`

```bash
npm uninstall -g opencode-ai
rm -rf ~/.config/opencode ~/.local/share/opencode
```

### 派活：把 opencode 当子代理用（`~/.local/bin/ocw`）

`ocw` 是包装脚本——注入 `OPENCODE_API_KEY`（运行时从 codex profile 读，**密钥全机只有一份**）+ 默认 `--auto`（无人值守不卡在权限确认）：

```bash
ocw "任务文本"                     # 当前目录
ocw --dir /路径 "任务文本"          # 指定工作目录
ocw --format json "任务文本"        # JSONL：step_start / text / step_finish（含 tokens + cost）
ocw -m opencode-go/deepseek-v4-pro "任务文本"
```

**实测耗时**（deepseek-v4-flash，2026-09-21）：纯文本 **7s**；带一次文件写入的 agent 循环 **9s**。→ **短任务不慢**，别把长会话的上下文压缩问题误当成基础延迟。

> ⚠️ **`--auto` 会绕过门禁**：它放行一切「未被显式禁止」的操作（写文件、执行命令）。在 `Unity/Lab` 里直接派它写 `Assets/Mine/` 会**跳过 `write_gated`**，违反项目规则。沿用既有约定——**opencode 产出 → Claude review → 经门禁链合入**（同 `codex exec` 产出的合入方式，见 `.codex/INTERFACE.md` §3）；日常让它在**沙盒副本或非门禁目录**里干活。

## 关键验证命令

```bash
# 1. CLI 直连
codex exec "Reply with exactly: OK"

# 2. 直测 OpenCode Go responses 端点（验 key/模型）
curl -s -X POST "https://opencode.ai/zen/go/v1/responses" \
  -H "Authorization: Bearer $OPENAI_API_KEY" -H "Content-Type: application/json" \
  -d '{"model":"deepseek-v4-flash","input":"hi","stream":false}'

# 3. 模型列表
curl -s "https://opencode.ai/zen/go/v1/models" -H "Authorization: Bearer $OPENAI_API_KEY"
```

## 历史方案（仅供参考，不再需要）

**opencode-go-proxy 协议翻译**（8-10 版方案，OpenCode Go 支持 responses 后废弃）：Codex 当时只发 `/v1/responses` 而 OpenCode Go 只有 `/v1/chat/completions`，需本地代理翻译。相关坑已随直连消失：`--chat-base-url` 漏 `/v1` 的 404、模型不在代理白名单 fallback 到 flash 的 403、CC-Switch 代理的"无限空白"流掐断。

## 与 CC-Switch 的关系

- **项目 Agent → OpenCode Go**：仍走 CC-Switch 本地代理（15721），Claude 走 Anthropic Messages 格式，需要 CC-Switch 做翻译
- **Codex → OpenCode Go**：**直连**（responses 原生格式），与 CC-Switch 完全无关
