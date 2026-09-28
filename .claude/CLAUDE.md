# Unity Lab — Claude 项目入口

> 本文件只保留 Claude Code 的项目入口、门禁和平台适配说明。跨平台共享知识唯一权威源为 `.agents/`；不要在 `.claude/` 编辑共享正文。

## 项目身份

Unity 6 + URP 17+ 渲染技术实验室，涉及 Shader / HLSL / Compute / RenderGraph / C# 与 Unity Editor 验证。

## 读取顺序

1. 根 `AGENTS.md`：跨平台项目入口与安全边界。
2. `.agents/README.md`：共享 SSOT、所有权和迁移状态。
3. `.agents/agents/<role>/AGENT.md`：按职责加载 `unity-developer` 或 `meta-developer`。
4. 按任务读取 `.agents/rules/`、`.agents/knowledge/`、角色 references、templates、CLI、scripts 和 memory。

## Claude 平台适配层

以下内容保留在 `.claude/`，因为它们属于 Claude Code 平台：

- `settings.json` / `settings.local.json`：权限、模型和本地覆盖。
- `hooks/`：Claude 生命周期钩子。
- `rules/`：Claude 的路径限定自动注入层；正文通过相对软链指向 `.agents/rules/`。
- `.mcp.json`：MCP 注册（存在时保持不变）。
- `agents/`：旧发现路径兼容壳；角色正文通过软链指向 `.agents/agents/<role>/AGENT.md`。
- `skills/`：Claude 发现兼容层。共享 skill 的编辑位置是 `.agents/skills/`；在 Claude CLI 可用性验证前，不删除现有兼容副本。

## 入口门禁 [G0]

任何文件写入前输出：

```text
## G0: Framework Check
Agent: unity-developer | meta-developer
Action: proceed | load agent first
```

- Unity 功能、Shader、C#、Editor 验证 → `unity-developer`。
- agent / skill / reference / rule / 路径体系维护 → `meta-developer`。
- `Assets/Mine/` 写入必须经过 MCP `g_entry` + `g_knowledge` 与 `write_gated`；删除必须经过 `delete_gated`；**移动/改名必须经过 `move_gated`**（门禁按操作建模：写内容 / 消失 / 换位置，别拿两个拼一个）—— 删除与移动另有 Bash 通道 `python3 .mcp/validation/{delete,move}_gated.py --reason "<原因>" ...`，与 MCP 工具同一道门禁（改一侧须同步另一侧），供 **MCP 工具表按会话冻结**时使用。settings 的 `Edit(/Assets/Mine/**)` deny 会连带命中 Bash —— 实测 `rm`/`mv`/`cp` 被拦（含把文件**读出**到 `/tmp` 这种纯读），写入型 `sed`/`tee`/重定向被拦，只读的 `sed -n` 放行；故原生工具不可用。另：`delete_gated` 只删文件不删目录，空目录靠删掉目录 `.meta` 后 Unity 刷新回收。Codex 侧使用 `python3 .mcp/validation/check_norm.py <file>` 自查。
- 删除、移动旧副本或架构切换前，必须保留回退点并列出精确清单；禁止 `git stash --all`。

## 运行验证

```bash
unityctl status
unityctl bridge start
unityctl editor run
```

编译看日志，运行看日志，视觉结果由人工在 Unity Editor / Game View 观察。Unity 业务代码和 `Assets/Mine/` 不属于本架构迁移范围。

## Agent 路由

| 任务 | 入口 |
|---|---|
| Unity 开发、渲染、脚本和 Editor | `.agents/agents/unity-developer/AGENT.md` |
| agent 体系、skills、rules、路径与适配层 | `.agents/agents/meta-developer/AGENT.md` |
| Codex 链路与双边契约 | `.agents/skills/codex-opencode-go/SKILL.md`、`.agents/skills/codex-bridge/SKILL.md` |

全局路由表仍由 `~/.claude/agents/default.md` 管理，不属于项目共享 SSOT。

## 会话收尾

- 记录改动清单、验证证据和遗留风险。
- 共享经验写入对应 `.agents/agents/<role>/memory/`，并更新索引；不要写回 `.claude/agents/` 旧副本。
- 架构阶段独立提交；旧兼容层只有在 Claude、Codex、MCP 和 Unity 验证完成后才可清理。
