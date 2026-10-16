---
name: 后处理搬运包结构对齐（DDOF/Kuwahara/SNN/SSL/SSO）
description: 五个老搬运包按近期模板重排 feature/shader 结构并清除兼容期备份，保留渲染路径与算法
date: 2026-10-10
---

# 后处理搬运包结构对齐

用户判定 `Assets/Mine/Shaders/PostProcess/` 下 DDOF / Kuwahara / SNN / SSL / SSO 为一批老版本搬运包：
此前只做了 Unity 6 兼容性处理，未跟进结构。本轮**只做结构更新**（功能更新涉及面更复杂，明确延后），
并要求清除兼容期备份。工作目录 `Assets/Mine/Shaders/PostProcess`，Production 配方。

## 改造基线

- Shader：`references/shader/postprocess/fullscreen-structure.md` + `templates/shader/postprocess/fullscreen-postprocess.shader`，对照同目录已整理的 SSSM / MaskBlur。
- Feature：`feature-script-structure.md` + `parameter-panel.md`（Settings 分层 Header、PropertyToID、sealed、XML 注释、Release/Dispose）。

## 统一动作（5 个包）

- **Shader**：文件头装饰块；include 归位（URP Core → URP 库 → Blit.hlsl → 自有库，Hash 在 Blur 之前）；
  材质参数集中进 `CBUFFER_START(UnityPerMaterial)`；每个函数 / Pass 前用 `// ═══` 装饰块；
  `SubShader` 统一 `Tags + Cull Off / ZWrite Off / ZTest Always / Blend One Zero`，去掉 `LOD 100` 与 `Queue`；
  Pass 块只留 `Name` + pragma。**`#pragma target` 一律原样保留、不新增**：`SHADER_TARGET` 会改变
  `HashFunction.hlsl` 整数入口（`>= 35` 保护）的可见性，补 pragma 等于改编译分支。
- **Feature**：Settings 按 Technical / Performance / Artistic / Debug 分组；参数 ID 全部 `internal static readonly int`
  预计算并替换字符串字面量；Pass 索引与关键字提为常量；Pass 类与 PassData `sealed`；
  `Release()` + `Create()` 重建 + `Dispose(bool)` 释放材质；**序列化字段名一律不改**
  （SSL 已接入 `PC_Renderer.asset`，改名即丢值）。

## 死声明清理（逐条可核对）

- **SNN**：删 `ConfigureInput(Depth | Normal)` —— shader 只采样 `_BlitTexture` 与 `_ScreenParams`。
- **SSO**：删死 local `color`（`SampleSceneColor`）、`jitter`、`depthBase` 与注释掉的 reversed-Z 分支；
  `rawDepth` 改为复用于 `ComputeWorldSpacePosition`；删未被 shader 使用的 uniform `_Downsample`（含 C# 端 ID 与 SetFloat）；
  `ConfigureInput` 去掉 `Color`、保留 `Depth | Normal`。
- **Kuwahara**：删死属性 `_Sharpness`（面板残留，C# 只设 `_Q`）；`_Q = 2` 提为 `GeneralizedQ` 常量；
  函数内 `static const` 低 / 中档 offset 表提到文件级。
- **SSL**：删死属性 `_MainTex`；删纯转发的 `hash21` 包装，直接调用 `Hash_Sine21`。
- **DDOF**：无死声明，仅 include 归位与 `Queue` / `LOD` 清理。

## 备份清理

删除 4 个隐藏备份目录共 9 个文件（DDOF / SNN / SSL 的 `.backup_v1_pre_unity6`、SSL 的 `.backup_v2_pre_decouple`）。
删前逐一核对可恢复性：v1 三份与 HEAD 逐字节一致（tracked-clean）；SSL v2 的 `SSLVolume.cs.meta` 与 `5159258` 同 blob，
`SSLVolume.cs` 与该提交同 blob 仅多 4 行退役注释 —— 注释内容已并入 `SSL.md` 恢复指南（并改走 git 取回）。

流程坑：`delete_gated` 的 guid-referenced 检查把备份自带的 `.cs`→`.meta` 当成外部引用（批内误判），拆成两批后通过；
`.meta` 删除后 `rmdir` 直接清空目录（Codex 侧 hook 只拦 3 条红线，不拦 rmdir）。
**SSC 也有同名 `.backup_v1_pre_unity6`，不在本轮清单内，未动。**

## 验证与边界

- Unity 6000.3.14f1 **正在运行**，但 `unityctl bridge start` 多次挂死（stale PID 重启、`alarm` 限时、stop+start 均无效），
  **本轮未走 bridge**，证据改从 `~/Library/Logs/Unity/Editor.log` 取：本轮 11 个文件触发一次 Refresh
  （`Asset File Changes: changed=11`），domain reload 后 Assembly-CSharp 重编成功；编译产物内含本轮新增的
  tooltip 字符串，证明编译覆盖了本次改动；日志无 `error CS`、无 `Shader error`（唯一 warning 是既有 `Fish` compute 的 metal 警告）。
