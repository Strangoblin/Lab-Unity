---
name: gate-directory-deletion
description: delete_gated 补齐目录删除（空目录默认 / recursive 展开 / 软链与作用域根保护）
date: 2026-10-10
---

# 删除通道补齐目录

存量整理收尾时遇到一个空目录 `Assets/Mine/Shaders/PostProcess/SSC/.backup_v1_pre_unity6`：文件已归档/删除，目录删不掉。先确认这不是 meta 通道的问题 —— [meta-architecture.md](../../../rules/meta-architecture.md) 的边界仍然成立：`.agents/**`、`.mcp/**`、`.claude/**` 体系维护直接编辑、不走 unity-gate；`Assets/Mine/**` 属 Unity 业务门禁。这个路径在 `Assets/Mine` 下，走的是 Unity 通道，缺口在执行核：`judgement.delete` 只调 `os.remove()`。

## 改动

判定核一层做（`.claude`/`.codex` 与 MCP 工具都是薄壳，改一处即两侧同源）：

- `validation/deletion.py`：`plan()` 展开精确清单（目录递归、父覆盖子去重）、`execute()` 先整批复核再 `unlink`/`rmdir`、`_fingerprint`/`_lineage`/`_parent_fd` 用 inode + 内容 hash + `O_NOFOLLOW` 防竞态；`STATE_BY_ERROR` 给出对外 state 名。
- `validation/judgement.py`：`delete(..., recursive=False)` 先结构预检（展开后的**每个子项**都进不可恢复性分类，`recursive` 只放开目录形状、不绕过 dirty/GUID），再执行并落审计（新增 `requested_paths` / `expanded_paths` / `recursive`）。
- 传输层：`server.py` 的 `delete_gated(recursive, dry_run)`、`gate_cli.py delete --recursive`、兼容入口 `delete_gated.py`；`run_isolated.py` 注册第三个套件并把测试函数计数改成 `ast.walk`（`unittest.TestCase` 的方法不在 module body 里）。

语义：空目录默认可删；非空须显式 `recursive`；软链（自身或祖先）与作用域根（`Assets/Mine` / `tmp` / `roslyn`）拒绝；非普通文件拒绝；预检后新增子项 / 同尺寸同 mtime 的内容替换 / 软链替换一律零改动。

## 能力契约（新检查 id `deletion.structure`）

- 假设：目录尺寸本身不是风险，**内容**才是；所以形状判定与不可恢复性判定分开记账。
- 证据：`.mcp/tests/test_directory_deletion.py` 23 例（空目录/非空拒绝/子树展开/dirty 与 staged 子项连坐/git 失败 fail-closed/批内 GUID 放行/批外 GUID 拦截/软链不跟随/父子去重/竞态四种/三通道一致）。
- 成本：一次 `lstat` 链 + 每文件一次内容 hash；比 `rmtree` 贵，换的是「预检通过前不删任何东西」。
- 退役判据：`retirable: false` —— 与 `deletion.recoverability` 同属用户下达的边界。级别未写入 `policy.json`：`level_of` 未列条目取固有 `block`，不引入策略数据变更。

## 验证与收尾

三套隔离契约 23+30+5 全绿、runner 回归 9/9、生产 state/审计指纹未变；架构 strict 与 `git diff --check` 通过；`gate_cli.py report` 无 review 级待办（`norm.region-added` / `placement.root.new` 的 retire-or-demote 是既有提案，策略改动须人工确认，未动）。顺带修 `grep` 的 GUID 查询：改用 `-rlF` 逐行读取 —— macOS/BSD grep 的 `-Z` 不产 NUL 分隔，`split("\0")` 会把所有命中并成一条。

SSC 的空备份目录已由 Unity 门禁删除（`channel=cli`，`recipe=Production`），前一轮执行报告只追加 `follow_up` 字段，不回溯改写当时的判定。P1-P3：能力只落 `deletion.py`/`judgement.py` 两处实现，文档改 `.mcp/README.md` 一处权威源 + 角色速查与平台入口引用，不新建文件体系。
