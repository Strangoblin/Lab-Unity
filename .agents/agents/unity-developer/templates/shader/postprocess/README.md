# Postprocess Templates

These code templates form the pieces of a fullscreen postprocess feature. This directory keeps only `README.md` as markdown; everything else is a compilable code template body.

| Template | Actual role |
|---|---|
| [fullscreen-postprocess.shader](fullscreen-postprocess.shader) | Fullscreen URP Shader (sole canonical template) |
| [urp-renderpass.cs](urp-renderpass.cs) | Unity 6 RenderGraph postprocess pass (single-pass skeleton; multi-pass as comment) |
| [volume-template.cs](volume-template.cs) | VolumeComponent integration |
| [compute-template.compute](compute-template.compute) | Screen-space depth Compute template |
| [spatial-filter-budget.hlsl](spatial-filter-budget.hlsl) | Blur/SNN/Kuwahara/Resolve 的质量档采样预算与独立半径骨架 |

空间滤波必须遵循 [Spatial Filter Sampling Budget](../../../references/shader/postprocess/spatial-filter-budget.md)：Quality 固定样本数/分布/下采样率，Radius 只缩放 offset，不能进入循环边界。

## 后处理系统文档写作要点

Writing a feature documentation `.md` (`references/shader/postprocess/` has the conventions; script-doc-template.md 已并入本段) — follow this skeleton:

1. **功能概述** — 2-3 句说明用途与整体行为
2. **架构** — 数据流图(输入 → Pass A (MRT) → … → 屏幕输出)+ 类关系表(`XxxFeature` → 面板参数 + Pass 注册 / `XxxPass` → 渲染逻辑 / 其他 MonoBehaviour)
3. **配置参数** — 分 Feature 面板参数表 与 Shader 参数表(参数/类型/默认值/说明)
4. **渲染管线** — Pass 编排表(顺序/Pass 名称/类型/目标/说明)+ RT 规格表(RT/格式/分辨率/生命周期/说明)
5. **使用方式** — 前置条件(管线、Renderer 添加 Feature、场景要求)/ 运行时行为(每帧流程、交互→Readback 更新)/ Debug 模式(枚举视图显示什么)
6. **性能** — 指标表(DrawCall、RT 数量、Readback 延迟、取值上限)
7. **已知限制** — 实测坑(视图缩放、持久化 RT 混入 MRT、边缘不连续等)
8. **扩展点** — 后续能力(深度感知、多选、动画)
9. **字段命名速查** — `public` 无前缀 / `[SerializeField] private` 与 `private` 用 `_` / `static readonly int` (PropertyToID) 用 `s_` 或 PascalCase / 局部变量无前缀

> ⚠️ 文档模板属于 function-independent 层 → `templates/standard/`(`shader-doc-template.md` / `script-doc-template.md`);本特征族不再持有独立 md 模板。