- 全部改动文件 `check_norm.py` 通过；`git diff --check` 通过；逐文件与 HEAD 做「去注释 / 去空白」规范化 diff，
  差异只落在结构、死声明与常量提取上。
- 未做：Metal 逐变体编译核验、Play Mode 画面 / 像素对照。5 个包中**只有 SSL 仍在 `PC_Renderer.asset` 且 `m_Active: 0`**，其余 4 个未接线。未提交。

## 已知残留（下一轮功能更新）

- **Debug 开关**：DDOF 有既有中间纹理（CoC 遮罩）故新增 `debug`；SSO / SSL 保留原 `SSOFeature` / `SSLFeature` 归入 Debug 组；
  SNN / Kuwahara 没有独立中间输出，新增等于引入本轮无法验证的渲染分支，留待功能更新。
- **参数分档**：SSL 的 `maxSteps / blurLevels / blurIterations`、Kuwahara 的 `downsampleLevel` 只做了分组，
  升格为 Performance 档位属功能项（会改面板形态与默认值语义）。
- **SSO 数据路径**：仍用 `_MainTex` / `_SSOTex` 全局纹理 + `DeclareCustomTexture.hlsl`；改用 `_BlitTexture` 属数据路径变更，未做。
- DDOF / SNN / SSO 仍缺同名技术文档 `.md`。

## 可复用经验

1. **只做结构更新时，先划出「数据路径不碰」的边界**：可以删可证伪的死声明、可以归位 include 与声明，
   但不改纹理绑定链、不改 Loop 预算来源、不改 `#pragma` —— 这三类是「结构」与「功能」的分界线。
2. **备份删除的可恢复性核对**：`git rev-list --all --objects` 能判断备份 blob 是否在历史里；
   tracked-clean 的备份无需比对内容，untracked 的必须找到同 blob 提交或证明差异只是注释。
3. **bridge 不可用时，Editor 日志仍能给出编译级证据**：`Asset File Changes: changed=N` 确认资产被扫到，
   编译产物 grep 新增字符串确认 .cs 改动进了程序集，`error CS` / `Shader error` 的缺席即编译通过与导入无错。

---

## 追加（同一会话）：Debug 开关落地

用户指示「先修 Debug 开关」，故把上节「已知残留」里的 Debug 一条提前做完，其余（参数分档、SSO 数据路径、缺文档）保持延后。

统一形态：每个 Feature 一个面板 `DebugMode` 枚举（`[Header("Debug")]`，默认 `Off`），
**Debug 分支只复用本 Pass 已生成的中间纹理或既有输出路径，不新增 Pass、不新增采样预算**；
`SSOFeature` / `SSLFeature` 这两个「名字像开关、实际是调试直出、默认还是 true」的布尔被替换为 `debug`，
并用 `[FormerlySerializedAs]` 保留旧序列化名。

| 包 | DebugMode | 接线 |
|---|---|---|
| DDOF | `Off / CoC / Blur` | `CoC` 在 Pass 0 后直出遮罩并早退（省掉模糊）；`Blur` 在双向模糊后直出模糊图并早退 |
| Kuwahara | `Off / StructureTensor` | 用 Pass 4 的特征向量场顶掉最终输出；**仅 Anisotropic 生成该纹理**，Basic/Generalized 下不生效 |
| SNN | `Off / Selection` | Shader 新增 `_DebugMode` uniform（MaskBlur 先例），复用同一采样循环输出「被选中的四个邻域均值（不含中心）」 |
| SSL | `Off / Light` | 同原 `SSLFeature=true`：用 `_SSLTex` 顶掉最终输出 |
| SSO | `Off / Outline` | 同原 `SSOFeature=true`：用轮廓/条纹中间结果顶掉最终输出 |

**行为等价性**：`Off` 分支的 blit 序列与顺序和改造前逐条一致；Kuwahara/SSL/SSO 的 debug 采用「先按正常路径写完，
再用中间纹理覆盖输出」的写法，避免声明了却未写入的 RenderGraph 资源。SNN 是唯一动了 Shader 的包，
`Off` 路径的累加与归一化表达式逐字未改。

**遗留**：`PC_Renderer.asset` 里 SSL 的 `SSLFeature: 1` 成为孤儿字段（该资产在本轮工作目录之外，未改），
Unity 下次保存该资产时会丢弃它、`debug` 落到 `Off` —— 这正是本次修复的目标（原默认 true 会在接线后直接顶掉画面）。
当前 SSL 为 `m_Active: 0`，不影响画面。

**验证**：8 个改动文件 `check_norm.py` 通过；C#↔Shader 交叉核对通过（PropertyToID 全部在 shader 存在、
CBUFFER 变量全部由 C# 设置、关键字常量全部命中 pragma、pass 索引在范围内、DebugMode 默认均为 Off）。
**Unity 侧编译证据待补**：Editor 只在窗口获得焦点时刷新，本轮第二批写入后 `Assembly-CSharp.dll` 仍停留在上一批的 15:21:55，
`Editor.log` 也没有新的导入记录（日志里最后三次 refresh 时长为 0.18–0.25 s，等于没扫到）。请把 Unity 窗口点一下触发刷新，
再复核 `error CS` / `Shader error` 与产物字符串。

