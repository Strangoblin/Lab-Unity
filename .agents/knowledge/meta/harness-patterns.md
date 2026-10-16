# Harness Design Patterns

> meta-developer 创建任何新内容时必须对照的原则。

---

## 核心原则

| # | 原则 | 检查方式 |
|---|------|---------|
| **P1** | **MD 做索引，文件做内容** | MD ≤ 80 行；代码块 > 20 行 → 提取为独立文件 |
| **P2** | **Skill 引用 Script，不包含 Script** | skill/capability 中无完整代码；代码在 scripts/ |
| **P3** | **Reference + Template 配对** | reference 做 API 速查；template 放可运行代码 |
| **P4** | **每次改动后精简去重** | grep 重复关键词 → 合并 → 拆分过大的 MD |
| **P5** | **渐进式披露** | 索引 → 概要 → 细节；不要一次性塞入上下文 |
| **P6** | **AI-friendly 格式** | 用模型"天然见过"的标准格式和开源协议 |

---

## 文件放置决策

```
新内容
  │
  ├── 是"如何组织/规范"的索引？     → MD 文件（≤ 80 行）
  ├── 是"可运行的完整代码"？        → templates/ 或 scripts/
  ├── 是"某领域的 API 速查"？      → references/<domain>/
  ├── 是"编辑文件时加载的规则"？    → rules/（带 paths: frontmatter）
  ├── 是"项目事实/决策记录"？       → memory/YYYY-MM-DD-<slug>.md
  ├── 是"怎么做一件事"的指令？      → skills/<skill>/capabilities/
  └── 是"多步骤编排"？             → skills/<skill>/modes/

特殊情况：
  ├── 内容只服务一个 agent → 放 agents/<name>/
  └── 跨 agent 共享         → 顶层（CLAUDE.md, rules/, skills/）
```

---

## 目录数量控制

| 条件 | 动作 |
|------|------|
| 目录下 ≤ 2 个文件 | 考虑内化到父级（如 platforms/ 2 文件 → 并入 agent.md） |
| MD 文件 > 80 行 | 拆分：提取代码到文件，MD 只留索引 |
| 同一概念在 3+ 文件出现 | 合并去重 |
| 两个文件内容重叠 > 50% | 合并为一个 |

---

## 门禁分支树 — Capability Branching Tree

> mode 定义门禁序列（WHEN），capability 定义分支逻辑（HOW）。
> mode 文件不嵌入决策细节，只引用 capability。

### 结构

```
mode/production.md           → 定义 [G0]→[G1]→[G2]→[G3]→P3→...
  │
  ├── [G2] → capabilities/script-decision.md   (脚本决策分支树)
  └── [G3] → capabilities/file-placement.md    (文件放置分支树)
```

### 创建规则

1. **mode 只做索引** — 每个 [Gx] 一行 `→ @capabilities/<name>.md`
2. **capability 做分支** — 包含完整的 ASK→DECIDE→OUTPUT 决策树
3. **分支树格式** — 每层 `ASK:` + `├──` / `└──` 决策路径
4. **OUTPUT 格式** — 每个 capability 末尾定义结构化输出契约

### 反例

```
❌ mode 中嵌入 30 行决策逻辑
✅ mode 中写 "[G2] → @capabilities/script-decision.md"
```

---

## ECS 三层解耦

> 从 2026-08-07 架构审计提取。Mode/Capability/Routing 严格分层。

| 层 | 职责 | 可引用 | 禁止引用 |
|----|------|--------|---------|
| **Mode** (编排) | 门禁序列 + @capabilities 引用 | @capabilities, agents/ | 不嵌入 capability 逻辑 |
| **Capability** (能力) | 决策分支树 + OUTPUT 格式 | rules/, 领域路径 | agents/, 具体脚本文件名 |
| **Routing** (路由) | mode 名, agent 名 | SKILL.md, CLAUDE.md | 不嵌入 domain 逻辑 |

> 反例：compile.md 引用 `../../../agents/unity-developer/AGENT.md 兜底退出` ← capability 跨层引用 agent
> 正例：compile.md 写 "暂停，报告 mode 层处理退出" ← capability 不知道 agent 存在

---

## 反模式

| 反模式 | 正确做法 |
|--------|---------|
| MD 包含 200 行代码 | 代码 → templates/xxx.cs，MD 只写 "模板: templates/xxx.cs" |
| reference 和 template 混在一个文件 | 分离：reference.md (索引) + template.xxx (代码) |
| 新建目录只放 1-2 个文件 | 内化到父级 |
| 多个文件重复同一概念 | 合并，只保留一处权威来源 |
| learnings/ 作为独立目录 | 错误 → rules/，经验 → memory/ |
| platforms/ 作为独立目录 | 并入 agent.md |
