---
name: codex-opencode-go
description: Codex CLI/VSCode 与 opencode CLI/VSCode 扩展接入 OpenCode Go 的完整配置与排障（直连方案，2026-08-17 起无需本地代理）。涉及 Codex config.toml/auth.json 与 opencode opencode.json、模型可用性与区域限制。在任何电脑上从零搭好 → OpenCode Go 链路，或排查 "Codex could not start"、stream disconnected、403 RegionError、Insufficient balance、Missing API key 等错误时使用。
---

# Codex / opencode ↔ OpenCode Go 接入手册（直连方案）

> 2026-08-17 起 OpenCode Go **原生支持 `/v1/responses` 端点**，Codex 可直连，不再需要 opencode-go-proxy 协议翻译。配置产物：全局 `~/.codex/config.toml` + `~/.codex/auth.json`；项目级隔离用 `CODEX_HOME`（示例：`Robot/Bot/.codex/`）。

> 📍 **本文件是 `codex-opencode-go` 的唯一权威**，路径 `Unity/Lab/.agents/skills/codex-opencode-go/SKILL.md`，随仓库 git 留档。
> 先前的 `~/.agents/skills/codex-opencode-go/`（139 行）与 `~/.claude/skills/codex-opencode-go/`（255 行）副本已于 2026-10-15 归并并删除，**不存在第二份正文**。
> 内容含机器级事实（config / 模型 / catalog / 传输 / opencode CLI / `ocw`）与 `Unity/Lab` 的门禁委派边界；其他项目复用请直接引用本路径，不要另存副本。

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

## 模型可用性（2026-08-25 实测 · 2026-10-08 补测）

| 模型 | 直连 | 走美国代理（如 `HTTP(S)_PROXY=http://127.0.0.1:7890`） |
|------|------|------|
| `deepseek-v4.1-flash` | ✅ 直连可用（2026-10-08 补测；用了 catalog 才有元数据——见排障表） | 未测 |
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

## 历史桶：本地会话列表按 `model_provider` 分桶（2026-10-10 实测）

**症状**：切换账号/profile 后「另一边看不到历史」；或给 provider 改名后某个 profile 的历史整批消失。

**机制**（实验验证，非推断）：客户端列历史固定传 `modelProviders: null` + `useStateDbOnly: true`，
服务端默认**只返回当前 `config.toml` 里 `model_provider` 那一桶**；过滤落在 `state_5.sqlite` 的
`threads.model_provider`（协议里只有显式 `modelProviders: []` 才是"全部 provider"）。
实测：opencode-go 配置→131 条全 opencode-go；official(`openai-http`)→1 条；`-c model_provider=openai`→39 条；
`modelProviders: []`→170 条混合。**改 provider 名 = 换历史桶**，与账号无关，也不丢数据
（rollout 都在 `~/.codex/sessions/`）。

**对策**：`cx-bucket`（`~/.codex/cx-bucket.py`）把整库挂到当前 profile 的 provider 上，切换 profile 时自动跑
（`CX_BUCKET=off` 跳过 / `=db` 只改 DB）。它同时改 **DB + rollout 首行 `session_meta.model_provider`**——
因为 upsert 是 `SET model_provider = excluded.model_provider`，只改 DB 会被重新索引回填冲掉。

- `cx-bucket --dry-run` 看分布/漂移 · `cx-status` 也会显示 `需改桶` / `rollout 漂移`
- 活跃会话（rollout 300s 内被写过）默认跳过，避免换走后 fd 往旧 inode 追加丢日志；会话结束后再跑一次
- 每次改 DB 前自动备份 `state_5.sqlite.bak-cxbucket-<时间>`，只留最近 3 份
- 想让所有 profile **长期**共享同一份历史：把两个 profile 的 `model_provider` 统一成同一个 id
  （各自 `[model_providers.<同id>]` 指向自己的 base_url），再跑一次 `cx-bucket` 即可，之后无需再挂桶

**另一种读全量的入口**：App 里按 project / 分节的目录视图传的是 `modelProviders: []`（全 provider），
只有"最近"列表分桶（代码层证据，未逐一点过 UI）。

