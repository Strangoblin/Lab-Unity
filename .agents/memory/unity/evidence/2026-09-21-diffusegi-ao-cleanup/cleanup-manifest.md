# DiffuseGI/AO 会话临时产物 — 删除清单（待人工确认，本任务未执行任何删除）

生成：2026-09-21 ｜ 角色：unity-developer ｜ 模式：只读盘点 ｜ 工具调用：≤40

> **落盘位置说明**：任务书指定 `.codex/tmp/cleanup-manifest.md`，但本会话沙箱把 `.codex/` 整棵子树设为只读
> （写入返回 `EPERM`，`.codex/` 与 `.codex/tmp/` 均无法创建文件），门禁写入通道亦不可用。
> 因此本清单落在两个可写且不入库的位置：本文件（`<repo>/tmp/cleanup-manifest.md`，`.gitignore:14 /tmp/`）
> 与 `/tmp/cleanup-manifest.md`。人工确认后可直接移动到 `.codex/tmp/`。

**盘点范围**（严格等于任务书 6 组路径）：`.codex/tmp/diffusegi/`、`.codex/tmp/ao/`、`.codex/tmp/DiffuseGI*`、
`.codex/tmp/diffusegi-editor*.log`、`Screenshots/diffusegi-*/`、`Assets/Mine/Shaders/PostProcess/SSR/DiffuseGIFeature.cs.new` 与 `.new.meta`。

**排除项**（已确认不在本表内）：`.codex/tmp/README.md`（tracked）、`Screenshots/` 根下 137 个 tracked 历史截图、
`Assets/Mine/` 其余文件、`.codex/tests/`、`Library/`、其他会话（snowy / plant / shader-rename）产物。

**口径**：文件行大小为实际字节；目录行大小为 `du -sk` 汇总，其内容不重复计入总大小。
`git 跟踪` 由 `git ls-files` 判定：已核验本表范围内**不存在任何被跟踪文件**（全为「否」），未触发停止条件。

**范围对账**（本表 vs 任务书预估）：
`.codex/tmp/diffusegi/` 62 项（= 预估 62）；`.codex/tmp/DiffuseGI*` 4 个（= 预估 4）；`diffusegi-editor*.log` 3 个（= 预估 3）；
`Screenshots/diffusegi-*/` 17 个目录（= 预估 17），另加范围边缘散落文件 1 个；
`.codex/tmp/ao/` 顶层 9 项（= 预估 9，即 8 文件 + `staged/` 目录），本表额外把 `staged/` 内 2 个文件各列一行，故 ao 共 11 行；
`Assets/Mine/.../DiffuseGIFeature.cs.new` 与 `.new.meta` 2 个（= 预估 2）。合计 100 行。

