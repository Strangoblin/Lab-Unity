---
name: 面板参数分层规范入模板层 + 模板 API 可核验落地
description: PCSS 参数整理的三层判据提升为函数无关规范 + 骨架；实测暴露模板从未验过（3 处缺陷 + 1 条写反的规范），并把「模板可编译」重定义为「模板 API 可核验」+ check_api_refs.py
date: 2026-09-20
---

# 面板参数分层规范入模板层 + 模板 API 可核验落地

来源：unity-developer 侧 PCSS 参数整理（Technical / Artistic 两层 → 新增 Performance 第三层）。
用户要求把「面板参数分类简化」这一行为从一次实践提升为模板层规范。

## 一、落点与归属判定

| 内容 | 落点 | 行数 |
|---|---|---|
| 判据正文（规范） | `unity-developer/references/standard/script/parameter-panel.md` | 63 |
| 代码骨架 | `unity-developer/templates/standard/script/parameter-panel.cs` | 110 |

- 归 `standard/` 而非新家族：规范对 RendererFeature 的 `Settings`、MonoBehaviour、VolumeComponent
  **一律适用** ⇒ function-independent，与模板层已有的 standard 定义一致。
- 三层 = **Technical / Performance / Artistic + Debug**（Debug 与前三维正交）。关键结论：决定暴露形式的
  不是「属于技术还是美术」（伪二分），而是**成本占比 + 是否需要微调**两个正交问题；都答「是」才划档位。
  **暴露形式本身就是信息。**
- 接线 3 处指针（各司其职、不重述内容）：`script-structure.md` §3① 的 `[Header]` 分组处、
  `feature-script-structure.md` 的 Feature `Settings` 骨架注释、`rules/csharp-renderpass.md`
  （唯一会被自动注入读到的入口）。另修 `references/standard/script/README.md` 的过期句。

## 二、主线以外：模板从未被验过，实测暴露 3 处缺陷

`template-conventions.md` 写着「⚠️ 只出现在注释行与字符串 —— **激活代码零内联（可编译承诺）**」，
但验证清单 5 项**无一真的编译**。实测后发现承诺从来没被兑现过，模板带病数周：

| 文件 | 实测缺陷 |
|---|---|
| `urp-renderpass.cs` | ① `⚠️` 进标识符 → CS1056；② `activeColorTextureDescriptor` 是**凭空的 API**（`UniversalResourceData` 只有 TextureHandle 成员）；③ 给 `RenderTextureDescriptor.depthBufferBits`（`int`）赋 `DepthBits` 枚举 → CS0266 |
| `volume-template.cs` | `[VolumeComponentMenuForRenderPipeline]` 自 2023.1 起过时，Unity 6 下 CS0619 **编译错误** |
| `fullscreen-postprocess.shader` | `Frag_⚠️YourEffect` 与 `Shader "…/⚠️YourEffectName"` 把 emoji 写进标识符 |

修复：urp-renderpass 单 Pass 路径删掉无用的描述符块（cameraColor→cameraColor 不需要），多 pass 块改用
`renderGraph.GetTextureDesc` + `renderGraph.CreateTexture`（均已核对签名）；volume-template 改为
`[VolumeComponentMenu]` + `[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]`；
shader 与 cs 的占位符统一成单一合法 token `YourEffect`（替换后 Shader 名 / Pass 名 / 函数名全覆盖、0 残留）。
**结果：4 个 `.cs` 模板全部 0 错误**（此前抽查仅 2 个通过）。

## 三、连带发现：一条规范写反了

`rules/csharp-renderpass.md` 原文「用 `[VolumeComponentMenuForRenderPipeline]`，非旧版
`[VolumeComponentMenu]`」——**两处都反**：前者是过时 API（Unity 6 报错），后者才是正确写法。
`references/shader/postprocess/volume-component.md` 有 3 处同样断言（含「是 Unity 6 的新属性」）。
权威依据：URP 17 自身的 `Bloom.cs` / `SplitToning.cs` 用
`[Serializable, VolumeComponentMenu("Post-processing/…")]` + `[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]`。

**为什么能存活**：项目当前**没有任何生效的 VolumeComponent**（SSL 走 Feature+Settings），
唯二两份 `SSLVolume.cs` 都在点开头的 `.backup_*` 目录里（Unity 不编译）⇒ 这条错规则从未被触发。
已同改 rules + reference 3 处 + 模板，三方一致。

## 四、方法学（比结论更值钱）

- **编译自检装置**：`/tmp` 一次性 csproj（`EnableDefaultCompileItems=false` + `<Compile Include>` +
  HintPath）跑 `dotnet build`，不改仓库、不触发域重载。配方留在 `template-conventions.md`，
  已提升为 `.mcp/validation/check_api_refs.py --compile`（见 §五）。
