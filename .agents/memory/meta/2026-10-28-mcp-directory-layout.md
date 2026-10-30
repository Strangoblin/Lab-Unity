---
name: mcp-v5-directory-layout
description: .mcp 根目录整理成 bin/lib/config/state 四层，根只留 README；删掉 43MB venv 与打包三件套；hook 传输层搬迁会把自己锁在门外（需垫片）。
date: 2026-10-28
---

# `.mcp` 目录整理：根目录只留 README

用户要求「mcp 根目录塞了太多 py/json，整理一下」，追加「尽量根目录只留下 readme」。

```
.mcp/
├── README.md          ← 根级唯一文件
├── bin/   cli.py, hooks.sh            入口：只这两个被直接调用
├── lib/   hooks/zones/judgement/session/close/profiles/telemetry + validation/
├── config/ policy.json, zones.json, project-resources.json
├── state/ session.json + 五本 jsonl（gitignored）
├── tests/
└── trash/
```

## 一条约束决定了整个布局

以前每个模块靠 `__file__` 的**邻居**找自己的配置（`zones.json` 在 `zones.py` 旁边，
账本在 `session.py` 旁边）。这类锚点在目录一动时全部失效，而且失效方式很隐蔽：
`zones.py` 的 `PROJECT_ROOT = dirname(__file__)/..` 从 `.mcp/` 搬到 `.mcp/lib/` 后指到了
`.mcp` 本身，于是 `.mcp/lib/hooks.py` 被判成「仓库外散文件」—— 判定核认为自己在管仓库，
实际管的是 `.mcp`。

改成**单一锚点**：`lib/` 里一律 `_MCP_ROOT = dirname(dirname(__file__))`，
`PROJECT_ROOT = dirname(_MCP_ROOT)`，配置走 `_MCP_ROOT/config`，状态走 `_MCP_ROOT/state`。
`validation/` 下沉一层后同类锚点集体多退一级（`_file__` → 三级）。

## 最大的坑：hook 传输层不能先搬

搬 `hooks.sh` 的瞬间，两个平台的注册（`.claude/settings.json`、`.codex/hooks.json`）
还指着旧路径，PreToolUse 起不来 → `onFailure: block` → **所有 Write/Edit/Bash 全被拦**，
包括「改注册指向新路径」那一次。注册文件本身受 PreToolUse 管辖，这是真正的死锁。

**顺序必须是**：先放一个转发的垫片在原路径 → 改注册 → 验证新路径独立可用 → 删垫片。
我当时直接搬了，最后只能让用户在终端手敲一条 `printf > hooks.sh` 恢复。

## 其它决定

- **删 `.venv`（43MB）+ `pyproject.toml` + `uv.lock` + `.python-version`**：v4 拆掉 MCP server 后
  `dependencies = []` 且无人 `import mcp`。我先用系统 python3（3.9.6）实跑四套契约全绿才删。
  `run_isolated.py` 的默认解释器从 `.mcp/.venv/bin/python` 改成 `sys.executable` ——
  留一个指向不存在的路径的默认值，报错会指向错误的方向。
- **`.gitignore` 从逐文件改成 `/.mcp/state/` + `/.mcp/trash/`**：以前 `session.json`/`writes.jsonl`/
  `reads.jsonl` 从来没被忽略过（只忽略了三本），整理时才发现。
- **`verify.py` 排除 `.mcp/trash/`**：垃圾桶是「可 restore 的删除留底」，不是活架构源。不排除时
  每删一次就有一批机器绝对路径与被删模块名进入报告，`absolute_path_hits` 从 38 涨到 83，
  全是噪音。修完后是 **0**（那 38 条本来也都是垃圾桶里的）。

## 踩到的其它坑

- 批量 sed 改路径时误伤了 `.agents/memory/**/evidence/` 与 dated 记录 —— 那些是历史快照，
  记录的是**当时**的布局。已 `git checkout` 全部回退。活文档才改。
- 改锚点期间 hook 在 `.mcp/lib/` 写了两个残余账本（含一次真实的 `ZONE_DENIED`）。
  按「账本是唯一痕迹」合并进 `state/` 而不是删掉。

## 验证

- `python3 .mcp/tests/run_isolated.py` → 4/4 PASS，生产账本指纹未变。
- `python3 .mcp/tests/test_isolated_runner.py` → 9/9 OK。
- `python3 .codex/tests/06_architecture/verify.py` → PASS，`absolute_path_hits: 0`。
- 真实链路：`cli.py open` → `state/session.json`；`hooks.sh PostToolUse` → `state/writes.jsonl`；
  `project_resources.py path library.hlsl.shared` → 正确解析。

## 相关

- v5 判定改造：[2026-10-28-mcp-v5-git-protected-writes.md](2026-10-28-mcp-v5-git-protected-writes.md)
- v4 业务式改造：[2026-10-28-mcp-v4-business-management.md](2026-10-28-mcp-v4-business-management.md)
