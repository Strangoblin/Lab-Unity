# 业务式管理（MCP v5）

> 2026-10-28 起，`.mcp` 不再是门禁，而是**判定核 + 证据账本**。
> Claude Code 与 Codex 共用同一份判定；两边只留 hook 注册。
> v5 同日修正 v4 的两处误拦：判据从「目录归属」换成「有没有兜底」（git 已入库 / trash 有副本）。

v3 收集了 1261 条摩擦记录，结论很清楚：真正贵的不是检查，是**检查放错了位置**。

| 观察（来自账本） | 说明 |
|---|---|
| 仪式开销比 0.093 | 已降到「不痛」，仪式不是瓶颈 —— 继续加/减仪式都没有收益 |
| 5 次真实误拦全在 `Assets/Mine` 内部 | 沙箱切到叶子目录，跨目录改共享库就被拦 |
| 收尾写记忆 2 次 `WORKSPACE_NOT_ALLOWED` | 记忆库在工作目录边界**之外**，收尾必然撞墙 |
| CLI 679 次 / MCP 309 次 | 写入通道倒挂：内容要先落文件再 `--from`，每次多一次往返 |

于是 v4 换了模型：**入口少管，出口多验；能留痕就不拦；能留底就不拒。**

---

## 四件事

| 目标 | 手段 | 实现 |
|---|---|---|
| 读到该读的 | profile 声明必读 + PostToolUse **观测**真实读取 | `profiles.py` / `session.py` |
| 用到该用的工具 | profile 声明 skills，`open` 返回 playbook | `profiles.py` |
| 改到正确的文件 | 分区 + 会话沙箱，PreToolUse 判定 | `zones.py` + `.mcp/config/zones.json` |
| 工作区内自由操作 | 删除进垃圾桶可恢复；记忆/体系区随时可写 | `lib/validation/trash.py` |

**出口验收**（`close.py`）是 v4 的新主旋钮：交付是否合格由**证据**回答，不由自报告回答。

| 义务 | 证据 | 为什么可信 |
|---|---|---|
| `knowledge` | PostToolUse 记录的 Read 路径 | 工具看到的就是实际发生的，没有申报字段可编造 |
| `norm` | `state/writes.jsonl` 里的违规 | 写入时已判定，这里只汇总 |
| `compile` | `unityctl logs` 的 error 行 | 读编辑器自己的日志；拿不到就记 `UNAVAILABLE`，不冒充通过 |
| `verify` | `Validation~/`、`tmp/sessions/` 下晚于最后写入的产物 | 时间先后可查，早于改动的报告不算数 |
| `memory` | 有实质改动时出现 dated 记录 + 索引更新 | 路径与形状可机械判定 |

---

## 分区（`config/zones.json`）

| zone | 路径 | 规则 |
|---|---|---|
| `asset` | `Assets/Mine/**` | 沙箱内自由写；**已入库的**沙箱外也放行（git 兜底）；新建 / 未跟踪仍须沙箱 |
| `memory` | `.agents/memory/**` | 随时可写，**既有记录也能原地改** |
| `system` | `.agents/{agents,knowledge,skills}`、`.mcp`、平台目录 | 可写，只记录 |
| `scratch` | `tmp/**`、`.mcp/trash/**` | 自由 |
| 分区表之外 | `AGENTS.md`、`.mcp.json`、`.gitignore` 等 | **已入库即放行**，按体系区记录；未入库拒 |

分区表**有序**：`tmp` 与 `.mcp/trash` 必须排在 `.mcp` 之前命中，否则垃圾桶会被当成体系区写入记录。
表是数据 —— 加目录、换平台、调整哪几个区吃 git 兜底（`git_protected.allow_zones`）都改 `config/zones.json`。

**git 保护 = 可随意编辑**（v5）。判据是**有没有兜底**，不是「agent 可靠」：已入库的内容改错
`git checkout` 就回来；未入库（新建、生成目录、仓库外）没有兜底，才拒。这条同时解决了两类实测误拦 ——
`AGENTS.md` 曾撞 `ZONE_DENIED`（项目入口文档恰在模型够不到处），改已落盘的记忆曾撞
`MEMORY_HISTORY_IMMUTABLE`（绕路新建副本比原地改更难维护，该检查已退役）。

沙箱仍在：它管的是**新建与未跟踪**的资产，那部分没有版本库兜底。用户中途指出新地址时用
`cli.py expand` 扩充，确认发生在对话框里，命令只负责留痕。

