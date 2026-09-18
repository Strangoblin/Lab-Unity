# Standard Shader Structure

> Function-independent Shader structure guidance for regular material shaders.
> 记录日期: 2026-06-12（2026-09-03 同步目录布局：私有库随 shader 同目录，`Special/Shaders/` 已退役）
> 参考实现:
> - 普通 Shader: [PBRToon.shader](Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader)
> - 复杂 Shader 分层: [RainDrops/](Assets/Mine/Shaders/Render/RainDrops/) + [RainDrop.hlsl](Assets/Mine/Shaders/Render/RainDrops/RainDrop.hlsl)
> - 功能库: `Assets/Mine/Special/HLSL/`

---

## 普通 Shader 编写结构参考

适用于常规材质 Shader（PBR、Toon、Unlit 等），核心原则：**结构分明、功能解耦、功能分级**。

Unity 6 / URP 17 管线能力以 [网格材质模板契约](../../../templates/standard/shader/README.md) 为准；下文布局示意不代表完整管线能力。PBRToon 是算法参考，不代替当前 URP 接口定义。

### 1. 整体布局

```
Properties                     —— 对外暴露的参数，按功能分组，[Header] 分组
HLSLINCLUDE                    —— 所有渲染代码集中于此
  #include                      —— 外部库引用
  TEXTURE2D_X / SAMPLER         —— 纹理声明
  CBUFFER_START(UnityPerMaterial)  —— 参数用 cbuffer 统一管理
  struct Attributes             —— 顶点输入
  struct Varyings               —— 顶点到片元传递
  // 工具函数                   —— 小尺寸工具（如法线计算）
  // Vert 函数                  —— 顶点着色器
  // Frag 函数                  —— 片元着色器（每个 Pass 一个）
ENDHLSL
SubShader                       —— 仅包含 Pass 定义
  Tags / LOD
  Pass "FORWARD"
    Cull / ZWrite / ZTest / Blend / Stencil
    HLSLPROGRAM
      #pragma vertex / fragment / shader_feature / multi_compile
    ENDHLSL
  Pass "OUTLINE"                —— 每个 Pass 一个 HLSLPROGRAM 块
  Pass "ShadowCaster"
  Pass "DepthOnly"
  Pass "DepthNormals"
```

### 2. 命名约定

| 类型 | 命名模式 | 示例 |
|---|---|---|
| 属性参数 | `_CamelCase` + 类型后缀 | `_BaseColor`, `_Roughness` |
| 顶点输入 | `XxxAttributes` | `PBRAttributes` |
| 顶点输出 | `XxxVaryings` | `PBRVaryings` |
| 顶点着色器 | `Vert` / `Vert_Xxx` | `Vert`, `Vert_Outline` |
| 片元着色器 | `Frag` / `Frag_Xxx` | `Frag`, `Frag_Outline` |

### 3. 结构规则

**① 渲染与管线分离**
- 所有渲染代码（include、纹理、cbuffer、struct、函数）统一放在 `HLSLINCLUDE ... ENDHLSL` 中。
- `SubShader` 只负责 Pass 定义（渲染状态 + 编译指令），不包含函数体。
- 每个 Pass 内的 `HLSLPROGRAM ... ENDHLSL` 仅包含 `#pragma` 指令，不写逻辑代码。

**② 参数用 CBUFFER 管理**
- 所有材质参数统一放在 `CBUFFER_START(UnityPerMaterial) ... CBUFFER_END` 中。
- 与 `Properties` 块一一对应，确保 cbuffer 内字段名、类型与 Properties 一致。

**③ 功能解耦为独立函数**
- 对于未来可能复用或涉及复杂计算的功能，单独拆分为函数管理。
- **拆库裁决（先问"谁会复用"）**：
  - 单效果私有数学/形状 → 私有库与其 shader **同目录**（`RainDrop.hlsl` 先例；可自持 CBUFFER / static const）
  - 跨效果横切 → `Assets/Mine/Special/HLSL/` 共享库（零 CBUFFER、零兄弟 include、调用方 Assets 全路径 include），例如:
    - `RimLightFunction.hlsl` — 边缘光
    - `ShadowFunction.hlsl` — 阴影处理
    - `ENVFunction.hlsl` — 环境反射
    - `PBRFunction.hlsl` — PBR 光照
    - `NormalFunction.hlsl` — 法线计算
    - `LightFunction.hlsl` — 通用光照
  - 完整决策表 → [render 选型节](../../../templates/shader/render/README.md) + [hlsl 三档依赖表](../../../templates/shader/hlsl/README.md)
