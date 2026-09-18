---
paths:
  - "Assets/Mine/Shaders/**"
  - "**/*.shader"
  - "**/*.shadergraph"
  - "**/*.hlsl"
---
# Shader 开发规范

> 完整结构规范见 [references/standard/shader/shader-structure.md](../agents/unity-developer/references/standard/shader/shader-structure.md) and [references/shader/postprocess/fullscreen-structure.md](../agents/unity-developer/references/shader/postprocess/fullscreen-structure.md)（2026-08-24 内化，归属 unity-developer）

## Include 顺序（固定，不可调换）

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
// 项目自定义 include 放最后
```

- **依赖前置**：自定义 include 内引用的全局（`#define` 常量、`static const` 数组、CBUFFER 参数）必须在 include 行**之前**声明——`#include` 是线性文本展开，后置定义会报 `undeclared identifier`（2026-08-27 StarryNight 实坑：SDF_POINT_COUNT）
- **调用先于定义**：HLSL 函数调用者必须先于被调用者定义——Unity/Metal 编译管线不支持下向声明，前向引用报 `undeclared identifier`（2026-09-01 StarryNight 实坑：ComputeRowColor 调用文件后部定义的 Hash2 → 编译失败，修复 = Hash/Hash2 置于文件顶部）

## 全屏后处理必须项

- `Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }`
- `Cull Off ZWrite Off ZTest Always`
- 基础全屏模板使用 `#pragma target 2.0`；额外功能按实际需求提高
- 纹理采样用 `SAMPLE_TEXTURE2D_X`，非 `SAMPLE_TEXTURE2D`
- 纹理声明用 `TEXTURE2D_X`，非 `TEXTURE2D`
- 输入纹理名为 `_BlitTexture`，非旧版 `_MainTex`
- Vertex shader 用 Blit.hlsl 提供的 `Vert()`，不手写

## Metal 兼容

- 所有 vertex output 字段必须显式初始化
- 显式声明 `#pragma target`；网格材质模板默认 3.0，按功能和目标平台验证
- 不在 frag shader 中大量使用 `clip()`（会导致 GPU 崩溃）

## 项目约定

- 文件头必须有 `// ═══` 分隔注释块
- Pass 命名用 PascalCase，与功能对应
- 公共函数/结构体提取到 `.hlsl` 文件，不复制粘贴

## Shader 菜单路径命名（Assets/Mine）

- ShaderLab 声明统一为 `Shader "父文件夹/Shader名"`；Shader 名取文件名（不含扩展名），路径仅两段。
- “父文件夹”按下表取所属分类或功能目录，不机械使用紧邻文件的效果子目录。

| 资源位置 | 菜单路径示例 / 规则 |
|---|---|
| `Shaders/Render/**.shader` | `Render/PBRToon`（包括更深的子目录） |
| `Shaders/PostProcess/**.shader` | `PostProcess/SSO` |
| `Shaders/**.shadergraph` | `Graph/RimToon`；与手写 Shader 分类分开以避免重名 |
| `Effects/<功能>/**` | `Stars/StarsOutline`、`Fire1/2DFire` |
| `Scripts/**`、`Special/**` 中的 Shader | 取直接父文件夹；父目录为 `Shaders` 时取上一级功能目录，如 `Picker/Picker`、`InteractionManager/InteractorObject`、`Boid/BoidInstance` |

- Shader Graph 的 `m_Path` 只填写前缀，Unity 自动拼接资源名；子图和 Compute 不使用 Shader 菜单路径。
- 不加 `Mine/`、`Custom/`、`Unlit/` 或 `Hidden/` 前缀；内部工具 Shader 同样按功能目录命名。
- 新增或重命名时检查完整名称唯一；同步 `Shader.Find`、`UsePass`、`Fallback` 及文档中的名称引用。Unity/包内置 Shader 引用保留原名。
- 菜单路径调整不移动资源、不改 `.meta` 或 GUID；空文件和历史备份不参与批量重命名。

## ddx/ddy 基底陷阱

- **铁律**：同一个 Jacobian 的所有项必须**同基底**。屏幕基底（ddx/ddy）与固定步长差分（世界基底）混用 → 相机因子约不掉，随视距/视角明暗漂移
- 详情（症状/做法/分析）：[memory/2026-08-04-water-caustics-screen-independent.md](../agents/unity-developer/memory/2026-08-04-water-caustics-screen-independent.md)

## UV 各向异性陷阱

- **铁律**：UV 空间在非方形屏幕上**不是各向同性空间**——同为 0.1 的 u 增量与 v 增量像素长度不等。直接在其中做 R/S，正方形会被剪切、尺寸会被 aspect 拉伸
- **做法**：R/S 移入等比空间（`u * aspect`）执行，T 留在 UV 空间（`translation` 是位置而非距离，角到角位移语义不受 aspect 影响），出栈再除回来
- **验收**：解析前向映射算面片四角，量边长比与相邻边点积。`L1/L2 = 1 且 cos(e1,e2) = 0` 才是刚体旋转的正方形；`cos ≠ 0` = 剪切，`L1/L2 ≠ 1` = 拉伸。**不要用二阶矩特征值判形状**——正方形与长方形的特征值简并，量出的边长比恒为 1.0，会掩盖错误
- **逆映射语义**：采样是逐像素反查，可见物体 = 正变换作用在单位正方形上，故传入参数描述的是**物体**姿态；`(T·R·S)⁻¹ = S⁻¹·R⁻¹·T⁻¹` 顺序是反的，不能靠取负调用正变换
- 复用入口：`Assets/Mine/Special/HLSL/TRS.hlsl` 的 `TRS2D_TransformUV` / `TRS2D_InverseTransformUV`。该库 2D 接口**一律内建 aspect**，不保留无 aspect 版本——那是踩坑入口，不是选项
- 详情（症状/做法/验证数据）：[memory/2026-09-17-uv-aspect-anisotropy.md](../agents/unity-developer/memory/2026-09-17-uv-aspect-anisotropy.md)

## 常见错误诊断

| 症状 | 可能原因 | 诊断 |
|------|---------|------|
| 渲染无效果 | Shader 未绑定 / RenderGraph 纹理未连接 | 检查 Material.SetShader / builder.UseTexture |
| `_BlitTexture` 采样全黑 | 忘记 include Blit.hlsl | 检查 `#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"` |
| Metal 编译失败 | 缺少 `#pragma target 2.0` | 加在 HLSLPROGRAM 内第一行 |
| GPU crash | frag 中大量 `clip()` | Metal 上避免，用 alpha 替代 |
| GetShaderMessages 返回上次编译缓存 | 写入后编辑器尚未重编译 | 等自动导入完成后查询，或先 AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate) 再查——否则旧编译错误会被误判为"干净"（2026-09-01 StarryNight 实坑） |
| 面片旋转/缩放后被剪切或拉长 | UV 空间各向异性，R/S 放错空间 | 见「UV 各向异性陷阱」；改用 `TRS2D_InverseTransformUV` 或把 R/S 移入 `u * aspect` 等比空间 |
| 效果跑在半分辨率 RT 时形状仍不对 | `_ScreenParams` 是相机目标尺寸，非 RT 尺寸 | aspect 改从 RT 自身尺寸取（URP 声明于 `ShaderLibrary/UnityInput.hlsl`，非内置 CG 全局） |
