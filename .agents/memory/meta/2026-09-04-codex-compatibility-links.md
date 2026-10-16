# Codex 共享内容兼容软链 — 2026-09-04

## 变更

- 在 `.codex/` 增加指向 `.agents/` 的相对软链：
  - `.codex/agents/unity-developer`
  - `.codex/agents/meta-developer`
  - `.codex/rules`
  - `.codex/skills`
  - `.codex/knowledge`
  - `.codex/interfaces`
- 更新 `.codex/AGENTS.md` 与 `.codex/INTERFACE.md`，明确这些路径是只读兼容别名，不是第二份权威源。
- 更新 Codex 架构白名单与 Phase 06 契约测试，要求软链存在、目标为仓库内相对 `.agents/` 路径且不可断链。

## 原因

Claude 适配层已经使用相对软链引用共享内容，而 Codex 适配层只有直接路径说明，没有对应的 `.codex/` 兼容入口。补齐后，两侧均可从平台目录解析到同一 `.agents/` 内容，同时保留 Codex 专属配置、hooks、tests、tmp 与运行时角色适配。

## 设计原则

- P1：平台目录只做索引和适配，不复制共享正文。
- P2：`.agents/` 继续是唯一可编辑权威源，软链目标不在 `.codex/` 下维护。
- P3：通过架构契约测试固定相对路径与目标一致性，避免适配层漂移。

## 验证

- `.codex/tests/06_architecture/run.sh`：通过。
- `python3 .codex/tests/06_architecture/verify.py --strict`：通过。
