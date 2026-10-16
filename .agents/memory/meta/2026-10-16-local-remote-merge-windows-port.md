---
name: local-remote-merge-windows-port
description: 合并远程 MCP v3 与渲染更新，并修掉随之引入的 Windows 删除通道与隔离测试清理缺陷。
date: 2026-10-16
---

# 本地工作合并远程（含 Windows 删除通道修复）

## 合并

- 本地工作区（TerrainManager 拆三层、CityScene、`validate_path` 分隔符回归）先分
  4 个提交落在 `1ebd5d1` 之上：`d5150fc`（路径归一）→ `1924557`（memory）
  → `7f62df9`（terrain 分层）→ `e6ea8ec`（场景），合并提交 `768f6d5`。
- 远程侧 22 个提交（MCP v3 判定核 + policy、GizmoRender / HDRMapBaker / Weather /
  MaskBlur / MaskRBR / SSGI 空间滤波、生产工具 skill 族等）一次性进入。冲突处理：
  - `.mcp/validation/project_paths.py`：自动合并，本地归一化落在远程重写的
    `validate_path` 上。
  - `.mcp/tests/test_recipes.py`：`write_gated` 用例前补 `gate_set_workspace`（远程 policy 要求
    `Assets/Mine` 写入先声明工作目录）。
  - `.mcp/tests/test_knowledge_paths.py`：取远程版，正斜杠子串断言改为 Path 相等
    （原断言在 Windows 恒失败）。
  - memory：拆分后本地记录迁到 `.agents/memory/meta/`，索引合并。
  - 场景：`SampleScene.unity` → `RenderScene.unity` 被 git 识别为重命名（两文件字节
    相同，已核），远程对旧构建场景的改动随之落到新名。

## Windows 移植缺陷（远程引入，本地修复于 `201be62`）

- `validation/deletion.py` 直接用 `os.O_NOFOLLOW` / `os.O_DIRECTORY` / `dir_fd`，Windows 上
  `delete_gated` 未判定即抛 `AttributeError`。改为能力探测 + 退化：无 `dir_fd` 时用
  `_lineage` 逐级比对 inode 复验祖先，`_stat/_unlink/_rmdir/_close` 分流，保留不跟随软链与
  替换竞态检查。
- `tests/run_isolated.py` 的 `shutil.rmtree` 在 Windows 因 git 对象只读而失败（WinError 5），
  导致全套通过仍以退出码 1 收尾；改为先摘只读位再删。
- `test_directory_deletion.py` 的 FIFO 用例需要 `os.mkfifo`，无此平台跳过。
- `_under` 归一在两侧都做（`check_workspace_roots` 传绝对路径，前缀却是
  `PROJECT_ROOT`）。

## 验证与遗留

- 已验证：`.mcp/tests/run_isolated.py` 四套全绿（33 / 6 / 23（1 skip）/ 5 项，退出码 0），
  真实 `state.json` 与审计指纹未变；`.codex/tests/06_architecture/verify.py --strict`
  出 0 项。
- 未验证：Unity Editor 未运行 —— TerrainManager 分层与 CityScene 未编译 / 未运行，合并带入的
  22 个提交涉及资源搬迁，需重新导入确认。
- 回退点：`tag pre-merge-worktree-20261016`（`1ebd5d1`）与 `backup/local-before-merge-20261016`；
  被删的 9 个 `.backup` 实验档案仍可从远程 `75fb0f2` 取回。
- 未处理：仓库根 `.backup/` 仍是未跟踪且未被 `.gitignore` 覆盖。
