# 项目专属适配器迁回项目

## 用户裁决与来源

- 2026-10-16 用户裁决：`project-adapters/` 是项目个体内容，共享库不得保留；**项目专属验证器一律归项目功能目录**。本记录撤掉 [生产工具归入 agent 模板](2026-10-16-production-tools-agent-templates.md) 里「适配器归 skill project-adapters」那一半裁决（同一记录的通用模板部分仍有效）。
- 适配器的原始来源见 [2026-10-16 production-tools relocation](evidence/2026-10-16-production-tools-relocation/manifest.json)：它的原项目路径就是 `Assets/Mine/Scripts/ProductionTools/Validation~/`。

## 当前落点

- `.agents/skills/auto-manager/project-adapters/production-tools/`（2 文件）→ `Assets/Mine/Scripts/ProductionTools/Validation~/`，原项目路径的精确逆迁移。
- `.agents/skills/unity-feature-reuse/project-adapters/hash/`（7 文件）→ `Assets/Mine/Special/HLSL/Validation~/hash/`，Hash 函数库所在项目文件夹。
- 两个 `project-adapters/` 目录已空并移除；全仓扫描确认共享库再无项目专属适配器。
- 通用技能不再硬编码 Lab 路径：`.mcp/project-resources.json` 新增 `validation.hash.guide` / `validation.hash.adapter` / `validation.production-tools.adapter`；[unity-feature-reuse SKILL](../../skills/unity-feature-reuse/SKILL.md) 改按资源 ID 取用；[task-artifacts](../../knowledge/meta/interfaces/task-artifacts.md) 长期归属表撤掉「项目专属遗留验证器可隔离在 project-adapters/」。
- `Validation~` 由 Unity 忽略（无 `.meta`、不进编译），与其它功能 `Validation~/` 同形；运行入口仍是 `unityctl script execute` / python。

## 精确迁移与回退

- 9 文件 / 365,525 字节，迁移前全部 tracked-clean；源、目标、前后 SHA-256 与 Git blob 见[清单](evidence/2026-10-16-project-adapters-back-to-project/manifest.json)。
- 通道：`.agents` 侧用平台文件工具移到 `tmp/sessions/2026-10-16-adapters-relocation-09dfbe16/staging/`，再经 `move_gated` 两批落位；未放宽 `ALLOWED_PREFIXES`、policy 或工作区边界。移动审计在 `.mcp/moves.jsonl`。
- 字节核验：迁移后 9/9 与迁移前逐字节相同；其中 2 个 README 在迁移前先改自指路径（清单标 `edited_before_move`），内容指纹相对 2026-10-16 原始版本已变。不可变基线 `fixtures/*.json` 未改写。
- 回退：`git revert` 本提交即恢复旧布局；历史 dated memory / evidence 原字节未动，`.codex` 历史迁移链另补两条新映射。

## 顺带清理

- 根目录 4 个 `mono_crash*.json`（用户裁决为废文件、项目无引用）已删。删前存为 Git 对象并建 checkpoint `refs/codex/checkpoints/2026-10-16-root-mono-crash-before-cleanup`（`0dbf1bb13d9a`，4/4 可还原），全程未接触索引。

## 验证

- `.codex/tests/06_architecture/verify.py --strict`：0 findings，PASS，与上一轮基线一致。
- `.mcp/tests/run_isolated.py`：4/4 PASS，exit 0，真实 `.mcp` state / 审计指纹未变。
- 路径残留：活动范围除 `HISTORICAL_RELOCATIONS` 新条目外无旧路径命中；旧路径只存在于历史 dated memory / evidence。

## 边界

- 本次只动用户点名的两处 `project-adapters` 与根目录 mono 转储；`hash.*` 被验证消费者绑定未动。
- **未运行 Unity Editor**：`Validation~` 被 Unity 忽略，迁移不触发导入或编译，故不需要 asset refresh；但也没有 Editor 侧证据。
- **验证器未复跑**：本机缺 clang / glslangValidator / NumPy，hash 三个脚本只做了位置无关性与字节核验；42 项窗口回归需连接 Editor 才能复跑。
