---
name: autoagent
description: >-
  Adaptive mode router for Unity implementation tasks. PROACTIVELY invoke this
  skill when the user asks to modify any file, write any code, edit any shader,
  organize any assets, refactor any class, or debug any issue in a Unity
  project. Routes to Research Mode (exploratory) or Production Mode
  (systematic). Always loads project knowledge base first. When Unity Editor is
  running, extends pipeline with compile → run → cleanup.
  Do not activate for meta-developer or .agents/.mcp/.claude/.codex maintenance.
user-invocable: true
argument-hint: "<task description>"
---

# AutoAgent

> Unity 开发任务自适应路由器。覆盖所有开发任务，Editor 可用时自动扩展为完整流水线。

## CRITICAL — 门禁驱动的执行流程

**This skill body loads on trigger. 每个 [Gx] 必须输出结构化决策才能继续。**

```
[G0] 框架入口
  OUTPUT: ## G0: Framework Check — Agent: unity-developer | NOT LOADED
  └── NOT LOADED → Read ../../agents/unity-developer/AGENT.md → 重试

[G1] 模式确认
  OUTPUT: ## G1: Mode Selection — Mode: <mode> | Reason: <why>
  └── Research | Production | Experiment

[G1.5] 知识加载校验
  OUTPUT: ## [G1.5] Knowledge Check — Loaded: <已读文件列表> | Status: COMPLETE
  └── 高优先级必读清单 + 判定标准见 capabilities/knowledge.md
  └── 未全读 → 补读后重新输出 COMPLETE，禁止进入下一步（门禁只收 COMPLETE）

Read 对应的 mode 文件 → 按 mode 文件中的 [Gx] 门禁逐步执行
```

**门禁不是建议。每个 [Gx] 必须显式输出决策，下一步读取上一步的输出来决定行为。**

**MCP 同步（unity-gate server 已注册时，[Gx] 输出文本的同时调用对应工具）——后果验证 v2，链唯一 [g_entry, g_knowledge]：**

```
所有模式（配方链一致）:
  G0   → gate_pass("g_entry", agent="<agent>")
  G1.5 → gate_pass("g_knowledge", loaded_files="<已读文件列表>", status="COMPLETE")   ← 唯一实质门禁
  G3   → write_gated(path, content, script_decision="<Decision>", file_type=..., ...)  ← 注解非阻塞
  删除 → delete_gated(paths, reason="<原因>")                                          ← 唯一删除通道
  移动 → move_gated(moves=[{"from":..., "to":...}], reason="<原因>")                   ← 唯一移动通道
```

- `Assets/Mine/` 写入走 `write_gated`、删除走 `delete_gated`、**移动/改名走 `move_gated`**——settings 的 `Edit(/Assets/Mine/**)` deny 会**连带命中 Bash**（`rm`/`mv`/`cp`/写入型 `sed`/`tee`/重定向；只读的 `sed -n` 放行），MCP 工具是唯一通道；未过配方门禁会 DENIED。门禁按**操作**建模，别拿 write+delete 拼一个移动
- **g_mode/g_script/g_file 门禁已退役**：模式走 `gate_set_recipe`，脚本决策/文件分类走 write_gated 注解（记录不阻塞）
- **write_gated 后果验证**：违反结构规范（shader-decl / cs-type-decl / region-added）或**文件落点**（受管文件须在 `Effects`/`Scripts`/`Shaders`/`Special` 之下）→ DENIED；分隔线风格、`category` 注解与路径不符 → warning 提示
- **delete_gated 后果验证**：`tracked-dirty`（有未提交改动）或 `.meta` GUID 仍被引用 → DENIED；`untracked` 放行但标注不可恢复；整批 all-or-nothing，`reason` 必填
- **move_gated 后果验证**：预检零改动（不覆盖目标、GUID 不得已被声明、批内不得链式）；**`tracked-dirty` 的源放行**（内容只是换位置，这正是它存在的理由）；`.meta` 成对搬运保 GUID；`reason` 必填
- **Codex 对等**：`python .mcp/validation/check_norm.py <file>` 全量检查

**报告格式：** Editor 可用/不可用分别注明全流水线或仅代码；Research 标记需人工观测。

---

## 激活条件

| 条件 | 说明 |
|------|------|
| 用户发出开发任务 | 创建/修改代码、Shader、文件整理、重构、调试、文档 |
| 非纯对话/咨询 | 涉及文件写入或项目操作；meta 体系维护除外 |

**边界：** `meta-developer` 或 `.agents/**`、`.mcp/**`、`.claude/**`、`.codex/**` 任务不激活本 Skill、不调用 Unity MCP，改走 meta 工作流。Editor 不是激活前提，只影响 Unity 流水线深度。

## 路由速查

```
用户指令
  │
  ├── 需搜索网络方案 / 无库内模板 / 自主迭代 → 🧪 Experiment Mode
  ├── Shader / 渲染 / 调参 / 效果验证 → 🔬 Research Mode
  ├── 功能开发 / Bug修复 / 重构 / 文件整理 → 🏭 Production Mode
  └── 纯咨询 / 闲聊                  → 不激活
```

完整选择逻辑和边界情况见 [AutoMode.md](AutoMode.md)。