---

## 通道

```
写入：平台原生 Write / Edit ──→ PreToolUse hook ──→ zones → norms → 落账
                              └→ PostToolUse hook ──→ 记录真实读取/落盘
其余：python3 .mcp/bin/cli.py open | expand | close | delete | move | restore | trash | status | report
```

**移动 / 改名也用原生工具**（`mv` 会顺带搬 `.meta`，GUID 随文件走）。只有**删除与清空**必须进垃圾桶 ——
那里不留底的代价是不可逆，而搬动本身是可逆的。`cli.py move` 保留给需要成对处理 `.meta` 的批量改名。

**没有 `cli.py write`**：多一个写入通道就多一套判定口径 —— v3 的 `write_gated` 就是这样
变成唯一通道的（内容得先落临时文件再 `--from`，每次多一次往返，679 : 309 倒挂由此而来）。

| 文件 | 职责 |
|---|---|
| `bin/hooks.sh` | 传输薄壳：判定核起不来时把「拿不到判定」翻译成 deny（资产区） |
| `bin/cli.py` | 统一入口 |
| `lib/hooks.py` | 平台 hook 判定核：PreToolUse 判定、PostToolUse 记账 |
| `lib/judgement.py` | 判定合成：分区 + 规范 + 落点 + policy 应用 |
| `lib/zones.py` | 分区与沙箱判定（读 `config/zones.json`） |
| `lib/session.py` | 会话状态与证据账本（`state/session.json` / `writes.jsonl` / `reads.jsonl`） |
| `lib/profiles.py` | 业务线：必读知识、技能、完成义务 |
| `lib/close.py` | 出口验收 |
| `lib/telemetry.py` | 摩擦账本 `state/friction.jsonl` |
| `lib/validation/trash.py` | 垃圾桶（留底 + manifest + restore） |
| `lib/validation/unity_assets.py` | 客观事实：git 状态、GUID 读取、被引用查询 |
| `lib/validation/norms.py` | 结构规范（只判新增行） |
| `lib/validation/moving.py` | 移动预检与执行（`.meta` 成对，GUID 不撞） |
| `lib/validation/policy.py` | 策略数据：`block` / `warn` / `off`，只能放宽不能收紧 |
| `lib/validation/friction_report.py` | 聚合 + 提案（人工确认后才改 `config/policy.json`） |

## 目录布局

根目录只留 `README.md`。其余按「入口 / 模块 / 数据 / 状态」分开：

```
.mcp/
├── README.md          ← 根级唯一文件
├── bin/               入口：只有这两个会被平台或人直接调用
│   ├── cli.py         python3 .mcp/bin/cli.py <子命令>
│   └── hooks.sh       hook 传输层（两平台注册都指向它）
├── lib/               模块：被 import，不直接执行
│   ├── hooks.py  zones.py  judgement.py  session.py
│   ├── close.py  profiles.py  telemetry.py
│   └── validation/    判定与通道实现
├── config/            数据：改行为改这里，不改代码
│   ├── policy.json            检查项级别
│   ├── zones.json             分区表
│   └── project-resources.json 资源绑定
├── state/             运行时状态（gitignored，唯一痕迹）
│   ├── session.json
│   └── {writes,reads,deletes,moves,friction}.jsonl
├── tests/             隔离契约测试
└── trash/             删除留底（可 restore）
```

分层理由是**锚点只有一个**：`lib/` 里的模块一律从 `__file__` 退到 `.mcp` 再退到仓库根，
配置与状态按同一个 `_MCP_ROOT` 定位。以前这些东西散在根目录、靠 `__file__` 的邻居找自己，
目录一动就会「找不到自己的配置文件」——这次整理由这条约束驱动。

---

## profile

| profile | 必读 | 完成义务 |
|---|---|---|
| `shader` | 结构规范 + HLSL include | knowledge, norm, compile, verify, memory |
| `compute` | 结构规范 + compute API | knowledge, norm, compile, verify, memory |
| `script` | 脚本结构规范 | knowledge, norm, compile, memory |
| `tooling` | 脚本结构规范 | knowledge, norm, compile, memory |
| `benchmark` | 渲染基准契约 | knowledge, compile, verify, memory |
| `meta` | — | memory |
| `research` | — | — |

`knowledge` 排第一项是有意的：只声明不核查的必读项，等于回到 v3 的「申报即通过」。

---

## 删除是可逆操作，不是审批闸

