# Experiment Mode

> 🧪 无库内模板、需搜索网络方案、自主迭代直到目标达成。
> 适用：库内无 template/reference，方案需要从网络搜索，不需要人工逐步观察效果。

---

## When to Use

| 触发 | 示例 |
|------|------|
| 库内无模板/方案 | "实现 SSR"（库里没有 SSR 模板） |
| 需要搜索网络找实现 | "参考 Nvidia 那篇 paper" |
| 不需要每步人工观测 | "跑通就行，效果对了告诉我" |
| 自主迭代直到目标 | "改到和参考图差不多" |

**Don't use for:**
- 需要逐帧看效果的 Shader 调参 → Research Mode
- 有现成模板的框架升级/Bug 修复 → Production Mode

---

## Process

```
用户指令
  │
  ├── [E1] WebSearch → Plan → @capabilities/web-search.md
  │     └── 🔴 暂停，等待用户确认 Plan
  │
  ├── [E2] 迭代执行
  │     ├── @capabilities/knowledge.md
  │     ├── @capabilities/compile.md（≤ 3 次自动修复）
  │     ├── @capabilities/runtime.md（优先结构化验证）
  │     └── @capabilities/backup.md（项目内 .backup/）
  │
  ├── [E3] 失败处理 + 大循环（mode 特有编排）
  │     ├── 单阶段 ≤ 2 次失败 → 继续
  │     ├── 第 3 次 → WebSearch 确认疑点 → 重新进入 E2
  │     └── 3 个大循环全失败 → 兜底退出，保留现场
  │
  └── [E4] 正常退出 → 报告产物路径 + 提示清理
```

---

## 门禁映射（后果验证 v2）

> 链统一 [g_entry, g_knowledge]（G0→G1.5）——所有配方一致。E1 WebSearch→Plan 继续文档驱动，无 MCP 同步；g_web_search/g_plan/g_mode/g_script/g_file 已退役，调用返回 GATE_NOT_IN_RECIPE。

```
G0   → gate_set_recipe("Experiment") + gate_pass("g_entry", agent="<agent>")
G1.5 → gate_pass("g_knowledge", loaded_files="<已读文件列表>", status="COMPLETE")
G3   → write_gated(path, content, ...)   ← 内容规范 + 文件落点检查（error 阻断 / warning 提示）
```

- 写入走 `write_gated`；未过配方门禁、内容违反结构规范、或落点不在已知顶层根下都会 DENIED
- 移动/改名走 `move_gated`（不要拿 write+delete 拼）
- Codex 对等：`python .mcp/validation/check_norm.py <file>`

---

## Rationalizations

| Agent 可能的错误 | 为什么不行 |
|-----------------|-----------|
| "跳过 Plan 确认，直接开始写" | Plan 是 Experiment 唯一的用户介入点。跳过 = 方向错了白迭代。 |
| "编译过了就是成功了" | Experiment 的目标是效果达成，不是编译通过。必须验证输出结果。 |
| "主观看一眼就当验证了"（非视觉目标） | 能用结构化工具验证的不要依赖主观观测。视觉目标才需要人工在 Editor 确认。 |
| "第 3 次了，再试一次" | 3 次失败是硬边界，必须回到 Plan 重新审视方向。盲目重试浪费 token。 |
| "失败了自动回滚" | Experiment 保留现场给用户分析。回滚由用户决定。 |

---

## Red Flags

- Plan 中未找到任何网络方案 → 可能需求不明确或技术不可行，暂停询问
- 连续 2 个大循环在同一个阶段失败 → 方案可能有根本性问题，暂停询问
- Editor 断连超过 60s → 同 Production 兜底退出
- 用户中途插入指令 → 优先响应用户，再决定是否继续 Experiment

---

## Verification

- [ ] E1 Plan 已被用户确认
- [ ] 每次编译失败后修复 ≤ 3 次
- [ ] 非视觉目标未用主观观测代替结构化验证
- [ ] .backup/ 有迭代备份记录
- [ ] 正常退出有产物路径报告
- [ ] 失败退出有方案总结 + 建议

---

## 跨引用

- 模式选择逻辑 + 对比表：../../../agents/unity-developer/AGENT.md
- 编译验证：../capabilities/compile.md
- 运行时验证：../capabilities/runtime.md
- 知识预加载：../capabilities/knowledge.md
