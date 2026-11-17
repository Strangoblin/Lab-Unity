# 仓库产物边界：可复跑源码入库，运行输出不入库（2026-11-17）

## 判定

仓库保存「成品」与「可复跑入口」，不保存「这一次运行」的截图/日志/报告。
`Validation~/` 的身份是**验证源码与基线的归档区**（见 [task-artifacts](../../knowledge/meta/interfaces/task-artifacts.md)），
不是运行产物仓库；新运行输出本应回任务目录 `tmp/sessions/`。

- 保留：`Validation~/**/*.cs`、`*.sh`、`*.py`、`*.json`、`*.md`、`Baselines/`、`Fixtures/` 等可复跑入口与必要基线。
- 不入库：`Validation~/**/*.png`、`*.jpg`、`*.log`、`*.txt`（批量截图与报告）。
- 确需归档的精选证据必须 `git add -f` 显式加入，或放进 `.agents/memory/<域>/evidence/`。
- 既有 tracked 的 12 张 `Validation~` PNG 不受影响（`.gitignore` 只约束未跟踪文件）。

`.gitignore` 同时恢复 `/.mcp/moves.jsonl`：移动审计账本和 `state/` 一样是「这台机器上发生了什么」，
不是仓库内容。此前 b98d234 换 CLI 时把这条规则一起删掉了。

## 清理与同步

- 一次性排除 GrassManager `Validation~` 的 104 张 PNG / 20 份 txt（约 36 MB），只提交两个 `*.cs` 检查器。
- 用户确认图片「随用随丢」后清掉 33 项（垃圾桶 `20261117-152553-2c048e`）：
  - 两份无引用的整目录证据（`evidence/2026-11-05-interaction-delta`、`-water-interaction`）；
  - 仓库根目录误放的 `Validation~/2026-10-30-*`（8 张）；
  - SurfaceDither `Validation~` 的 4 张本轮截图；
  - `diffusegi-ao-cleanup` 的 9 张 `.bmp` 原始截图（同目录已有同名 PNG）；
  - 无任何记录引用的 evidence 图片（diffuse-sequence、diffuse-hiz、grass-wind）。
- 仍保留 64 张被记录正文引用的证据图；要一并删除需同步改写 26 篇记录里的链接。
- 清掉两个历史 stash（fgd-window 清理、`tmp/fix_features.cs` 旧备份）。
- 合并 `origin/unity6` 时，远端把旧的 Sand/Snow 处理器改名为 `Sand~` / `Snow~` 来绕过缺失的
  `InteractionProcessor.cs`。本分支的处理器是更新版本且基类齐全，故保留活动目录、丢弃两份 `~` 归档副本。
- `InteractionSurfacesDemo.unity.meta` 采用远端 GUID（本地从未跟踪过该 meta，场景无外部 GUID 引用）。

## 复查

工作区应干净；`git check-ignore -v` 可验证上述规则。删除留底在 `.mcp/trash/20261117-152553-2c048e/`。