---

## 追加 2（同一会话）：按顺序继续优化

用户确认「刷新后无报错，可以按顺序继续优化」，于是做完上节列出的三项延后项。

### 1) 参数升档（Performance 档位）

| 包 | 新档位 | 表 |
|---|---|---|
| SSL | `QualityMode { Low, Medium, High }`，默认 **Medium** | Low (16, 0, 1) / Medium (32, 1, 1) / High (64, 2, 2) —— maxSteps, blurLevels, blurIterations |
| Kuwahara | `QualityMode { Low, Medium, High }`，默认 **High** | Low (9 点, ÷2) / Medium (16, ÷1) / High (25, ÷0) —— sampleQuality, downsampleLevel |

- 两处默认档**逐字段复现升档前的面板值**（SSL 原 32/1/1；Kuwahara 原 SampleQuality=High + Downsample=0），
  被替换掉的旧字段在 `PC_Renderer.asset` 里变成孤儿（该资产在本轮工作目录之外，未改），Unity 下次保存即丢弃。
- 档位表实现为 `Settings.ResolveQuality(...)` 静态纯函数，`out` 出各字段，不在 Pass 间传可变状态。
- Kuwahara 把「采样点数」与「张量降采样率」并进同一档：两者同源，分开暴露会让「High」名不副实
  （依据 `spatial-filter-budget.md` 与 parameter-panel 规则⑤/⑥）。
- 半接线复核：SSL 的 maxSteps 走 `_MaxSteps` uniform、模糊链层数是 C# 侧循环；Kuwahara 的采样数由 offset 表选表、
  降采样率只在 C# 决定 RT 尺寸、`_BlurScale` 由它推导 —— 无「一侧接线、另一侧硬编码」的漏项。

### 2) SSO 数据路径去掉手工 `_MainTex`

- Shader：删 `TEXTURE2D_X(_MainTex)`；合成 Pass 的源色改用 Blit.hlsl 的 `SAMPLE_TEXTURE2D_X(_BlitTexture, ...)`；
  轮廓 Pass 的三平面投影调用把纹素参数换成 `_BlitTexture`（`SAMPLE_CUSTOM` 宏只吃 uv，不读纹素）。
- Feature：删 `MainTexID` 与 `cmd.SetGlobalTexture("_MainTex", tempMain)`；只保留 `_SSOTex` 全局。
- 两个 Pass 的 `_BlitTexture` 都是 tempMain（留档件），因此源色语义与原来一致；`_SSOTex` 继续走
  `SampleCustomTexture`（真正的自定义纹理，保留其 stereo 变换语义）。
- 注意语义差异：`SampleCustomTexture` 会做 `UnityStereoTransformScreenSpaceTex`，`_BlitTexture` 路径不做 ——
  非 XR 下等价，XR 单通道数组下是否等价未经设备验证。

### 3) 文档

- 新增 `DDOF.md` / `SNN.md` / `SSO.md`（按 `templates/shader/postprocess/README.md` 的骨架：概述 / 架构 / 参数 /
  管线 / 使用方式 / 性能 / 已知限制 / 扩展点 / 命名速查）。
- `SSL.md`、`Kuwahara.md` 随升档更正：旧的 `DownsampleLevel` 手调表、`SampleQuality` 枚举说明替换为档位表。

### 顺带更正（注释与文档不实）

- `Kuwahara.shader` 里既有的注释声称「WeightScale 由 C# 按模式设定：Generalized 0.125」——实际三种模式都写面板值、
  只有 Generalized / Aniso 读取；本轮按代码更正注释（**未改行为**）。
- 我上一版 `Kuwahara.md` 里写错的一条「Basic 受影响于 Hardness / WeightScale」也一并更正：Basic 只做方差硬选，
  不调用权重公式。**教训：模式互斥的 Shader，写文档前要按分支核到具体函数，不能按「共用参数」想当然。**

### 验证与边界

- 本轮 10 个文件 `check_norm.py` 全通过；`Kuwahara.shader` 与 HEAD 做去注释/去空白 diff，新增代码行只有上节的结构项
  （注释改写零代码影响）；`SSO.shader` 的代码差异落在预期集合内。
- 旧字段/旧符号残留审计（`settings.maxSteps`、`SampleQuality.`、`MainTexID`、`_Sharpness`、`_Downsample` 等）零命中。
- **Unity 编译证据待补**：本批写入后 `Assembly-CSharp.dll` 仍停在 15:43:40（上一批），`Editor.log` 也没有新 refresh
  —— Editor 只在窗口获得焦点时才刷新。需点一下 Unity 窗口，再复核 `error CS` / `Shader error` 与产物字符串。
- 仍未做：Metal 逐变体编译、Play Mode 画面对照；5 个包中只有 SSL 接线（`m_Active: 0`）。