- 函数命名清晰，避免在一个函数中混合多种职责；库函数带语义模块前缀（`BlendNormal_` / `SDF_` 等），避无前缀通用名（MainLight 重复定义地雷）。

**④ 功能分级（复杂 Shader 专属）**

参考 RainDrops 的分层抽象模式，对复杂效果进行多级变换分解：

```
第1层 — 基础输入    原始数据（UV、时间、噪声）
第2层 — 空间变换    坐标映射、网格划分、扭曲变形（如 RemapUV → RemapGridUV）
第3层 — 核心形状    SDF、遮罩、法线生成（如 DropShape）
第4层 — 编排组合    多实例组合、混合策略（如 DropLayer + BlendLayer）
第5层 — 渲染集成    光照、颜色输出（Frag 中的最终合成）
```

各级职责明确，通过结构体（如 `DropConfig`）传递参数，方便调试和替换任意层级而不影响其他部分。每层对外暴露清晰的接口函数，便于未来扩展新变体时复用已有变换逻辑。

### 4. 文件组织

```
Assets/Mine/Shaders/Render/
  └── <YourEffect>/
      ├── YourEffect.shader        — 主 Shader 文件
      ├── YourEffectFunction.hlsl  — 效果私有库（复杂效果时，与 shader 同目录）
      └── YourEffect.md            — 技术文档（可选；知识要点已内化 references/）

Assets/Mine/Special/HLSL/
  └── XxxFunction.hlsl             — 跨效果共享函数库（零 CBUFFER、Assets 全路径 include）
```

> 注：`Assets/Mine/Special/Shaders/` 旧目录已退役（2026-09-03），复杂效果的私有库一律与其 shader 同目录。

### 5. Pass 定义规范

每个 Pass 包含：
- `Name` — 明确命名
- `Tags { "LightMode" = "..." }` — 指定光照模式
- 渲染状态（Cull / ZWrite / ZTest / Blend）
- `HLSLPROGRAM` 块，仅包含：
  - `#pragma vertex` / `#pragma fragment`
  - `#pragma shader_feature_local`（本地功能开关）
  - `#pragma multi_compile`（全局多编译变体）

### 6. 代码编排细节

- Properties 按功能使用 `[Header]` 和 `[Space]` 分组
- `#include` 顺序：URP 内置库 → 自有功能库
- 纹理声明紧随 `#include` 之后，与对应 `SAMPLER` 成对出现
- struct 字段按语义排列（position → normal → tangent → uv）
- 函数体内部变量声明遵循：先声明、后计算、变量名含义清晰

### 7. 注释规范

**核心原则：Shader 内只作简要功能注释，细节交由 `.md` 文档。**

每个函数（尤其是 Frag / Vert）前使用装饰线分隔块 + 一行简要说明，点名函数职责和实现方式。不展开内部细节、不逐行写注释、不写算法推导。

```
// ════════════════════════════════════════════════════════════
//  <函数名/Pass名> — <一行功能概述>
// ════════════════════════════════════════════════════════════
```

示例（普通材质 Shader）：

```hlsl
// ════════════════════════════════════════════════════════════
//  双边保边模糊 — 调用 BlurFunction.hlsl，关键字控制强度与法线
// ════════════════════════════════════════════════════════════
half4 Frag_BlurH(Varyings input) : SV_Target { ... }
```

注释要素：
- **函数名/Pass名** — 是什么（可选，函数名已自文档时可不重复）
- **一行概述** — 做什么 + 怎么做的关键思路（如"调用 BlurFunction.hlsl"、"DDA 2D 屏幕空间步进"）
- 不包含内部细节（步进次数、采样策略、阈值说明等交 `.md` 文件）

如需补充接口说明（输入/输出），放在同一注释块的第二段，保持简洁：

```hlsl
// ════════════════════════════════════════════════════════════
//  Pass 0: DDA 2D 雷步进 — 屏幕空间阴影追踪
//
//  输出：R=shadow factor, G=avg occluder depth
// ════════════════════════════════════════════════════════════
```

**禁止行为：**
- 不在函数内部逐行写注释（除非逻辑非常反直觉）
- 不在 Shader 中写详细的算法说明、参数取值范围、优化建议——这些都归 `.md`
- 不用散乱的 `// ---` 或 `// =====` 做分隔，统一使用 `// ════════...╗` 包裹块

**对应的 `.md` 文件中应包含：**
- 函数签名与职责
- 算法核心思路（可带公式/图示）
- 参数取值范围与效果说明
- 优化建议与性能分析
- 扩展方向

---
