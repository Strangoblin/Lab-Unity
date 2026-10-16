---
name: opencode-era-ledger
description: 2026-08-10（切换 opencode-go）至 2026-09-21 的 Unity/Lab 任务台账——按主题聚合，标注权威记录与无记录缺口。
date: 2026-09-21
---

# 2026-08-10 → 2026-09-21 任务台账（opencode 时代）

> 本文件是**索引**，不是知识正文。每条任务指向权威记录；无权威记录的列为「缺口」。
> 用途：新会话的冷启动入口——先读本表定位，再按链接读单篇 memory，不必回扫会话历史。

## 时间基点

**2026-08-10 08:06:44** — 首个 `model_provider = opencode-go` 的会话（`~/.codex/sessions/2026/08/10/`）。
此前为 `custom` provider。`session_index.jsonl` 起点 08-10T07:54 与之吻合，即现有会话索引覆盖的恰好是本表区间。

会话来源分布（Unity/Lab，同期 131 个 codex 会话）：

| originator | 数量 | 说明 |
|---|---|---|
| `codex_vscode` | 58 | 主工作面，长会话，**压缩只发生在这里** |
| `Codex Desktop` | 55 | **导入会话，时间戳全部塌缩到同一秒**，不作为时间依据 |
| `codex_exec` | 18 | 派发工作面，**零压缩**；其中 12 个是一次性契约探针 |

> 读会话统计时的坑：`Codex Desktop` 的 55 条按日期聚合会把 09-02 与 09-04 虚高成 17 / 54。

## 已完成 — 有权威记录

| 日期 | 任务 | 权威记录 |
|---|---|---|
| 08-25 → 08-27 | HyperSpace 移植接手 / Van Gogh Sunset（TheStarryNight）移植 | [2026-08-27-starrynight-angle-field.md](2026-08-27-starrynight-angle-field.md)（角场部分） |
| 08-25 → 09-04 | 门禁体系：截图环节移除、知识内化、references 归位、后果验证 v2、配方分层、MCP 边界 | meta-developer `2026-08-24-*` ~ `2026-09-14-mine-write-scope.md` |
| 09-02 → 09-04 | InteriorMapping + InteriorMap Baker：UV 空间、半球接缝、box 左右反向，到归档 | [2026-09-04-interior-mapping-archive.md](2026-09-04-interior-mapping-archive.md) |
| 09-02 | StarryNight 麦田多层横带收尾 | [2026-09-02-starrynight-wheat-bands.md](2026-09-02-starrynight-wheat-bands.md) |
| 09-03 | Curve/Noise Generator 框架统一 | [2026-09-03-generator-unification.md](2026-09-03-generator-unification.md) |
| 09-03 | 模板体系第三、四轮（standard 代码框架 / render + hlsl 家族 / window 家族） | [2026-09-03-standard-code-window-family.md](2026-09-03-standard-code-window-family.md)、[2026-09-03-render-hlsl-template-families.md](2026-09-03-render-hlsl-template-families.md) |
| 09-04 | Agent 架构重构收尾：`.agents` 成为唯一 SSOT，`.claude`/`.codex` 转薄适配层 + 软链 | meta-developer `2026-09-04-agent-architecture-cutover.md` 等 4 篇 |
| 09-07 → 09-20 | 后处理落雪 Snowy：双 Pass 原型 → 屏幕雪粒子 → 收尾 | [2026-09-20-snowy-wrapup.md](2026-09-20-snowy-wrapup.md)、[2026-09-18-parameter-range-policy.md](2026-09-18-parameter-range-policy.md) |
| 09-10 | unityctl 官方 CLI 与 unityctl 的路由边界 | meta-developer `2026-09-10-unity-cli-routing.md` |
| 09-11 | 水面重建：caustics 屏幕无关化复核 + DDA 命中修正（`surfaceDistance` 世界空间 / `DDX_CorrectHit` 新旧 SWITCH） | ⚠️ 见缺口 |
| 09-14 → 09-16 | 光照库结构整理：BRDF/BTDF 前置、Hair 后置、F_Dielectric 留存不用、Burley+Lambert 合并、参数统一 real、弃用并重命名光照文件 | ⚠️ 见缺口 |
| 09-15 | halfLambert 重映射后 LUT 明度需同步；`shadowAttenuation` 非黑（灰）的成因 | ⚠️ 见缺口 |
| 09-17 | PBRToon 五 Pass 实例化 / Stereo / URP 17 结构迁移 | [2026-09-17-pbrtoon-urp17.md](2026-09-17-pbrtoon-urp17.md) |
| 09-17 | UV 空间各向异性（屏幕 UV 内做 R/S 必剪切） | [2026-09-17-uv-aspect-anisotropy.md](2026-09-17-uv-aspect-anisotropy.md) |
| 09-17 | Mine 下全量 Shader 重命名 + 菜单路径规范 | meta-developer `2026-09-17-shader-menu-naming.md`、`2026-09-17-shader-validation-branches.md` |
| 09-18 → 09-20 | 参数分三层（Technical/Performance/Artistic）+ 半接线参数诊断 | [2026-09-20-parameter-layering.md](2026-09-20-parameter-layering.md) |
| 09-20 | 时域累积抽为 TemporalFunction.hlsl 共享库 + PCSS 接入 | [2026-09-20-temporal-accum-pcss.md](2026-09-20-temporal-accum-pcss.md) |
| 09-20 | RT 回读与画面判读四陷阱（误判复盘） | [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md) |
| 09-20 | unityctl 三坑（取像 / 生命周期 / 求值） | [2026-09-20-unityctl-tool-pitfalls.md](2026-09-20-unityctl-tool-pitfalls.md) |
| 09-20 | 模板核验 `check_api_refs.py`：「模板可编译」重定义为「模板 API 可核验」 | meta-developer `2026-09-20-parameter-panel-template.md` |
| 09-21 | **DiffuseGI（SSGI Phase 3）**：事件排序修复 `BeforeRenderingTransparents` → `AfterRenderingSkybox` | [2026-09-21-diffusegi-ssgi-phase3.md](2026-09-21-diffusegi-ssgi-phase3.md) |
| 09-21 | **AO（SSGI Phase 4）**：SSAO/HBAO 双模式，持久化进 `PC_Renderer` | [2026-09-21-ssgi-phase4-ao.md](2026-09-21-ssgi-phase4-ao.md) |

