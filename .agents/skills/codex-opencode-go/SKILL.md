---
name: codex-opencode-go
description: Codex CLI/VSCode 与 opencode CLI/VSCode 扩展接入 OpenCode Go 的完整配置与排障（直连方案，2026-08-17 起无需本地代理）。涉及 Codex config.toml/auth.json 与 opencode opencode.json、模型可用性与区域限制。在任何电脑上从零搭好 → OpenCode Go 链路，或排查 "Codex could not start"、stream disconnected、403 RegionError、Insufficient balance、Missing API key 等错误时使用。
---

# Codex / opencode ↔ OpenCode Go 接入手册（直连方案）

> 2026-08-17 起 OpenCode Go **原生支持 `/v1/responses` 端点**，Codex 可直连，不再需要 opencode-go-proxy 协议翻译。配置产物：全局 `~/.codex/config.toml` + `~/.codex/auth.json`；项目级隔离用 `CODEX_HOME`（示例：`Robot/Bot/.codex/`）。

> 🔀 **本文件是项目专用超集**——通用内容 + `Unity/Lab` 专属的「opencode CLI / VSCode 扩展接入」与「派活（`ocw`）」两节。通用版（零项目专属路径，供其他项目用）在 `~/.claude/skills/codex-opencode-go/SKILL.md`。
> **改通用内容时两处同步**（Codex 侧链路、模型可用性表、排障表等）；纯项目专属内容只改本文件。两份曾长期无声漂移（本文件缺了整批 Codex 侧内容），2026-09-21 才合并补齐——故留此指针。

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
requires_openai_auth = true   # ⚠️ 必填（2026-08-21 实测）：codex 0.149+ 收紧认证，自定义 provider 不再默认回退 auth.json，缺此行 → 401 Missing API key
wire_api = "responses"
stream_idle_timeout_ms = 300000
request_max_retries = 2
stream_max_retries = 2
```

## 安装步骤（新电脑）

1. **安装 Codex CLI**：`npm install -g @openai/codex`（或 VSCode 装 `openai.chatgpt` 扩展）
2. **写配置**：创建 `~/.codex/config.toml`（用上面模板）+ `~/.codex/auth.json`（填有效 key）
3. **验证**：`codex exec "Reply with exactly: OK"` → 应输出 `OK`

## 模型可用性（2026-08-25 实测）

| 模型 | 直连 | 走美国代理（如 `HTTP(S)_PROXY=http://127.0.0.1:7890`） |
|------|------|------|
| `deepseek-v4-flash` | ✅ 默认（需在 opencode 工作台开启「提供商 → 启用部署在中国的模型」） | ✅ |
| `deepseek-v4-pro` | ✅ 无区域限制（备选） | ✅ |
| `gpt-5.6-luna` | ❌ 403 `unsupported_country_region_territory` | ✅ **需美国代理** |
| `mimo-v2.5` / `glm-5.2` | ❌ 500 上游内部错误（与代理无关） | ❌ |
| `qwen3.8-max` / `kimi-k3` / `minimax-m3` | ❌ 401 `not supported for format openai`（不支持 responses 格式） | ❌ |

切模型：改 `config.toml` 的 `model = "xxx"`；CLI 临时切换用 `codex exec --model xxx`。

