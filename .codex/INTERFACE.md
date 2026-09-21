# Codex 侧接口契约（.codex/）

> 本文件是 **Codex 侧**对外接口的权威来源。共享权威源是 `.agents/`（入口见项目根 `AGENTS.md`）；`.claude/` 与 `.codex/` 是各自客户端的薄适配层，`.mcp/` 是执行门禁基础设施。
> 知识模型：**单一权威源 `.agents/`，Codex 直接读取**（2026-09-04 随架构解耦重构切流；此前"直接读 .claude/"模型已废止）。
> Claude 侧对应接口由 2 个 skill 组成（分工见 §1.1）：codex-bridge（契约+入口同步）、codex-opencode-go（链路搭建与排障）。

## 1. 本侧（Codex）能力声明

| 能力 | 说明 |
|------|------|
| 自动读取 | 项目根 `AGENTS.md`（trusted 目录内自动加载）→ 指向 `.agents/README.md` 与共享角色入口 |
| 知识读取 | **直接读 `.agents/`**：`.agents/agents/<role>/AGENT.md`（宪法/模式/退出条件）、`.agents/rules/`（开发规范）、`.agents/agents/unity-developer/references/`（知识库）、同角色 `memory/`（项目上下文）。顺序：根 AGENTS.md 入口 → `.agents/README.md` → 按任务读共享文件 |
| 不自动读取 | 无（`.agents/` 需显式读取；入口已列出清单，照单读） |
| 认证 | 全局 `~/.codex/auth.json`（OpenCode Go key，chmod 600）；项目级 `.codex/auth.json` 仅 CODEX_HOME 隔离方案使用（默认不存在） |
| 链路 | 直连 `https://opencode.ai/zen/go/v1`（`wire_api = "responses"`，无代理）。**全部配置在全局 `~/.codex/config.toml`**；项目级 `.codex/config.toml` 已退役（2026-08-25 归档为 `config.toml.bak-20260825`——Codex 忽略项目级 provider 类 key） |
| 审查模型 | `review_model` 必须显式指向本区域可用模型（默认 `deepseek-v4-flash`）——auto_review 审批走独立模型，不设则提权全部 403 |
| 模型目录 | 全局 `~/.codex/model-catalogs/opencode-go.json` + config.toml `model_catalog_json`（VSCode 面板模型列表来源） |
| 门禁边界 | Codex **无 MCP 工具链**，不持有 write_gated 通道——Codex 自行写 `Assets/Mine/` 前先自查 `python3 .mcp/validation/check_norm.py <file>`；需走完整门禁链的产出由持 MCP 通道的一侧（Claude）合入 |

### 1.1 Claude 侧对应 skill 分工

| Skill | 职责 |
|-------|------|
| `codex-bridge` | 双边契约（本文档对侧）+ AGENTS.md 入口同步（共享源变更 → 更新入口指引） |
| `codex-opencode-go` | 链路搭建与排障（config.toml / auth.json / 模型可用性 / 404/403/stream 问题；含 opencode CLI 接入与派活） |

## 2. 接口文件清单

| 文件 | 角色 | 维护者 |
|------|------|--------|
| `AGENTS.md`（项目根，普通文件） | 平台无关入口；指向 `.agents/`（Codex trusted 目录内自动加载）。迁移后为普通文件，不再是软链 | 共享 |
| `.codex/AGENTS.md` | Codex 自动加载**入口指引**（薄适配，指向 `.agents/` 权威源；非知识全量、非编译镜像） | 低频同步（共享源入口变化时） |
| `.codex/INTERFACE.md` | 本文档：接口契约 | 双边 |
| `.codex/SKILL.md` | codex-opencode-go 接入手册的 **Codex 侧镜像**（权威源在 `.agents/skills/codex-opencode-go/SKILL.md`；镜像自动生成或同步，不手改） | 同步 |
| `.codex/agents/` | Codex 运行时角色适配：`unity-developer.toml` / `meta-developer.toml`（正文指向共享 AGENT.md）；不再维护 exec/auto 替代 agent | 双边 |
| `.codex/agents/unity-developer/`、`.codex/agents/meta-developer/` | 共享角色目录的只读相对软链；不承载 Codex 专属运行时适配 | 共享结构同步 |
| `.codex/rules/`、`.codex/skills/`、`.codex/knowledge/`、`.codex/interfaces/` | 共享目录的只读相对软链；仅作 Codex 路径兼容别名 | 共享结构同步 |
| `.codex/config.toml.bak-20260825` | 项目级 config 归档（退役，勿恢复） | — |
| `~/.codex/config.toml` | 实际生效的直连配置（主模型 / review_model / provider / catalog） | 双边 |
| `~/.codex/auth.json` | API key（不入 git，chmod 600） | 双边 |