- **Unity 模块路径**取安装目录，**URP 程序集取项目 `Library/ScriptAssemblies/`**——后者最可靠。
  按旧印象写 `Contents/Managed/` 会得到 30 个 CS0246，看着像模板坏了其实是路径错。
- **控制组必做**：故意注入已知缺陷的副本要能报错，否则装置可能是空转（它当天就抓到了一处真 bug，见 §五）。
- ⚠️ **我自己的全称断言翻车了一次**：首轮只扫 `*.cs` 就写「urp-renderpass.cs 是唯一违反者」，
  全类型重扫后发现 `fullscreen-postprocess.shader` 同样违反 —— 与今日另一条 memory
  「单次观察不足以下全称断言」是同一个坑，**扫描面本身也是断言的一部分，必须写明扫了什么**。
- ⚠️ **配对检查也要防误报**：naive 地数 `HLSLPROGRAM`/`ENDHLSL` 报「不配平」，
  实为 `HLSLINCLUDE`…`ENDHLSL` 的合法配对。**报警后先读原文再改**。

## 五、裁决：「可编译」还是「API 可核验」

用户质疑「一般模板不需要编译，属冗余操作」——**部分同意，且这个质疑指向了正确的修正**：
「能编译」把**完整性**与**真实性**两件事捆在一起。完整性（骨架自足编译）确实冗余且过度——
骨架带占位符，`.shader`/`.hlsl` 更无从编译；但**真实性必须成立**，因为模板是给人**拷贝**的，
任意一行错 API 会复制进每一份拷贝。**编译从来只是探测手段，不是目的。**

改为按真实性设计：**词法 + 过时 API 为默认检查，无完整性前提，四种代码文件一视同仁；编译降级为
自足 `.cs` 的可选深检**（`--compile`）。这是可核验性（verifiability）优先于自足性的直接结果。

## 六、CLI 落地 `.mcp/validation/check_api_refs.py`（2026-09-20）

| 检查 | 抓什么 | 覆盖 | 手段 |
|---|---|---|---|
| 词法 | 标识符内非 ASCII（注释/字符串之外） | `.cs/.shader/.hlsl/.compute` | 逐字符状态机剥注释与字符串 |
| 过时 | 引用名在 URP / Core RP 包源码里带 `[Obsolete(..., true)]` | 同上 | 扫 `Library/PackageCache/*/Runtime`，**以声明处为准，不靠记忆** |
| 编译 | 凭空 API 及其余一切 | 仅自足 `.cs` | `--compile` 追加，走 §四 的 csproj 装置 |

实测：16 个模板全绿；控制组三例 d1 词法 / d2 过时 / d3 凭空 API 全部被对应检查抓到。

⚠️ **控制组抓到的是 CLI 自己的 bug**：过时检查**一直在静默空转**——包目录前缀用
`basename(glob).split("*")[0]` 取值得到 `"Runtime"`，`startswith` 恒假 ⇒ 索引为空 ⇒ 检查 2 对任何
输入都返回「通过」。若无控制组，这个 CLI 会以「全绿」的形式骗过所有人。已改为 `PKG_PREFIXES`
前缀元组，并**在索引为空时大声告警**。教训：**一个静默空转的检查比没有检查更坏。**

d3 只在 `--compile` 下才报 —— 这是诚实的边界而非缺陷：过时检查只能判「存在但已废弃」，
**凭空造出的成员名只有编译器能判**。

## 七、边界

- `.shader` / `.hlsl` **的真编译依赖 Unity 导入**（ShaderLab 外壳 + Unity 宏；`glslangValidator`
  装了但不认这些），本层不做。已做的仅是词法与内部一致性验证（函数声明↔`#pragma fragment` 一致、
  token 替换自洽、括号配平、HLSLINCLUDE 配对）。**此边界写进规范，不假装已验证。**
- 过时索引只覆盖 URP / Core RP 两个包；UnityEngine 本体、其它包、第三方库不在索引内。

## 八、P1-P3

- **P1**：判据正文 63 行（≤80）；110 行代码全部落 `.cs` 骨架，规范 MD 内零长代码块；
  `template-conventions.md` 改完仍是 76 行。
- **P2**：规范只规定「怎么判」，骨架只示范「怎么写」；骨架注释保留 2 行判据摘要 + 指向正文，
  因为它会被拷贝出仓库、必须自洽。CLI 用法在 `.mcp/README.md` 只留摘要，判据权威在
  `template-conventions.md`，不两处重述。
- **P3**：删掉 `rules/csharp-renderpass.md` 末尾与诊断表重复的「错误处理 3 次」行；
  全库 `grep parameter-panel` = 7 个引用点，无同主题第二份权威源；
  VolumeComponent 的写法在 rules + reference + 模板三处**同改**，不留单边旧说法。