## 排障速查表

| 症状 | 根因 | 修复 |
|------|------|------|
| 扩展报 `unknown variant chat/completions, expected responses` | Codex 26.803 不支持 chat/completions | `wire_api = "responses"` |
| 上游 401 Invalid API key | auth.json key 失效 | 更新 auth.json，重新去 opencode.ai 生成 |
| 上游 401 `Missing API key`（**CLI 同配置却正常**，或扩展内置 codex 0.149+ 必现） | codex 0.149.0 收紧认证：自定义 provider（`requires_openai_auth = false` 默认）不再回退读 auth.json | provider 块加 `requires_openai_auth = true`（走 auth.json 的 OPENAI_API_KEY）+ **Reload Window**。勿用 `env_key` 替代——env_key 只认环境变量，GUI 扩展进程拿不到；且与 requires_openai_auth 互斥 |
| 本地报 `Missing environment variable: OPENAI_API_KEY` | 配了 `env_key` 但环境变量未导出（0.149+ env_key 纯环境变量，无 auth.json 回退） | 若走 env_key 方案需 `export OPENAI_API_KEY=...`（仅 CLI 有效，扩展无效）；推荐改用 `requires_openai_auth = true` |
| 上游 403 `only available hosted in China` | 模型需中国区 opt-in | 去 opencode 工作台开启「提供商 → 启用部署在中国的模型」；不开则换 deepseek-v4-pro 等无限制模型 |
| 上游 403 `unsupported_country_region_territory` | 模型有地区限制（如 gpt-5.6-luna 需美国） | 给 Codex CLI 挂美国代理环境变量（`export HTTPS_PROXY=http://127.0.0.1:7890`）；VSCode 扩展配 `http.proxy` 或系统代理。直测：`curl -x http://127.0.0.1:7890 -X POST https://opencode.ai/zen/go/v1/responses ...` |
| 裸 curl 回 `{"error":"MissingSessionID"}`（**CLI 同模型却正常**） | 2026-10 起上游要求 `x-opencode-session` 头；codex 自身不发此头也能通（未被上游拦） | 直测命令加 `-H "x-opencode-session: $(uuidgen)"`；**别据此判 key/模型失效** |
| 404 Not Found（HTML 页） | `base_url` 漏 `/v1` | 必须 `https://opencode.ai/zen/go/v1` |
| `Model metadata for X not found` warning | Codex 无该模型元数据，用 fallback（context window 等按 fallback 值走，会影响压缩时机） | 把模型补进 `~/.codex/model-catalogs/opencode-go.json`——**key 是 `slug` 不是 `id`**，克隆同族条目改 `slug`/`display_name` 即可（2026-10-08 换 `deepseek-v4.1-flash` 时此法消警）；或退回 config 显式 `model_context_window`。⚠️ 上游 `/models` 只回 id/object/created/owned_by，**不带窗口大小**，catalog 里的数值是手工填的，别当权威 |
| 自动压缩后**不继承上文**，把之前做过的 `ls`/`diff`/`cat` **重跑一遍** | ① 压缩机制本身就只留交接摘要（`replacement_history` 里零工具输出），**官方链路同样如此**——重读是设计使然；② 真病灶：手写 catalog 三个字段拧错（见下节） | 按「`model_catalog_json` 三字段」节修正 catalog；改完 `model_context_window` 会跳到 `context_window×95%` |
| `Ignored unsupported project-local config keys: model_provider, model_providers` | **项目级** `.codex/config.toml` 不支持 provider 类 key（Codex 只认 user-level `~/.codex/config.toml`） | 模型/provider 只写在全局 `~/.codex/config.toml`；项目级 config 只放 trust_level 等 key |
| `Codex is ignoring N unrecognized configuration settings`（点 user 级 key 的名） | key 在新版已废弃/拼错 | 0.159.2 下 `disable_response_storage`、`mcp_servers.node_repl.type` 已被忽略（2026-10-08 实测），留着只刷警告，删掉即可 |
| VSCode 面板模型选择器只有默认 gpt 模型（看不到 opencode-go 的 8 个） | `model_catalog_json` 未配置 / catalog 文件缺失（伴随 CLI 侧 `Model metadata not found` warning） | 重建 `~/.codex/model-catalogs/opencode-go.json`（每模型必含 `visibility:"list"`、`supported_reasoning_levels`、`truncation_policy` 等完整字段）+ config.toml 顶层加 `model_catalog_json = "绝对路径"` + **Reload Window**。catalog 是面板模型列表的唯一来源，与代理无关，直连也必须保留 |
| `Codex could not start` / webview 卡住 | 扩展启动需连 chatgpt.com / github.com，网络不通 | 检查外网连通性；重载重试 |
| `codex doctor` 报 WS 超时（**只在 ClashX 关掉「增强模式」的普通规则模式下出现**，开 TUN 就正常） | 官方 provider 默认走 Responses WebSocket（`wss://chatgpt.com/backend-api/...`），WS 客户端**不认 macOS 系统代理**（doctor：`respect system proxy: disabled`）→ 握手超时；但同状态下 HTTPS 可达（`ChatGPT inference URL reachable (HTTP 405)`） | 功能不受影响（codex 自带 WS→HTTPS 降级），但每次会话先付 15s 握手超时 + 最多 5 次重连。**根治=关掉 WS：`cx-http`**（见「网络传输」节）；或让 WS 能握手：`codex features enable respect_system_proxy`（under development）/ ClashX 开 TUN。⚠️ **勿改 `[model_providers.openai]`**——保留 ID，覆盖会让 codex 完全起不来 |
| 配置疑似被改坏 / codex 起不来 | — | 校验**必须用 `codex features list`**（真加载器，配置坏了直接报错）。⚠️ **`codex doctor` 不能当校验器**：配置加载失败时它静默回退默认配置继续跑，只在顶部留一行 `✗ config config could not be loaded` |
| VSCode 改了 config.toml 不生效 | 扩展用内存缓存 | Reload Window |
| 模型身份幻觉（自称 GPT） | 模型训练数据问题，正常现象 | 忽略，以 config.toml 的 model 为准 |

