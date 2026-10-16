---
name: 对齐 2D Debug 验证实际入口
description: 补齐 PostProcess/Debug 资源与材质优先的固定 Feature 链路
date: 2026-09-17
---

# 2D Debug 验证入口纠正

- 用户提醒 2D 调试已复用 `Assets/Mine/Shaders/PostProcess/Debug/`；核对实际 Shader、DebugOutputFeature.cs 与其文档。
- 纠正 [屏幕调试方法](../../unity-developer/references/shader/postprocess/feature-script-structure.md)：区分 Debug Shader 资源目录和固定 Feature 执行器，材质优先、Shader 默认参数回退。
- shader-validation 与 fullscreen-structure 仅路由到上述正文，避免复制细则；保留真实相机管线，不引入 2D 离屏替代流程。
- 不修改业务代码；平台适配通过共享软链同步。