```
删除 → .mcp/trash/<id>/files/<原路径> + manifest.json（原路径 / git 状态 / GUID 引用 / sha256 / 目录清单）
     → python3 .mcp/bin/cli.py restore <id>
```

于是 v3 的两条硬拦不再需要：
- `tracked-dirty` —— 内容在垃圾桶里，恢复是一条命令
- `guid-referenced` —— 引用还在，恢复后立刻复原

**留底不是审计**：`deletes.jsonl` 记「谁在什么时候删了什么」，trash 记「内容在哪」。
审计回答意图，manifest 回答内容，缺一不可。

---

## 安全红线（不变，且不再分叉）

| 红线 | 理由 |
|---|---|
| `git stash --all` | 永久禁止 —— 会连同别人的未提交改动一起收走。用 `git stash push -- <paths>` |
| `rm -rf Assets/Mine/` | 资产区删除必须走 `cli.py delete`（留底可恢复） |
| 删除证据账本 | `friction/deletes/moves.jsonl`、`session.json`、trash 是「发生了什么」的唯一痕迹 |

三条都是不可逆的。**「agent 自行批准沙箱」不在其中**（v5 去掉）：沙箱扩充改由用户在对话里确认、
agent 执行 `cli.py expand` 并留痕 —— 封一条已经被对话取代的命令只会制造死规则。

v3 把这几条写成两平台各一份 `guard-bash.sh`，靠逐字一致维持等价，再由
`verify.py` 的 `platform_hook_drift` 守着 —— 那是在守一个已经发生的漂移可能。
v4 把它们并进 `lib/hooks.py`：规则只有一份，平台侧只留注册（`verify.py` 现在断言两件事：
两个平台都指向 `.mcp/bin/hooks.sh`，且旧的两份副本没有回来）。

---

## 失败语义

**无兜底路径失败即拒，其它失败即放行。**

判定核出错时，宁可挡住一次正常写入，也不放过一次越界或坏内容；但记忆区与暂存区的
错误由 git 和测试兜住，卡死收尾没有收益。平台上同时配 `onFailure: "block"`
（超时与崩溃）+ `bin/hooks.sh` 里的判空（进程活着但吐了非 JSON）—— 两层各覆盖一半。

---

## 会话生命周期

```bash
python3 .mcp/bin/cli.py open --task "修复 SSC 云边缘" --profile shader \
    --workspace Assets/Mine/Shaders/PostProcess/SSC \
    --workspace Assets/Mine/Special/HLSL
# … 用平台 Write/Edit 直接改（移动用 mv），hook 判定 …
python3 .mcp/bin/cli.py expand --workspace Assets/Mine/Special/HLSL --reason "用户确认: 共享库一起改"
python3 .mcp/bin/cli.py close
python3 .mcp/bin/cli.py close --waive verify --reason "纯逻辑改动，无需画面验证"
```

`trash` 与 `restore` 之外，其余子命令都返回 JSON；退出码 `0` = 通过 / `1` = 被拦或未达出口 / `2` = 用法错误。

---

## 测试

```bash
python3 .mcp/tests/run_isolated.py --task-dir tmp/sessions/2026-10-28-mcp-v5
python3 .codex/tests/06_architecture/verify.py
```

隔离 runner 把 `.mcp` 与知识树拷进一次性 fixture（独立 git 根、独立 TMPDIR），
跑完后校验**生产账本与垃圾桶指纹未变**。永远不要直接重置生产状态。

| 套件 | 内容 |
|---|---|
| `test_zones.py` | 分区、沙箱、hook 真实入口判定、Bash 红线、失败语义 |
| `test_trash_close.py` | 垃圾桶往返与不覆盖、出口验收各项义务 |
| `test_knowledge_paths.py` | 知识 ID 解析、脚本库精确授权、profile 必读可解析 |
| `test_project_resources.py` | 只读资源绑定不授权写入 |

---

## 策略演进

`state/friction.jsonl` 是传感器，`report` 是提案，**人工确认后**才改 `config/policy.json`：

```bash
python3 .mcp/bin/cli.py report            # 人读
python3 .mcp/bin/cli.py report --json     # 机器
```

v4 报告看两件事：哪条检查在真实写入上反复拒绝（可能误拦），哪项出口义务总是缺失
（义务定得不对或环境拿不到证据）。后者不该靠「再努力一点」解决。

改策略前读 `.agents/knowledge/meta/enforcement-policy.md`。