## `model_catalog_json` 三字段（2026-10-08 修正，直接决定压缩行为）

手写 catalog 里这三行决定模型**实际拿到的系统提示词**和**压缩/截断时机**，克隆条目时最容易留错：

| 字段 | 错法（本机曾如此） | 正确 | 后果 |
|------|------------------|------|------|
| `base_instructions` | `"You are a coding agent."`（**23 字符**） | codex 内置回退文本（**20,751 字符**） | 模型没有工具/计划纪律指令，压缩后倾向重跑取证 |
| `context_window` | `131072` | 查 models.dev 的 `limit.context`（这一批都是 1M 档） | 压缩阈值 = `context_window×0.95`；131072 → **124,518**，只有真实容量的 1/8 |
| `truncation_policy.mode` | `"bytes"` | `"tokens"` | `limit:10000` 在 bytes 下 = **10.2KB**，tokens 下 ≈40KB，**差 4 倍** |

**两条 schema 约束（都实测过）**：

- `base_instructions` 与 `model_messages.instructions_template` **至少存在一个**——写 `null` 或不写都报
  `model \`X\` is missing both ... at line N`。想让模型用内置回退提示词，只能把文本**内嵌**进去
  （代价：提示词不再跟随 codex 版本升级）。**提取方法**：遍历 `~/.codex/sessions/**/*.jsonl` 首行的
  `session_meta.base_instructions.text`，取长度 == 20751 的那条（建 catalog **之前**的会话）——
  开头是 `You are a coding agent running in the Codex CLI, a terminal-based coding assistant.`
- **`model_catalog_json` 是整体替换不是合并**：一旦加载，模型列表 = catalog 条目，
  官方 `gpt-6.1-sol` 等**全部消失**；重叠 slug 也取 catalog 的值而非服务器下发的值。
  所以**改动 scope = "哪个 profile 写了这行"，不是"哪个 provider"**
  （`profiles/opencode-go/config.toml` 有，`profiles/official/config.toml` 没有）。
  目录名 `model-catalogs` 在二进制里搜不到（`grep -c` = 0）→ codex **不自动扫描**该目录，只有显式引用才加载。