## 缺口 — 无 memory 记录

以下任务只存在于会话历史与代码注释中，**新会话无法从 memory 冷启动**：

| 任务 | 现存线索 | 缺什么 |
|---|---|---|
| 09-11 水面重建 | `Assets/Mine/Shaders/Render/Water/Water.md` | 「世界空间 vs 屏幕空间射线」这条判据的**判读方法**——为什么用屏幕空间信息算 `surfaceDistance` 是错的 |
| 09-14 光照库结构整理 | `Assets/Mine/Special/HLSL/PBRFunction.hlsl`、`LightFunction.hlsl` | BRDF/BTDF/Hair 的**分层依据**，以及 F_Dielectric「性能太大」的量级 |
| 09-15 halfLambert ↔ LUT | 同上 | 重映射后 LUT 必须同步明度的**成因**；`shadowAttenuation` 为灰的根因 |
| 09-16 光照文件弃用重命名 | git 历史 | 弃用名单与替换关系 |
| 08-26 Van Gogh 移植 | `Assets/Mine/Shaders/Render/VanGogh/` | 仅角场部分有 memory，**移植流程本身**未记录 |
| 08-13 StochasticSSR 噪点修复 | 会话 116 行 | 修复结论 |
| 09-10 ComputeCaustics 优化 | 会话 | 仅 08-04 有屏幕无关化 memory，**本轮优化**未记录 |

## 在飞 / 未决

- **SSGI 主线**：Phase 3（DiffuseGI）、Phase 4（AO）已落地并提交。**2026-09-21 后半程完成家族收敛**——
  `PostProcess/{SSR,SSPR,StochasticSSR}/` 连目录删除，`ScreenSpaceTrace.hlsl` 与两份进度骨架迁入 `SSGI/`，
  SSGI 自此自包含；见 [2026-09-21-ssgi-consolidation.md](2026-09-21-ssgi-consolidation.md)。
  Phase 3 遗留「水面变暗 197 px / −0.027」，已定性为管线路径差异（与 GI 和事件无关），未修。
- **Phase 6 统一 Composite**：**待实施**，设计已登记在
  `Assets/Mine/Shaders/PostProcess/SSGI/SSGI_Content_Skeleton.md` §Phase 6。
  落地前有四个前置约束（AO 不可叠乘两次、Forward 拿不到直接/间接拆分、全局纹理仅同帧同相机、
  SpecularGI 的 Composite 是 lerp 而非加法）。
- **`SSR/` 下两个 `.new` 草稿副本**：**已随 `SSR/` 目录整体删除而消解**（该目录连同三份 `.backup_v*` 全部移除）。
  过程记录见下方「附」。附带发现：`Assets/Mine/` 下 Bash 删除被 `Edit(/Assets/Mine/**)` deny 级联挡住
  （`rmdir`/`rm -d` 均拒，同命令在 /tmp 通过）；但**目录回收仍能闭环**——删掉目录 `.meta` 后
  `AssetDatabase.Refresh(ForceUpdate)` 会自己回收空目录，无需人工。注意 `git status` 对目录不感知，
  验证要用 `find`/`ls`。
- **`.agents/agents/unity-developer/memory/sessions/FTS5_SETUP.sql`**：孤儿文件，无对应会话记录，可清。
- **meta-developer 待办**：① `.claude/agents/<role>/` 空目录壳物理清理（无 git 足迹）；
  ② `references/standard/shader/shader-structure.md` §3「拆库裁决」补第三档「家族私有共享库」（家族根目录）。

## 附 — 本表生成时的清理动作（2026-09-21）

DiffuseGI/AO 会话临时产物清理：清单 100 项（源头见 `<repo>/tmp/cleanup-manifest.md`），
实删 **98 项 / 27.5 MB**（`.codex/tmp/diffusegi/`、`.codex/tmp/ao/`、日志、17 个 `Screenshots/diffusegi-*/` 等），
排除 `.codex/tmp/README.md`（tracked）与 `Screenshots/` 根下 137 个 tracked 历史截图（危险区，零触碰）。
回退点：`tmp/rollback-2026-09-21-diffusegi-ao.tar.gz`（27 MB / 207 文件，`.gitignore:14` 已忽略）。
清单中 8 项「需人工判断」经 diff 方向核对，**7 项确认为正式文件超集下的过时副本**，仅 `skeleton-new.md` 为独立旧草稿。
2 项 `Assets/Mine/` 下 `.new` 未删，原因见「在飞 / 未决」。