| 路径 | 类型 | 大小 | git 跟踪 | 建议 | 判据 |
|---|---|---|---|---|---|
| `.codex/tmp/diffusegi/DiffuseGI.shader` | 文件 | 6.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/DiffuseGIFunction.hlsl` | 文件 | 7.9 K | 否 | 需人工判断 | 与正式 `DiffuseGIFunction.hlsl` md5 不一致（疑似迭代中间态），归属不明 |
| `.codex/tmp/diffusegi/ScreenSpaceTrace.hlsl` | 文件 | 5.3 K | 否 | 需人工判断 | 与正式 `ScreenSpaceTrace.hlsl` md5 不一致（疑似迭代中间态），归属不明 |
| `.codex/tmp/diffusegi/drift-diag.cs` | 文件 | 3.5 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/hello.cs` | 文件 | 80 B | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/inspect-renderer.cs` | 文件 | 1.0 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/inspect-state.cs` | 文件 | 2.8 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/lod0-DiffuseGIFunction.hlsl` | 文件 | 8.8 K | 否 | 需人工判断 | lod0 实验副本，且与正式 hlsl md5 不一致，归属不明 |
| `.codex/tmp/diffusegi/lod0-ScreenSpaceTrace.hlsl` | 文件 | 6.0 K | 否 | 需人工判断 | lod0 实验副本，且与正式 hlsl md5 不一致，归属不明 |
| `.codex/tmp/diffusegi/ortho-control.cs` | 文件 | 2.5 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/ortho-control2.cs` | 文件 | 2.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/ortho.cs` | 文件 | 1.9 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/persist-feature.cs` | 文件 | 3.0 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-cameras.cs` | 文件 | 1.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-giregion.cs` | 文件 | 5.1 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-layers.cs` | 文件 | 1.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-mats.cs` | 文件 | 725 B | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-ortho2.cs` | 文件 | 1.4 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-pp.cs` | 文件 | 2.1 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-state.cs` | 文件 | 1.2 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-uniforms.cs` | 文件 | 1.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/probe-uniforms2.cs` | 文件 | 2.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/rt-capture-test.cs` | 文件 | 3.0 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/scene-state.cs` | 文件 | 787 B | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/skeleton-new.md` | 文件 | 18.3 K | 否 | 需人工判断 | 无对应正式文件（正式两个 skeleton md 的 md5 均不匹配），归属不明 |
| `.codex/tmp/diffusegi/state-now.cs` | 文件 | 647 B | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/sweep.cs` | 文件 | 4.7 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/sweep2.cs` | 文件 | 4.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/sweep3.cs` | 文件 | 1.9 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/sweep4.cs` | 文件 | 733 B | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-alive.cs` | 文件 | 3.1 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-anomaly.cs` | 文件 | 7.7 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-anomaly2.cs` | 文件 | 4.7 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-blur.cs` | 文件 | 4.5 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-dbg.cs` | 文件 | 3.8 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-dbg2.cs` | 文件 | 3.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-final.cs` | 文件 | 6.4 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-msaa.cs` | 文件 | 4.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-msaa2.cs` | 文件 | 4.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-neg-diag.cs` | 文件 | 4.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-persist.cs` | 文件 | 879 B | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-pollution.cs` | 文件 | 5.5 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-1.cs` | 文件 | 5.5 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-10.cs` | 文件 | 4.5 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-11.cs` | 文件 | 4.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-12.cs` | 文件 | 5.2 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-13.cs` | 文件 | 4.9 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-14.cs` | 文件 | 5.8 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-16.cs` | 文件 | 4.4 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-2.cs` | 文件 | 7.7 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-3.cs` | 文件 | 5.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-4.cs` | 文件 | 4.0 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-5.cs` | 文件 | 6.0 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-6.cs` | 文件 | 5.2 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-7.cs` | 文件 | 6.8 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-8.cs` | 文件 | 4.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-rt-9.cs` | 文件 | 4.0 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-sky.cs` | 文件 | 4.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-swap-diag.cs` | 文件 | 3.7 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-trace.cs` | 文件 | 4.3 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-trace2.cs` | 文件 | 3.4 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/diffusegi/verify-waterloc.cs` | 文件 | 3.6 K | 否 | 删除 | 会话临时脚本/中间文件，未跟踪，非正式产物 |
| `.codex/tmp/ao/probe_tex.cs` | 文件 | 558 B | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/register.cs` | 文件 | 3.3 K | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/restore.cs` | 文件 | 627 B | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/staged` | 目录 | 36.0 K | 否 | 删除 | 会话临时目录，未跟踪（含 2 个文件，大小为 du 汇总） |
| `.codex/tmp/ao/verify.cs` | 文件 | 2.3 K | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/verify2.cs` | 文件 | 6.4 K | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/verify3.cs` | 文件 | 6.9 K | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/verify4.cs` | 文件 | 7.0 K | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/verify_persist.cs` | 文件 | 1.9 K | 否 | 删除 | AO 会话临时校验脚本，未跟踪，非正式产物 |
| `.codex/tmp/ao/staged/AO.md` | 文件 | 11.9 K | 否 | 删除 | staged 副本，与正式文件 md5 一致，无独有内容 |
| `.codex/tmp/ao/staged/SSGI_Content_Skeleton.md` | 文件 | 21.6 K | 否 | 删除 | staged 副本，与正式文件 md5 一致，无独有内容 |
| `.codex/tmp/DiffuseGI.Check.dll` | 文件 | 10.5 K | 否 | 删除 | 临时编译产物（校验用 dll），未跟踪 |
| `.codex/tmp/DiffuseGI.Check.rsp` | 文件 | 45.1 K | 否 | 删除 | 临时编译产物（响应文件），未跟踪 |
| `.codex/tmp/DiffuseGI.md` | 文件 | 6.7 K | 否 | 需人工判断 | 内容与正式 DiffuseGI.md 不一致；可能含未转移到 Assets 的文档内容 |
| `.codex/tmp/DiffuseGIFeature.cs` | 文件 | 12.1 K | 否 | 需人工判断 | 正文与正式 DiffuseGIFeature.cs 的 md5 不一致；可能含未转移的改动 |
| `.codex/tmp/diffusegi-editor-clean.log` | 文件 | 513.7 K | 否 | 删除 | 会话运行日志，未跟踪 |
| `.codex/tmp/diffusegi-editor-utf8.log` | 文件 | 21.1 K | 否 | 删除 | 会话运行日志，未跟踪 |
| `.codex/tmp/diffusegi-editor.log` | 文件 | 32.6 K | 否 | 删除 | 会话运行日志，未跟踪 |
| `Screenshots/diffusegi-anomaly` | 目录 | 1.44 M | 否 | 删除 | 本会话截图目录，未跟踪（9 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-blur` | 目录 | 280.0 K | 否 | 删除 | 本会话截图目录，未跟踪（4 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-final` | 目录 | 1.54 M | 否 | 删除 | 本会话截图目录，未跟踪（12 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-msaa` | 目录 | 1.45 M | 否 | 删除 | 本会话截图目录，未跟踪（9 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-msaa2` | 目录 | 912.0 K | 否 | 删除 | 本会话截图目录，未跟踪（4 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-ortho-check` | 目录 | 4.82 M | 否 | 删除 | 本会话截图目录，未跟踪（11 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt` | 目录 | 2.36 M | 否 | 删除 | 本会话截图目录，未跟踪（13 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt2` | 目录 | 2.80 M | 否 | 删除 | 本会话截图目录，未跟踪（15 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt3` | 目录 | 800.0 K | 否 | 删除 | 本会话截图目录，未跟踪（5 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt4` | 目录 | 516.0 K | 否 | 删除 | 本会话截图目录，未跟踪（3 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt5` | 目录 | 528.0 K | 否 | 删除 | 本会话截图目录，未跟踪（3 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt6` | 目录 | 828.0 K | 否 | 删除 | 本会话截图目录，未跟踪（4 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-rt7` | 目录 | 960.0 K | 否 | 删除 | 本会话截图目录，未跟踪（5 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-takeover` | 目录 | 4.00 M | 否 | 删除 | 本会话截图目录，未跟踪（12 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-takeover-01.png` | 文件 | 27.2 K | 否 | 需人工判断 | 位于 Screenshots/ 根、非 diffusegi-*/ 目录，处盘点范围边缘；未跟踪 |
| `Screenshots/diffusegi-takeover2` | 目录 | 2.40 M | 否 | 删除 | 本会话截图目录，未跟踪（8 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-takeover3` | 目录 | 888.0 K | 否 | 删除 | 本会话截图目录，未跟踪（2 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Screenshots/diffusegi-trace` | 目录 | 412.0 K | 否 | 删除 | 本会话截图目录，未跟踪（6 个文件）；Screenshots/ 下已跟踪的历史截图不在此范围 |
| `Assets/Mine/Shaders/PostProcess/SSR/DiffuseGIFeature.cs.new` | 文件 | 12.8 K | 否 | 删除 | 任务书事实#4：门禁写入前的草稿副本，未跟踪；实际删除需 Assets/Mine 写入门禁 |
| `Assets/Mine/Shaders/PostProcess/SSR/DiffuseGIFeature.cs.new.meta` | 文件 | 155 B | 否 | 删除 | 任务书事实#4：门禁写入前的草稿副本，未跟踪；实际删除需 Assets/Mine 写入门禁 |

## 汇总

- 总项数：**100** 项（`删除` 92 ｜ `保留` 0 ｜ `需人工判断` 8）
- 总大小：**27.50 M**（28,831,390 B，范围内全部文件字节之和）
- `删除` 项合计大小：**27.71 M**（文件 907.6 K + 目录 du 汇总 26.83 M）
- 注：目录行 `du -sk` 含块对齐开销，故 `删除` 合计（目录部分）略大于上行「总大小」（纯文件字节口径）。

### 需人工判断条目（8）

- `.codex/tmp/diffusegi/DiffuseGIFunction.hlsl`（7.9 K）— 与正式 `DiffuseGIFunction.hlsl` md5 不一致（疑似迭代中间态），归属不明
- `.codex/tmp/diffusegi/ScreenSpaceTrace.hlsl`（5.3 K）— 与正式 `ScreenSpaceTrace.hlsl` md5 不一致（疑似迭代中间态），归属不明
- `.codex/tmp/diffusegi/lod0-DiffuseGIFunction.hlsl`（8.8 K）— lod0 实验副本，且与正式 hlsl md5 不一致，归属不明
- `.codex/tmp/diffusegi/lod0-ScreenSpaceTrace.hlsl`（6.0 K）— lod0 实验副本，且与正式 hlsl md5 不一致，归属不明
- `.codex/tmp/diffusegi/skeleton-new.md`（18.3 K）— 无对应正式文件（正式两个 skeleton md 的 md5 均不匹配），归属不明
- `.codex/tmp/DiffuseGI.md`（6.7 K）— 内容与正式 DiffuseGI.md 不一致；可能含未转移到 Assets 的文档内容
- `.codex/tmp/DiffuseGIFeature.cs`（12.1 K）— 正文与正式 DiffuseGIFeature.cs 的 md5 不一致；可能含未转移的改动
- `Screenshots/diffusegi-takeover-01.png`（27.2 K）— 位于 Screenshots/ 根、非 diffusegi-*/ 目录，处盘点范围边缘；未跟踪

### 执行提示（人工确认后）

- **危险区**：`Screenshots/` 根下仍有 137 个已被 git 跟踪的历史截图（`screenshot_2026-06-*.png` 等）。
  清理只允许精确删除 `Screenshots/diffusegi-*/` 17 个目录与 `Screenshots/diffusegi-takeover-01.png`；**禁止** `Screenshots/*` 通配删除。
- `Assets/Mine/Shaders/PostProcess/SSR/DiffuseGIFeature.cs.new`(.meta) 位于 `Assets/Mine/` 下，实际删除需走写入门禁。
- 未读取脚本内容判定归属；`.new` 与正式文件的注释差异沿用任务书事实#4，未逐行复核。
- 本清单生成过程中未执行任何 `rm` / `mv` / `git rm`；探针文件亦未遗留。
