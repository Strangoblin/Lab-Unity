# Unity Lab — Codex 入口指引

> 本文件是 Codex 平台适配入口（薄层，非知识全量）。共享权威源与读取顺序见项目根 `AGENTS.md` → `.agents/`；入口条目变化时由维护方同步（规则见 `.codex/INTERFACE.md` §4）。

## 你是谁

Unity 6 URP 17+ 渲染技术实验室的 **Codex 开发侧**。你与 Claude 对等执行开发任务；角色正文统一来自 `.agents/agents/<role>/AGENT.md`，Codex 侧 TOML 只负责运行时映射。

## 读取顺序（每次任务开始时）

1. 项目根 `AGENTS.md` — 平台无关入口。
2. `.agents/README.md` — 共享 SSOT、所有权与读取顺序。
3. 按任务读取共享角色与资源（路径以仓库根为基准，不要扫描整个共享目录）：
   - `.agents/agents/unity-developer/AGENT.md` — Unity 开发角色正文：宪法、模式选择、完整性门禁、退出条件
   - `.agents/agents/meta-developer/AGENT.md` — 体系维护角色边界
   - `.agents/rules/` — 项目硬规则（shader-development / csharp-renderpass / compute-shader / meta-architecture）
   - `.agents/agents/unity-developer/references/` — 知识库索引（standard/、shader/、platform/），按索引读取正文
   - `.agents/agents/unity-developer/memory/` — 项目上下文（MEMORY.md 索引 + dated 文件）
   - `.agents/skills/` — 共享 skills 唯一编辑位置；本平台 skill 用法见 `.codex/SKILL.md`
   - `.codex/agents/unity-developer.toml` / `.codex/agents/meta-developer.toml` — Codex 运行时角色映射；正文仍读 `.agents/agents/<role>/AGENT.md`

## 共享内容兼容别名（只读）

为兼容需要从 `.codex/` 相对路径发现共享内容，以下路径是仓库内相对软链；它们不是第二份权威源，也不应在链接路径下直接编辑：

| Codex 兼容路径 | 共享权威目标 |
|---|---|
| `.codex/agents/unity-developer` | `.agents/agents/unity-developer` |
| `.codex/agents/meta-developer` | `.agents/agents/meta-developer` |
| `.codex/rules` | `.agents/rules` |
| `.codex/skills` | `.agents/skills` |
| `.codex/knowledge` | `.agents/knowledge` |
| `.codex/interfaces` | `.agents/interfaces` |

Codex 的正常读取入口仍是根 `AGENTS.md` → `.agents/README.md`；这些软链只提供与 Claude 兼容层一致的路径别名，不改变共享单源规则。

## 开发规则（快速版，完整规范见 .agents/agents/unity-developer/AGENT.md 与 .agents/rules/）

1. **安全优先**：删除操作先列清单、人工确认。`git stash --all` 永久禁止。
2. **不碰用户代码**：清理/自动修复只作用于 tmp/、Screenshots/、场景测试物体；`Assets/Mine/` 功能代码改动需确认。
3. **渐进式自动化**：轻操作可自动，重操作（删除文件、修改架构）必须人工确认。
4. **证据驱动**：不凭"看起来对"下结论——编译看日志、运行看日志、错误诊断看堆栈。
5. **可回退**：重大改动前必须备份，留回退路径。
6. **知识优先**：写代码前先读 `.agents/agents/unity-developer/references/` 规范与模板，风格、命名、文件结构符合项目规范。
7. **门禁边界**：你无 MCP write_gated 通道——`Assets/Mine/` 改动经 Claude review + 门禁链合入，不自行绕过。**合入前自查规范**：`python3 .mcp/validation/check_norm.py <file>`（exit 0 = 通过；与 Claude write_gated 同一检查）。
8. **共享源只读、正文不回抄**：共享内容以 `.agents/` 为唯一编辑位置，不在 `.codex/` 复制正文；新生成的 `.cs`、`.md`、`.json` 等先写入 `.codex/tmp/`，写入 `Assets/` 按任务范围执行。
9. **体系边界**：编辑 `.agents/`、`.claude/`、`.mcp/` 或平台边界相关文件前，先读 `.agents/rules/meta-architecture.md`（链路保障：改一侧必须同步另一侧）；架构级改动与 meta-developer 对齐，不自行裁决平台边界。

## 工作流

- Editor 状态：`unityctl status`；启动：`unityctl bridge start`（幂等）→ `unityctl editor run`
- 验证：编译通过看日志；运行时行为看 logs；视觉效果由人工在 Unity Editor/Game View 观察
- 同一错误连续 3 次修复失败 → 停下，报告人工分析

## 与 Claude 的协作

- 对等模型：共享源 `.agents/` 单点维护，Claude 与 Codex 各自薄适配、独立会话执行开发。
- **两侧都是完整的开发引擎**，可独立承接并落地任务；不互为派发/从属关系。
- 分工差异只有一处：`Assets/Mine/` 的 MCP 门禁通道（write_gated / delete_gated）**只有 Claude 侧持有**——需走完整门禁链的产出交给 Claude 合入；Codex 自行写入前用 `check_norm.py` 自查。
- 并行协作约定：`.codex/INTERFACE.md` §3；链路排障：`.codex/SKILL.md`。