核查命令：

```bash
# 配置态：codex 实际解析出的元数据（9 条全在 = catalog 生效）
codex debug models | python3 -m json.tool | grep -E 'slug|context_window|"mode"|base_instructions'
# 运行态：新会话 rollout 首行
#   session_meta.base_instructions.text 长度   → 应为 20751
#   payload.model_context_window               → 应为 context_window×0.95
```

**排查手法（通用）**：统计所有 rollout 的 `session_meta.base_instructions.text` 长度分布，
就能看出某条链路实际拿到的是哪个系统提示词。本机实测五档：
23（手写 catalog）/ 17,730 / 18,446（服务端下发）/ 20,751（内置回退）/ 21,769（GPT-6 模板）。

## 网络传输：WS（默认）⇄ HTTPS，与 `cx-http` / `cx-ws`（2026-10-08 落地）

官方 provider 默认传输是 **Responses WebSocket**（doctor 可见 `endpoint wss://chatgpt.com/backend-api/<redacted>`、
`supports websockets: true`）。WS 客户端不认 macOS 系统代理（doctor：`system proxy: manual` 但
`respect system proxy: disabled`），ClashX 关掉「增强模式」只留普通规则模式时会握手超时。
codex 自带 WS→HTTPS 自动降级，**但降级前先付 15s 握手超时 + 最多 5 次重连**——不是报错，是每次会话的成本：

```
$ codex exec "Reply with exactly: OK"        # 官方 provider（ws 传输）
ERROR: Reconnecting... 2/5  3/5  4/5  5/5    # ← 先走完这一遍才降级到 HTTPS
```

```
cx-http     # official → https   纯 HTTPS，不发 WS 握手
cx-ws       # official → ws      恢复内置 provider 默认（WS 优先 + 自动降级）
cx-status   # 同时显示 账号轴 + 传输轴
```

实现（`~/.codex/switch-codex.sh`）＝在 config.toml 插一段带标记的托管块，声明一个
**改名 provider 指回官方同一后端**：

```toml
# >>> cx-transport (managed by switch-codex.sh) >>>
model_provider = "openai-http"

[model_providers.openai-http]
name = "OpenAI HTTPS only"
base_url = "https://chatgpt.com/backend-api/codex"
wire_api = "responses"
requires_openai_auth = true     # ← ChatGPT OAuth 认证照旧
supports_websockets = false     # ← 关掉 WS 握手
# <<< cx-transport <<<
```

- **2026-10-08 实测（0.159.2）**：配置合法；ChatGPT OAuth 保留——真实请求打到官方后端，返回的是
  账号额度错误而非 401；doctor 显示 `✓ websocket Responses WebSocket is not enabled for the active provider`；
  **零重连**（同一账号走 WS 时 5 次 `Reconnecting...`）。
- 托管块必须插在**第一个 `[table]` 之前**——TOML 裸键归属最近的表头，插到 `[projects.*]` 之后
  `model_provider` 会变成某个 project 表的键。
- 传输轴只作用于 official（opencode-go 是自定义 provider，本就不走 WS）；随 profile 持久化，
  cx-off → cx-on 往返后仍是 https。切换后 VSCode 扩展需 Reload Window。

> ⚠️ **勿写 `[model_providers.openai]` 任何键**——`openai` 是 codex **保留的内置 provider ID**，
> 覆盖它会让配置整体加载失败、codex 完全起不来（`model_providers contains reserved built-in provider
> IDs: 'openai'.`）。**2026-09-14** 的 cx-http/cx-ws 正是这么实现而翻车、从 switch-codex.sh 与 .zshrc
> 移除的；**0.159.2 复测仍被拒**。改名 provider 是唯一可行解。

> **校验配置一律用 `codex features list`**（真加载器）。`codex doctor` 不能当校验器：配置加载失败时它
> **静默回退默认配置继续跑**，只在顶部留一行 `✗ config config could not be loaded`——2026-09-14 用 `sed`
> 过滤恰好滤掉了这行，把"WS 检测行消失"误读成"WS 被关掉了"。switch-codex.sh 的传输切换已内置该校验 + 失败回滚。