## 3. 并行协作约定（Claude ↔ Codex）

Claude 与 Codex 是**对等并行**的两个开发引擎，**不是派发/从属关系**（与 §6 一致）。各自独立承接任务、独立落地。

- **知识源**：两侧都**直接读 `.agents/`**（入口：根 `AGENTS.md` → `.agents/README.md` → 角色 `AGENT.md` / `rules/` / `references/`）。不互为镜像，也不需要任一侧代为转述
- **角色路由**：按任务选共享角色（`unity-developer` 开发 / `meta-developer` 体系维护），显式引用 `.agents/agents/<role>/AGENT.md`，并说明 Production / Research / Experiment 模式
- **门禁边界**：`Assets/Mine/` 的 `write_gated` / `delete_gated` 通道**只有 Claude 侧的 MCP 工具持有**。Codex 自行写 `Assets/Mine/` 前先自查 `python3 .mcp/validation/check_norm.py <file>`；需走完整门禁链的产出交给 Claude 合入
- **并发安全**：同一 git 仓库内两个引擎**同时写文件**会互相冲突，应避免；只读并行无碍
- **Codex 侧调用**：`codex exec -C <项目根> --sandbox workspace-write --skip-git-repo-check "<prompt>"`；沙箱红线 `--sandbox read-only`（只查）/ `workspace-write`（✅ 默认）/ `danger-full-access`（⛔ 永不使用）
- **大任务（>2KB 指令）**：写成任务书文件让执行方读取，避免上下文截断
- **模型**：默认 `deepseek-v4-flash`（全局 config.toml 主模型，实际以全局配置为准）；复杂任务可 `--model` 覆盖

## 4. 同步规则（共享单源）

- 源：`.agents/`（AGENT.md、rules/、references/、skills/、memory/）——**共享权威知识源，Codex 直接读取**
- 方向：共享正文只在 `.agents/` 编辑一次；`.codex/` 只在入口条目变化（路径/清单调整）时同步 `.codex/AGENTS.md` 与 `.codex/SKILL.md`（镜像），不回写
- 软链：`.codex/agents/{unity-developer,meta-developer}`、`.codex/{rules,skills,knowledge,interfaces}` 只提供相对路径兼容，不得在 `.codex/` 软链目标下创建第二份正文
- 触发：入口条目变化时由维护方更新（codex-bridge skill）；**共享知识内容变更无需重编译本入口**（Codex 读源）
- Codex 侧需要新规则 → 在 `.codex/AGENTS.md` 顶部注释中标注建议，由共享维护方在 `.agents/` 落盘后生效
- 编辑共享层或平台边界前：先读 `.agents/rules/meta-architecture.md`（链路保障——改一侧必须同步另一侧；`.agents` ↔ `.claude` / `.codex` ↔ `.mcp` 并行层）

## 5. Codex 临时产物

- 新生成的 `.cs`、`.md`、`.json`、测试脚本和其他中间文件，先保存到 `.codex/tmp/` 或任务子目录。
- 不要把未经确认的新内容直接写入 `.agents/` 或 `.claude/`；`.agents/` 是共享权威源，由对应角色（unity-developer / meta-developer）负责归档位置、去重和同步。
- 写入 `Assets/` 的功能资产按用户任务范围和项目门禁执行。
- `.codex/tmp/` 内容不是权威知识，任务结束后由调用方审查、转移或清理。

## 6. 职责划分（对等模型）

| 层 | 职责 |
|----|------|
| 共享层 `.agents/` | 角色正文、rules、references、templates、scripts、skills、memory 的唯一编辑位置 |
| Claude `.claude/` | Claude 平台适配：settings、hooks、rules 自动注入、agent/skill 发现兼容层 |
| Codex `.codex/` | Codex 平台适配：config、hooks、tests、tmp、运行时角色 toml 与 agent 文档 |
| MCP `.mcp/` | 执行门禁与规范验证（write_gated / delete_gated / check_norm） |

Claude 与 Codex 对等执行开发任务（落地执行 + 自主全流程）；知识体系与门禁链由共享维护方（meta-developer）裁决，不默认任一侧的从属关系。
