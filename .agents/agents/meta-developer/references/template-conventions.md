# Template & Content Authoring Conventions

> .agents 代码模板与知识文件（templates/ + references/ 层）的编写规则。meta-developer 维护模板体系时对照；细则权威在模板族 README（下表链接）与 `.agents/rules/`，本文件只做摘要不重述。
> 演进记录：memory/2026-09-03-template-reference-taxonomy.md（分类决策）；unity-developer/memory/2026-09-03-standard-code-window-family.md、2026-09-03-render-hlsl-template-families.md（后续轮次）。

## 分类总则

按实际职责分类，不按扩展名：`standard/` = 函数无关、`script/` = C# 责任族、`shader/` = 特征族；references/ 镜像到相应 agent。现况家族树（2026-09-03；最新以族 README 为准，根索引 `../../unity-developer/templates/README.md` + `shader/README.md`，状态词 Available/Planned）：

| 家族 | 职责 | 权威 README |
|------|------|------|
| standard/ | 函数无关代码骨架 + 文档模板（与 references/standard 规范配对：规定/骨架） | [unity templates](../../unity-developer/templates/standard/README.md) |
| script/* | Baker / Window / Generator / Manager / Controller（窗口壳 = 跨族共享形态） | [script](../../unity-developer/templates/script/README.md) |
| shader/postprocess | 全屏 Blit 管线代码件（RendererFeature/RenderPass/Volume/Compute） | [postprocess](../../unity-developer/templates/shader/postprocess/README.md) |
| shader/render | 物体上渲染的效果材质（direct-effect / effect-shader+function 对） | [render](../../unity-developer/templates/shader/render/README.md) |
| shader/hlsl | 跨效果共享函数库（对应 `Assets/Mine/Special/HLSL`，非 feature） | [hlsl](../../unity-developer/templates/shader/hlsl/README.md) |
| shader/particle | 占位 | — |

## 代码模板文件编写规则

| 规则 | 要求 |
|------|------|
| 横幅 | 79-char ═ 等宽 + 中文（定位 / 实源 / 使用方式）；文件内分区用 3-char ═ |
| ⚠️ 标记 | 标注需自定义位置；⚠️ **只进注释行与字符串，不得进标识符**（非 ASCII 在标识符里是编译错误，2026-09-20 实测 CS1056） |
| 占位标识符 | YourXxx 单一合法 token（YourBaker / YourEffect / YourDomain），拷贝后全局替换 |
| 自定义 include | 激活行不引用不存在路径 → 注释态 + ⚠️ 行给真实写法；复杂对 =「双文件同拷 + include 路径同步」 |
| 实源标注 | 头部列 Assets/Mine/… 真实实源；旧写法 / 外部生成物按现行规范重写，不逐字照抄 |
| 族目录规则 | 独特族只有 README.md 是 markdown，其余 = 代码模板体；文档写作要点并入族 README |
| 族 README 范式 | 定位句 + `Template \| Actual role` 表 + 决策表 / 要点 + ⚠️ 约束注 |
| 防悬空 | README / memory 声称的文件必须真实落盘（editor-baker-window 前科） |
| 兼容 | 默认 Metal 兼容（target 2.0 等），与 rules/shader-development.md 一致 |

## HLSL 库编写规则（摘要；细则权威 = `shader/hlsl/README.md` 三档决策表）

- **拆库裁决**：单效果私有数学 → 与其 shader 同目录私有库（可自持 CBUFFER / static const）；跨效果横切 → `Assets/Mine/Special/HLSL/` 共享库（**零 CBUFFER、零兄弟 include**，调用方一律 Assets 全路径 include）
- **guard**：`#ifndef <文件名去连字符全大写>_HLSL_INCLUDED`，与文件名绑定 —— 重命名必同步
- **依赖三档**：A 纯函数（拷贝即用）/ B 依赖调用方上下文（不自行 include，头部合同注释要求调用方先 include）/ C 自带 include + 全局资源命名契约（`_FGDLut` / `_HeightMap`）
- **命名**：语义模块前缀（`BlendNormal_` / `SDF_` / `BRDF_`），避无前缀通用名（ShadowFunction × AdditionalLightsFunction 的 MainLight 重复定义地雷）
- 顺序铁律（URP 内置 → 自有 / 依赖前置 / 调用先于定义）属 rules/shader-development.md，不重复

## 验证清单（模板 / 库文件新增或修改后）

1. `python3 .mcp/validation/check_norm.py <file>`（Codex 产出走同一 CLI 自查）—— error 级违规阻断
2. `python3 .mcp/validation/check_api_refs.py <dir>` —— 词法 + 过时 API（见下）
3. `ls` 族目录：独特族只有 README.md 是 markdown
4. grep 残留（占位句 / 旧文件名）零输出；README 相对链全部可解析（broken: NONE）
5. memory：agent 侧建 dated 文件 + 更新 MEMORY.md / memory.md 索引
6. `git diff --check` 无尾随空白等

### 模板 API 核验 — `.mcp/validation/check_api_refs.py`

**要的不是「能编译」，是「写下的 API 真实存在且没过时」。** 理由：模板是给人**拷贝**的参考实现，
不要求自足编译（骨架本就有占位符，`.shader`/`.hlsl` 更是没法用编译器验）；但任意一行错 API
会复制进每一份拷贝。API 核验没有完整性前提，且四种代码文件一视同仁。

```bash
python3 .mcp/validation/check_api_refs.py .agents/agents/unity-developer/templates
python3 .mcp/validation/check_api_refs.py --compile <自足骨架.cs>   # 可选深检
```

| 检查 | 抓什么 | 覆盖 |
|---|---|---|
| 词法 | 标识符内非 ASCII（注释/字符串之外） | 全部四种 |
| 过时 | 引用的名字在包源码里带 `[Obsolete(..., true)]` | 全部四种 |
| 编译（`--compile`） | 凭空 API 及其余一切 | 仅自足 `.cs`，需 dotnet |

**边界（不假装）**：凭空造出的成员名只有编译器能判——过时检查只能判「存在但已废弃」。
`.shader`/`.hlsl` 的真编译依赖 Unity 导入，本层不做。

**两条方法学铁律**：

- **必须带控制组**：把已知缺陷注入副本，确认能被抓到。2026-09-20 首次实现时正是控制组
  发现「过时索引静默为空、检查 2 一直在空转」——包目录前缀取错，CLI 每次都说通过。
  **一个静默空转的检查比没有检查更坏。** CLI 已内置索引为空时告警。
- **一次刷全族**，别只测刚改的：存量模板同样可能从未验过（2026-09-20 实测 `urp-renderpass.cs`
  带 2 个真实 API 错误、`volume-template.cs` 带 1 个过时 API 错误，已潜伏数周）。