**GPT 模型使用要点**：`gpt-5.6-luna` 有地区限制，Codex CLI 需挂美国代理环境变量（`export HTTPS_PROXY=http://127.0.0.1:7890`）后才可调用；VSCode 扩展则需让扩展进程走代理（VSCode `http.proxy` 设置或系统代理）。环境变量里的旧 srt 代理（56310）2026-08-25 已失效。

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
| 上游 401 `Missing API key`（**CLI 同配置却正常**，或扩展内置 codex 0.149+ 必现） | codex 0.149.0 收紧认证：自定义 provider（`requires_openai_auth = false` 默认）不再回退读 auth.json | provider 块加 `requires_openai_auth = true`（走 auth.json 的 OPENAI_API_KEY）+ **Reload Window**。勿用 `env_key` 替代——env_key 只认环境变量，GUI 扩展进程拿不到；且与 requires_openai_auth 互斥 |
| 本地报 `Missing environment variable: OPENAI_API_KEY` | 配了 `env_key` 但环境变量未导出（0.149+ env_key 纯环境变量，无 auth.json 回退） | 若走 env_key 方案需 `export OPENAI_API_KEY=...`（仅 CLI 有效，扩展无效）；推荐改用 `requires_openai_auth = true` |
| 上游 403 `only available hosted in China` | 模型需中国区 opt-in | 去 opencode 工作台开启「提供商 → 启用部署在中国的模型」；不开则换 deepseek-v4-pro 等无限制模型 |
| 上游 403 `unsupported_country_region_territory` | 模型有地区限制（如 gpt-5.6-luna 需美国） | 给 Codex CLI 挂美国代理环境变量（`export HTTPS_PROXY=http://127.0.0.1:7890`）；VSCode 扩展配 `http.proxy` 或系统代理。直测：`curl -x http://127.0.0.1:7890 -X POST https://opencode.ai/zen/go/v1/responses ...` |
| 404 Not Found（HTML 页） | `base_url` 漏 `/v1` | 必须 `https://opencode.ai/zen/go/v1` |
| `Model metadata for X not found` warning | Codex 无该模型元数据，用 fallback | 无害，可忽略；或在 config 显式声明 `model_context_window` 等字段 |
| `Ignored unsupported project-local config keys: model_provider, model_providers` | **项目级** `.codex/config.toml` 不支持 provider 类 key（Codex 只认 user-level `~/.codex/config.toml`） | 模型/provider 只写在全局 `~/.codex/config.toml`；项目级 config 只放 trust_level 等 key |
| VSCode 面板模型选择器只有默认 gpt 模型（看不到 opencode-go 的 8 个） | `model_catalog_json` 未配置 / catalog 文件缺失（伴随 CLI 侧 `Model metadata not found` warning） | 重建 `~/.codex/model-catalogs/opencode-go.json`（每模型必含 `visibility:"list"`、`supported_reasoning_levels`、`truncation_policy` 等完整字段）+ config.toml 顶层加 `model_catalog_json = "绝对路径"` + **Reload Window**。catalog 是面板模型列表的唯一来源，与代理无关，直连也必须保留 |
| `Codex could not start` / webview 卡住 | 扩展启动需连 chatgpt.com / github.com，网络不通 | 检查外网连通性；重载重试 |
| `codex doctor` 报 WS 超时（**只在 ClashX 关掉「增强模式」的普通规则模式下出现**，开 TUN 就正常） | 官方 provider 默认走 Responses WebSocket（`wss://chatgpt.com/backend-api/...`），WS 客户端**不认 macOS 系统代理**（doctor：`respect system proxy: disabled`）→ 握手超时；但同状态下 HTTPS 可达（`ChatGPT inference URL reachable (HTTP 405)`） | **通常无需处理**：codex 自带 WS→HTTPS 自动降级（`warning: Falling back from WebSockets to HTTPS transport`），CLI 实测照常工作。若桌面对话框确有问题，唯一对症的官方杠杆是 `codex features enable respect_system_proxy`（under development）。⚠️ **勿改 `[model_providers.openai]`**——`openai` 是保留的内置 ID，覆盖会让 codex 完全起不来 |
| 配置疑似被改坏 / codex 起不来 | — | 校验**必须用 `codex features list`**（真加载器，配置坏了直接报错）。⚠️ **`codex doctor` 不能当校验器**：配置加载失败时它静默回退默认配置继续跑，只在顶部留一行 `✗ config config could not be loaded` |
| VSCode 改了 config.toml 不生效 | 扩展用内存缓存 | Reload Window |
| 模型身份幻觉（自称 GPT） | 模型训练数据问题，正常现象 | 忽略，以 config.toml 的 model 为准 |

## 网络传输（Responses WebSocket）与 cx-http / cx-ws 的撤销

官方 provider 默认传输是 **Responses WebSocket**（doctor 可见 `endpoint wss://chatgpt.com/backend-api/<redacted>`、
`supports websockets: true`）。WS 客户端不认 macOS 系统代理（doctor：`system proxy: manual` 但
`respect system proxy: disabled`），所以在 ClashX 关掉「增强模式」、只留普通规则模式时会握手超时。
**但 HTTPS 在同状态下可达，且 codex 自带 WS→HTTPS 自动降级**，实测普通规则模式下 CLI 正常工作：

```
$ codex exec "Reply with exactly: OK"
warning: Falling back from WebSockets to HTTPS transport. stream disconnected before completion: Connection refused (os error 61)
codex → OK        (exit 0)
```

> **2026-09-14 撤销**：曾短暂提供 `cx-http` / `cx-ws` 两个开关，实现是写
> `[model_providers.openai] supports_websockets = false`。**这是非法配置**——`openai` 是 codex
> **保留的内置 provider ID**，覆盖它会让配置整体加载失败、codex 完全起不来：
> `model_providers contains reserved built-in provider IDs: 'openai'. Built-in providers cannot be overridden.`
> 已从 `~/.codex/switch-codex.sh` 与 `.zshrc` 中移除。

**为什么当时没发现**：用 `codex doctor` 当校验器。它在配置加载失败时会**静默回退默认配置继续跑**，
只在顶部留一行 `✗ config config could not be loaded`；当时用 `sed` 过滤输出恰好滤掉了这行，
把"WS 检测行消失"误读成"WS 被关掉了"。**校验配置一律用 `codex features list`。**

已排除的替代杠杆：

| 杠杆 | 结论 |
|------|------|
| `[model_providers.openai]` 任何键 | ❌ 保留 ID，不可覆盖 |
| 改名 provider 复制官方 | ❌ 换名后 ChatGPT OAuth 认证模式丢失 |
| `model_catalog_json` + `prefer_websockets: false` | ❌ 实测**不能阻止 WS 首次尝试**；且自定义 catalog 会冻结模型列表 |
| `responses_websockets` / `_v2` 特性开关 | ❌ 0.153 已 **removed** |
| `codex features enable respect_system_proxy` | ✅ 官方对症方案（under development），**唯一真正治本** |

验证连通性（**必须在沙箱外跑**，Claude Code 沙箱会 MITM 掉 TLS 导致误判为网络不通）：

```bash
codex doctor          # 完整输出，不要 sed 过滤——顶部那行 ✗ config 是关键信号
```

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

- **Claude Code → OpenCode Go**：仍走 CC-Switch 本地代理（15721），Claude 走 Anthropic Messages 格式，需要 CC-Switch 做翻译
- **Codex → OpenCode Go**：**直连**（responses 原生格式），与 CC-Switch 完全无关