杠杆对照：

| 杠杆 | 结论 |
|------|------|
| `[model_providers.openai]` 任何键 | ❌ 保留 ID，不可覆盖（0.159.2 复测仍拒） |
| 改名 provider 指回官方后端 | ✅ 本页方案：`requires_openai_auth` 保住 OAuth + `supports_websockets = false` 关 WS |
| `model_catalog_json` + `prefer_websockets: false` | ❌ 实测**不能阻止 WS 首次尝试**；且自定义 catalog 会冻结模型列表 |
| `responses_websockets` / `_v2` 特性开关 | ❌ 0.153 已 **removed** |
| `codex features enable respect_system_proxy` | ✅ 官方对症方案（under development）——**修 WS 让它能握手**，不是换 HTTPS；ClashX 开 TUN 同理 |

验证连通性（**必须在沙箱外跑**，Claude Code 沙箱会 MITM 掉 TLS 导致误判为网络不通）：

```bash
codex doctor          # 完整输出，不要 sed 过滤——顶部那行 ✗ config 是关键信号
codex features list   # 唯一可靠的配置校验器
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
| `opencode-go` | `https://opencode.ai/zen/go/v1` | **订阅** | OpenCode Go，37 个模型（2026-10-08 实测），与 Codex 用的是同一个 |

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

> ⚠️ **`--auto` 会绕过门禁**：它放行一切「未被显式禁止」的操作（写文件、执行命令）。在 `Unity/Lab` 里直接派它写 `Assets/Mine/` 会**跳过 `write_gated`**，违反项目规则。受门禁保护的源码只在**隔离副本或非门禁目录**生成草稿，再由主 agent 经该项目的门禁通道合入（Codex 走 `gate_cli.py write|delete|move`，Claude 走 MCP 工具，同一判定核）。不要假定某一侧只能靠另一侧代合入。

## 关键验证命令

```bash
# 1. CLI 直连
codex exec "Reply with exactly: OK"

# 2. 直测 OpenCode Go responses 端点（验 key/模型）
# 注意 x-opencode-session（2026-10 起上游新增要求）：缺它必回 {"error":"MissingSessionID"}，
# 与 key/模型无关。codex 自身不发此头也能通（未被上游拦），所以裸 curl 失败≠codex 链坏了。
curl -s -X POST "https://opencode.ai/zen/go/v1/responses" \
  -H "Authorization: Bearer $OPENAI_API_KEY" -H "Content-Type: application/json" \
  -H "x-opencode-session: $(uuidgen)" \
  -d '{"model":"deepseek-v4-flash","input":"hi","stream":false}'

# 3. 模型列表
curl -s "https://opencode.ai/zen/go/v1/models" -H "Authorization: Bearer $OPENAI_API_KEY"
```

## Unity/Lab 委派边界

- `ocw` / `opencode` 默认带 `--auto`，放行一切未被显式禁止的写操作，**绕过 `.mcp` 判定核**。
- 受管源码（`Assets/Mine/**`）不得由 opencode 直接写入：先在隔离副本或 `tmp/sessions/<date-task-id>/` 生成草稿，再由主 agent 经 `gate_cli.py write|delete|move` 或对应 MCP 工具合入。
- 委派不改变工作区授权、人工批准通道与 policy 档位；不能以「worker 已经写了」为由跳过门禁。
- 委派说明必须给出目标、允许修改的文件、约束与验收标准；主 agent 复核完整 diff 并对结果负责。

## 关联

- Agent 清单：`.agents/agents/README.md`
- 门禁事实与控制模型：`.agents/knowledge/meta/enforcement-policy.md`
- 技能总索引：`.agents/skills/README.md`

## 与 CC-Switch 的关系

- **Claude Code → OpenCode Go**：仍走 CC-Switch 本地代理（15721），Claude 走 Anthropic Messages 格式，需要 CC-Switch 做翻译
- **Codex → OpenCode Go**：**直连**（responses 原生格式），与 CC-Switch 完全无关
