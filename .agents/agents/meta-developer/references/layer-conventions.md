# .claude 架构约定

> 当前架构的各层文件、命名、引用规则。meta-developer 创建/修改时必须遵守。

---

## 层级总览

```
.claude/
├── CLAUDE.md                    # 项目身份 + 宪法引用（不重复内容）
├── settings.json / .local.json  # 权限 + 模型配置
│
├── rules/                       # 路径限定开发规范（path-scoped loading）
│
├── skills/                      # 过程性知识（Claude Code 要求顶级）
│   └── <skill>/SKILL.md         #   入口 → capabilities/（原子操作）→ modes/（编排）
│
├── scripts/hooks/               # 生命周期钩子
│
└── agents/
    ├── <name>.md                 # agent 定义（YAML frontmatter + 宪法 + 自描述）
    └── <name>/                   # agent 专属资源
        ├── memory/               #   日期命名: YYYY-MM-DD-<slug>.md + MEMORY.md 索引
        ├── references/           #   API 速查（MD ≤ 80 行）+ README.md 索引
        ├── templates/            #   代码模板（standard/script/shader 家族; 独特族只 README.md 为 md）
        ├── cli/                  #   命令参考文档
        └── scripts/              #   可执行脚本（Roslyn .cs 等）
```

---

## 各层约定

### Agent 层

| 约定 | 说明 |
|------|------|
| 文件命名 | `<domain>-developer.md` 或 `<domain>-maintainer.md` |
| **YAML frontmatter** | 必须有 `name`、`description`、`tools`、`model` |
| 必须有 | 职责边界、工作流、会话收尾、**自描述段**、跨引用 |
| 自描述段 | `## 自描述` 开头，含依赖、知识边界、触发条件 |
| 路由注册 | 全局 `~/.claude/agents/default.md` |

### Reference 层 — P1: MD 做索引，文件做内容

| 约定 | 说明 |
|------|------|
| 每个子目录 | 必须有 README.md 索引 |
| MD 文件 | ≤ 80 行，只放 API 速查表 + 差异对照 |
| 完整代码 | 放 templates/（.shader / .compute / .cs / .hlsl），不放 references/ |
| 内容去重 | 同一概念只在一处出现 |

### Template 层 — 与 Reference 配对

| 约定 | 说明 |
|------|------|
| 家族结构 | standard/（函数无关）· script/（责任族）· shader/（特征族）; 现况家族树见各 agent `templates/README.md` |
| 文件类型 | 可运行的 .shader / .compute / .cs / .hlsl; standard/ 另许 doc .md |
| 族目录规则 | 独特族（script/*、shader/*）只有 README.md 是 markdown, 其余 = 代码模板体 |
| 横幅与 ⚠️ | ═ 横幅 + 中文（定位/实源/使用方式）; ⚠️ 只入注释行与字符串, **不得进标识符**（非 ASCII 在标识符内是编译错误, 2026-09-20 实测 CS1056） |
| 占位符 | YourXxx 单一合法 token（YourBaker / YourEffect / YourDomain）, 拷贝后全局替换 |
| 来源标注 | 头部注释写明 Assets 实源路径; 旧写法按现行规范重写, 不逐字照抄 |
| Metal 兼容 | 所有模板默认 Metal 兼容 |
| 细则 | 编写规则 + 验证清单 → [template-conventions.md](template-conventions.md) |

### Skill 层 — P2: 引用 Script，不包含 Script

| 约定 | 说明 |
|------|------|
| 目录结构 | `SKILL.md`（入口）+ `capabilities/`（原子）+ `modes/`（编排） |
| 不包含代码 | capability 中超过 10 行代码 → 提取到 scripts/ |
| 渐进式披露 | SKILL.md 只写触发+路由，细节在 capabilities/ |

### Rules 层

| 约定 | 说明 |
|------|------|
| paths: frontmatter | 必须声明匹配的文件路径 pattern |
| 内容 | 编辑该类型文件时的规范约束 + 错误诊断 |
| 不重复 | 不与 references/ 中的 API 速查表重复 |

### Memory 层

| 约定 | 说明 |
|------|------|
| 文件命名 | `YYYY-MM-DD-<slug>.md` |
| 索引文件 | `MEMORY.md` / `memory.md` 列出所有日期文件 |
| 内容归属 | 每个 agent 的 memory 只记录自己领域的内容 |

---

## 反模式速查

| 反模式 | 正确 |
|--------|------|
| 目录下只有 1-2 个文件 | 内化或合并 |
| MD 超过 80 行 | 提取代码到文件，MD 只留索引 |
| references/ 放完整代码 | 代码 → templates/ |
| capabilities/ 中嵌入 C# 代码 | 代码 → scripts/roslyn/ |
| 同一概念在 3 处出现 | 合并为 1 处权威来源 |
| 新建 `learnings/` 目录 | 经验 → memory/，规范 → rules/ |
| 新建 `platforms/` 目录 | 内容 → agent.md 内 Editor 段 |
| 声称模板文件已建但未落盘 | 声称与落盘一一对应（editor-baker-window 悬空前科） |
| 独特族目录混入第二个 .md | 内容并入族 README 后删除 |
