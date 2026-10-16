---
name: mcp-gate-audit
description: 2026-08-24 — .claude↔.mcp 门禁链路审计 + 修复（g_knowledge 门禁、settings deny、MCP 映射、顺序校验）
date: 2026-08-24
metadata:
  type: project
---

# .claude ↔ .mcp 门禁链路审计与修复

## 背景

.claude（大脑，markdown 门禁）与 .mcp（骨架，Unity Gate MCP Server 工具层校验）各自遵循 ECS 三层解耦（Mode/Capability/Routing）。解耦后两侧同步靠人工，产生缺漏。

## 审计结论

生产模式链路本身可走通（MCP 自测全绿），但存在 3 实质缺漏 + 2 风险：

1. **G1.5 知识加载校验无 MCP 对应门禁** — knowledge.md 的 G1.5 是 2026-08-24 新增，gate_center.py 未同步
2. **write_gated 非强制（最严重）** — memory 声称"settings deny Write/Edit(Assets/Mine/**)"，实测三层 settings（项目/项目 local/用户）均无此条。原生 Write/Edit 可完全绕过骨架
3. **markdown ↔ MCP 无映射指令** — SKILL.md/production.md 门禁契约只要求输出文本，不指示同步调用 gate_pass
4. **MCP 顺序不强制** — pass_gate 只查 requires 不查配方顺序，g_file 可先于 g_script
5. **ALLOWED_AGENTS 不含专用 agent**（边界）— 文档化为"专用 agent 默认 Quick 通道"

研究/实验模式走后门（Quick=[]）确认：配方表有 Research/Experiment 链但实际使用绕开——保留配方作声明，Quick 为实际通道。

## 修复（2026-08-24）

| 修复 | 内容 |
|------|------|
| F1 | 新增 `.mcp/gates/g_knowledge.py`（COMPLETE 需声明已读 shader-structure.md + script-structure.md；PARTIAL DENIED）+ 注册 + Production 配方插入 `g_entry→g_mode→g_knowledge→g_script→g_file` |
| F2 | settings.json deny 加 `Write(/Assets/Mine/**)` + `Edit(/Assets/Mine/**)` → 强制走 write_gated |
| F3 | SKILL.md + production.md 门禁契约加"MCP 同步调用"列（G0→g_entry / G1→g_mode / G1.5→g_knowledge / G2→g_script / G3→g_file） |
| F4 | gate_center.py pass_gate 加配方顺序校验（PREREQUISITE_ORDER） |
| F5 | SKILL.md 文档化：Quick 通道 = 实验/研究不可预判流程 + meta 维护 + 专用 agent（subagent） |

测试：test_recipes.py 扩展 3 用例（PARTIAL DENIED / COMPLETE 缺文件 DENIED / 跳序 DENIED），7 用例全绿。

## 经验

- **两侧并行架构（.claude + .mcp）改一侧必须同步另一侧**。G1.5 新增时只改了大脑，骨架缺环一个月未发现——门禁体系的变更检查清单应含 .mcp 配方
- **memory 声称的强制配置需实测验证**：2026-08-07 memory 记录"settings deny Write/Edit"与实现不符（从未存在），导致骨架"硬校验"名存实亡数月
- 配方变更的连锁：Production 4→5 门禁后，原测试链自动 DENIED（PREREQUISITE_ORDER）——测试文件是变更的哨兵

相关：[[ecs-decoupling-refactor]]、[[unity-gate-mcp-server]]、[[screenshot-removal]]
