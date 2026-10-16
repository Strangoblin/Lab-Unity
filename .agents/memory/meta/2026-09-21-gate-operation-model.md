---
name: gate-operation-model
description: 门禁按操作建模（写/删/移）—— move_gated 补复合面、落点校验从休眠接上线，及「限制功能使用 vs 规范功能使用」的审计结论
date: 2026-09-21
---

# 2026-09-21 — 门禁从「按文件」改为「按操作」建模

承接 [2026-09-21-delete-gated.md](2026-09-21-delete-gated.md)。那轮补了删除，这轮把
「操作缺口」当作一类问题系统过了一遍，并补上两处。

## 审计结论：门禁按「文件」建模，而开发需要「资产生命周期」

`write_gated` 管「文件内容」、`delete_gated` 管「文件消失」。凡不是这两者的操作都掉进缝里，
而缝隙的填补方式是**复合调用**——复合调用又会撞上按原子操作写的判定。

`delete_gated` 对 `tracked-dirty` 硬拦**是对的**：单独删一个脏文件，内容只剩历史版本。
但「移动」= 写新 + 删旧，内容完整保全在新位置，同一判定变成**误拦**。
这不是 bug，是建模粒度问题：**门禁不知道自己的对偶操作**。

## 决策：加 `move_gated`，而不是给 delete 开豁免

给 `delete_gated` 加 `exempt_when_paired` 参数也能跑，但那把「移动」这个意图塞进了删除的语义里。
按操作建模更干净：**`write` 管内容 / `delete` 管消失 / `move` 管换位置**。

- `validation/moving.py::plan()` 预检**零改动**，任一被拦则整批不执行
- 判定：`SAME_PATH` / `MISSING_SOURCE` / `TARGET_EXISTS` / `DUPLICATE_SOURCE` / `DUPLICATE_TARGET`
  / `CHAINED_MOVE` / `META_TARGET_EXISTS` / `GUID_TAKEN`
- **批次级检查排在逐文件检查之前**：链式歧义是「这一批的形状」问题。原先排在存在性之后，
  「B 是 A 的目标又是 C 的源」会先报成 `MISSING_SOURCE`——而 B 不存在恰恰是链条造成的，
  那个诊断会把人引向错误的方向。（测试第一次跑就暴露了这一点。）
- **链式判定必须对称**：`dst in sources or src in targets`。只查一个方向时，链条的另一半
  会掉到存在性检查去，报出误导性的 `MISSING_SOURCE`。
- `.meta` **成对搬运**保 GUID；搬前查该 GUID 是否已被别的 `.meta` **声明**
  （`find_guid_owners` 与 `deletion.guid_referenced_elsewhere` 不同：那个问「谁引用它」，
  这个问「谁也声称拥有它」——同 GUID 两份 = Unity 报错）
- **不复跑内容规范**：移动不引入新内容。对纯搬位置重跑 norms，会把「历史遗留不合规但
  本来就存在」的文件拦下——那是限制而非规范
- 执行 **write-all → delete-all**：预检不过零改动；写阶段失败回滚新文件（源没动）；
  删阶段失败保留旧副本（内容没丢，安全方向）
- 审计落盘 `.mcp/moves.jsonl`：新位置有产物、旧位置没产物，**丢失的正是「哪个变成了哪个」**

测试的核心断言是**分歧点**：同一份 `tracked-dirty` 输入，`deletion.evaluate` 拦、
`moving.plan` 放。两条断言成对写，缺一条这个测试就退化成「移动能跑」。

## 落点校验：从休眠接上线

`project_paths.py` 早有 `VALID_FILE_TYPES` / `CATEGORY_BASE` / `resolve_target_dir`，
但唯一消费者 `gates/g_file.py` **已退役** → 整个放置规则**休眠**，`write_gated` 只做前缀检查。

接上线前先**扩表**——但方向不是「把白名单列全」：

- **受管扩展名只收自撰代码与文档**（`.shader`/`.hlsl`/`.cs`/`.compute`/`.md`）。
  `.mat`/`.prefab`/`.png`/`.asset`/`.meta` 等由 Unity 导入或工具产出，**不进白名单**——
  列全导入类型，白名单会随 Unity 每加一种资产类型而失效，最终变成误拦
- **顶层根只到一层**（`Effects`/`Scripts`/`Shaders`/`Special`）。实测现实布局：
  `Scripts/` 下既有 `Editor/`/`Shaders/`/`Noises/` 嵌套子目录，也有直接躺在根下的
  `TestAuto.cs`；`.shader` 还出现在 `Scripts/Picker/`。任何按深度或「必须进效果子目录」
  的规则，第一次运行就会误拦
- 强度与 `norms.py` 的「新增行 diff」同源：**新文件 error**（全新决定）、
  **已有文件 warning**（只是就地编辑，硬拦会堵死修正入口）
- **验证方式**：扫全部 **202 个受管文件**，0 误报。规则是照现实写的，这个数字就是证据

