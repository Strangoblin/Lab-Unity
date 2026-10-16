---
name: 2D 与 3D Shader 验证分支
description: 从 Plant 实践提炼隔离渲染流程，与既有全屏调试并列
date: 2026-09-17
---

# Shader 验证分支

- 在 [shader-validation.md](../../../skills/auto-manager/capabilities/shader-validation.md) 统一路由：2D 使用已有真实相机调试流程，3D 按需使用网格数据检查和隔离 URP 渲染。
- compile/runtime 两个能力入口链接新流程，覆盖现有模式的验证阶段；不新增 MCP 门禁或复制平台正文。
- Plant 实践包括逐面 UV/硬法线检查、实际 GPU 渲染、共同平移数值对照、多视角/投影/变换及 Pass 同步编译。
- 区分已执行检查与效果保证：非空像素仅冒烟检查，阴影编译不代表投影验收，预览不代表真实场景集成。
- 保留既有“截图非必需、视觉质量人工观察”约定；不将 Plant 的专有面数、颜色阈值或脚本固化为通用约束。
- P1-P3：能力文档承载流程，引用现有 2D 正文；具体执行脚本按任务生成到 tmp，不内嵌大段代码。
