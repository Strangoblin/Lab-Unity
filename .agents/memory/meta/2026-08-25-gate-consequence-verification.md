---
name: gate-consequence-verification
description: 2026-08-25 — 门禁收敛为后果验证（v2）：链统一 [g_entry, g_knowledge]，知识证据 + 内容规范检查，Codex 经 check_norm CLI 对等
date: 2026-08-25
metadata:
  type: project
---

# 门禁收敛为后果验证（v2）

## 背景

用户判定 MCP 门禁「鸡肋」：① 只有 Claude 用，Codex 无通道；② 大部分情况知识本来就读，门禁链摩擦大（agent 撞墙）；③ 门禁校验的是**自报告**（loaded_files 子串匹配），编造即通过，撞墙的 agent 会找捷径（Bash 直写无 deny）。

诊断根因：系统处于「高摩擦 × 弱保证」最差角落——硬阻断链 + 自报告校验，且 MCP 机制天然 Claude-only。

## 决策（用户选方案一：后果验证门禁）

1. **链唯一**：所有配方 = `[g_entry, g_knowledge]`（配方保留为模式声明，用于审计）
2. **g_knowledge 实质化**：声明条目必须解析到 references/、rules/ 真实 .md，或项目内真实文件（参考实现）；编造文件名 → `G15_UNRESOLVED_FILE` DENIED；PARTIAL 废弃（只收 COMPLETE）
3. **write_gated 后果验证**：内容执行结构规范检查（norms.py）——error 阻断（NORM_VIOLATION）/ warning 提示放行
4. **新增行 diff**：对已存在文件只检查本次写入新增的行——历史遗留不合规（旧文件早于规范，校准发现 PBRFunction.hlsl、FurRenderer.cs 等仍在用 `// ===` 分隔线）不阻断新写入
5. **g_mode/g_script/g_file/g_web_search/g_plan 退役**：保留注册，调用返回 GATE_NOT_IN_RECIPE 带退役提示；脚本决策/文件分类降级为 write_gated 注解（`script_decision` 复用 script_library 校验，非阻塞记录审计 `state.writes`）
6. **Codex 对等**：`.mcp/validation/check_norm.py <file>` CLI（全量检查，exit 1 = error 违规）——Codex 合入前自查，Claude merge 复查同一检查

## 规范规则（norms.py，v1）

| id | 级别 | 检测 |
|---|---|---|
| shader-decl（必须含 `Shader "..."`） | error | 全量 |
| cs-type-decl（必须含类型声明） | error | 全量 |
| region-added（禁止 #region） | error | 仅新增行 |
| divider-added（分隔线统一 ═ 风格） | warning | 仅新增行 |

来源：shader-structure.md §7 / script-structure.md §6。校准：Shader 声明 8/8 零误报；分隔线/#region 旧文件违规 → 新增行 diff 解决。

## 落地文件

- `.mcp/validation/norms.py`（新增）+ `check_norm.py`（新增）
- `.mcp/gates/g_knowledge.py`（真实文件解析重写）
- `.mcp/gate_center.py`（RECIPES 统一链 + RETIRED_GATES + writes 审计）
- `.mcp/server.py`（v0.5.0，write_gated 后果验证 + 注解）
- `.mcp/tests/test_recipes.py`（v2 重写，13 块全绿）
- 文档：README / CLAUDE.md G0 / mcp-gate-usage.md（速查重写）/ SKILL.md 门禁块 / modes 三件 / script-decision.md / .codex/AGENTS.md / codex-orchestrate SKILL.md

## 验证

- 测试套件 13 块 All tests passed ✓（含编造文件 DENIED、参考实现 OK、新增行 diff、CLI exit 码）
- 活动文档 grep 旧链引用零残留（历史 memory 快照除外，meta-architecture 规则）

## 前代决策

[[gate-recipe-layering]]（按模式分层链）被本次收敛取代——门禁从「流程仪式」转向「知识证据 + 后果验证」。