顺带把 `category` 注解从自由文本升为可校验（与路径前缀比对，不符只 warning）。

## 一处保持原样的判断

`deletion` / `moving` 的**审计不对称是有意的**：`writes` 只在会话内存，因为写入的产物
本身就是持久记录；`deletes` / `moves` 落盘，因为删除不留产物、移动丢的是配对关系。
本轮未改。

## 已知边界（未做）

- **`cp` 读 `Assets/Mine` 写 `/tmp` 被 deny 级联拦住**（纯读，目标是合法写入区）。
  这是 [2026-09-21-delete-gated.md](2026-09-21-delete-gated.md) §遗留 已裁决过的取舍——
  deny 是更强的写入闸，代价就是覆盖了 Bash 的移动/删除/复制。**不是新问题，改法也不在权限层**
  （放开 deny 会丢掉 `sed`/`tee`/重定向的覆盖）。本轮只把它**如实写进文档**：
  `CLAUDE.md` 与 `mcp-gate-usage.md` 原来笼统说「连带命中 `sed`」，实测**只读 `sed -n` 是放行的**
  ——已改成精确表述。
- **`g_file` 退役遗留**：`gates/g_file.py` + `resolve_target_dir` 现在确定不可达
  （调用即 `GATE_NOT_IN_RECIPE`，`check()` 永不执行）。保留是因为测试钉住了退役集合，
  单独清理属另一轮。
- `move_gated` **已补 Bash CLI 通道** `.mcp/validation/move_gated.py`（与 MCP 共用
  `moving.plan` + `moving.execute`，判定与执行都同源）。理由与删除 CLI 相同：
  **MCP 工具表按会话冻结**（见 [2026-09-21-delete-gated.md](2026-09-21-delete-gated.md) §两个通道），
  而写入与移动同样会撞上它——`check_norm.py` 是检查器不是写入器，挡不住。
  实测再次确认该冻结：本会话 `move_gated` 已注册（`server.call_tool` 走通）但 MCP 客户端调不到，
  只能走 CLI。
- **写入仍无 CLI 通道**（`write_gated.py` 未做）。这是三项操作里唯一还是单通道的。
  设计上它比移动更麻烦——`content` 走命令行参数不现实，得用 `--from <本地文件>` 的形式，
  顺带能解决「Codex 产出经门禁合入」的落地方式。下一步。

## 同步清单（链路保障）

代码：`.mcp/server.py`（抽出 `_atomic_write` 供写/移共用）、`.mcp/gate_center.py`（`SessionState.moves`）、
`.mcp/validation/moving.py`（新）、`.mcp/validation/project_paths.py`（执行层 + 区分咨询层）、
`.mcp/tests/test_recipes.py`（+3 个用例）、`.gitignore`（`.mcp/moves.jsonl`）。

文档：`.mcp/README.md`、`.agents/agents/unity-developer/references/mcp-gate-usage.md`、
`.agents/skills/auto-manager/SKILL.md` + `modes/{production,experiment}.md`、
`.agents/interfaces/project-structure.md`、`.claude/CLAUDE.md`、`.codex/AGENTS.md`、`.codex/INTERFACE.md`。
dated memory 按规则不改。

## 验证

- `.mcp/tests/test_recipes.py` `All tests passed ✓`（含新增 `test_moving_plan` /
  `test_placement_check` / `test_write_gated_placement_wired`）；`test_knowledge_paths.py` PASS
- `.codex/tests/06_architecture/verify.py` PASS：`agents_broken_markdown_links: 0`、
  `skill_drift_files: 0`、`mcp_hardcoded_claude_files: 0`、`phase2_contract_errors: 0`
- 落点规则扫 202 个受管文件 0 误报
- **端到端实测（真实 `Assets/Mine`，经 CLI 通道）**：`write_gated` 造 `.hlsl` 探针 →
  `move_gated.py --dry-run` 判定可移且零改动 → 实移同目录改名 → 核对：
  源与源 `.meta` 均消失、正文 581 B 保真、**GUID `aeea9097…` 经 Unity
  `AssetDatabase.AssetPathToGUID` 核对与搬前完全一致**、审计落盘 `.mcp/moves.jsonl`
  （含 `channel: cli` / `argv` / `removed`）。探针经 `delete_gated` 清除，工作区无残留。
  **GUID 一致性是 `.meta` 成对搬运设计的最强验证**——Unity 资产的身份就是它。
- 顺带实测到一条状态语义：测试套件末尾的 `gate_reset` 会清掉 `.mcp/state.json`，
  而 MCP 服务端的内存态仍在（`gate_status` 照常显示 Production/passed），
  此时 CLI 报 `NO_RECIPE`。这是「CLI 只消费已通过的链」的正常表现，不是 bug——
  但排查时会迷惑，故记在此。

## 偏离记录

`mcp-gate-usage.md` 收敛后 150 → **109 行**，仍超 P1 的 80 行上限。剩余的
工具表 / DENIED 码表 / 流程均为非重复的操作内容，再砍就删价值，故保留并在此记账。
