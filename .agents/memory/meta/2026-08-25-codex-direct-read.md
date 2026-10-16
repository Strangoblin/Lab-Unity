---
name: codex-direct-read
description: 2026-08-25 — Codex 接入模型变更：不再专门作执行，同样执行开发任务；知识从"编译镜像"改为"直接读 .claude/ 权威源"
date: 2026-08-25
metadata:
  type: project
---

# Codex 接入模型：编译镜像 → 直接读取

## 变更（2026-08-25，用户决策）

1. **Codex 不再专门作为执行存在**——同样执行开发任务（独立功能/修复），非仅"基础重复工作"
2. **Codex 直接读 `.claude/` 权威源**——不再依赖编译版 AGENTS.md 搬运知识

## Before / After

| 维度 | Before | After |
|------|--------|-------|
| Codex 角色 | 纯执行（"手"），只接基础工作 | 开发任务 + 基础工作双轨（落地执行/自主开发由 agent 区分） |
| 知识来源 | `.codex/AGENTS.md` 编译产物（.claude → 编译 → 软链），Codex 读不到 .claude | **直接读 .claude/**（CLAUDE.md / agents / rules / references / memory）；AGENTS.md 仅作入口指引 |
| 同步频率 | .claude 每次变更后重编译 | 仅入口清单结构变更时更新（知识内容零搬运） |
| 门禁边界 | —（不写 Assets/Mine/） | 开发产出经 Claude review + write_gated 门禁链合入（Codex 无 MCP 工具链） |
| 职责划分 | Claude=大脑，Codex=手 | Claude=体系裁决+门禁，Codex=开发落地（双开发引擎） |

## 落地文件

- `.codex/INTERFACE.md` — 契约重写：能力声明加"知识读取=直接读 .claude/"、文件清单加 SKILL.md 镜像与 agents/、派发约定加开发任务+沙箱红线+门禁边界、同步规则改"仅入口变更"
- `.codex/AGENTS.md` — 从"编译产物（勿手改）"改为**入口指引**：读取顺序清单 + 快速版规则 + 门禁边界；保留迁移历史注记
- `.codex/SKILL.md` — codex-opencode-go 镜像同步（补 model_catalog_json 排障行）
- `.codex/agents/exec-developer.md` / `auto-developer.md` — 清理 8-24 迁移漏改的 `Assets/MarkDowns/` 活引用 → `.claude/agents/unity-developer/references/`
- `.claude/skills/codex-bridge/SKILL.md` — 核心机制改"直接读取"（消除编译镜像漂移：8-17 编译版曾落后于 8-24 迁移）
- `.claude/skills/codex-orchestrate/SKILL.md` — frontmatter 描述 + 派发类别（加"开发任务"）+ ❌ 留 Claude 表（改"裁决与体系"）+ 陷阱/关系更新
- `.codex/config.toml` 退役归档 `config.toml.bak-20260825`（项目级 provider 类 key 无效，只认全局）；全局 `~/.codex/config.toml` 8-25 更新（主模型 gpt-5.6-luna，review_model=deepseek-v4-flash）

## 验证

- 软链 `AGENTS.md → .codex/AGENTS.md` 完好
- grep 残留：无"编译产物/勿手改/读不到 .claude"等旧表述（MarkDowns 仅迁移历史注记）
- `codex exec -C <root> "Reply with exactly: BRIDGE-OK"` → BRIDGE-OK（链路连通）

## 注意

- codex-opencode-go skill 的 `.codex/SKILL.md` 镜像是唯一保留的"搬运"物（链路手册无漂移风险）；权威源在 `.claude/skills/`，改源后 cp 同步
- CLAUDE.md 的 Codex 路由表（exec-developer 落地执行 / auto-developer 自主全流程）保持有效，与新模型兼容

相关：[[mcp-gate-audit]]（deny 保留 + MCP 接线）、[[gate-recipe-layering]]（门禁链分层）
