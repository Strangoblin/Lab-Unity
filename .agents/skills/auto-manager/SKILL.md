---
name: autoagent
description: >-
  Route Unity tasks through one project entry: development workflows, standalone
  Editor operations, rendering checks, reuse analysis or read-only consultation.
  Use for Unity code, shaders, assets, refactoring, debugging and verification;
  exclude .agents/.mcp/.claude/.codex system maintenance.
user-invocable: true
argument-hint: "<task description>"
---

# AutoAgent

Unity 任务的统一分流入口。外部角色与索引到本文件；本 skill 再选择开发流程或专门能力，不要求预读全部内部资源。
agent 提供领域判断、上下文取用与 memory 记录；体系维护由 agent-maintenance 承担。

## 先按任务分流

按最具体的当前目标选择分支；已有对照报告的判读走耗时对照，普通事实咨询才取知识。

| 当前目标 | 下一节点 |
|---|---|
| 新增 / 修改代码、Shader、资产或功能文档，修复、调参、结构重构 | [开发流程](AutoMode.md) |
| 独立执行 Editor 查询、编译、运行、测试或构建 | [unity-editor](../unity-editor/SKILL.md) |
| 独立查看中间输出、获取完整相机 PNG | [unity-render-debug](../unity-render-debug/SKILL.md) |
| 采集渲染耗时，或判读已有对照报告 | [unity-render-benchmark](../unity-render-benchmark/SKILL.md) |
| 只比较已有实现与行为契约，形成复用结论 | [unity-feature-reuse](../unity-feature-reuse/SKILL.md) |
| 创建或维护 unityctl 插件 | [unityctl-plugins](../unityctl-plugins/SKILL.md) |
| 只解释事实、查看实现或已有记录，不实施改动 | 按需取用 [Unity 知识](../../knowledge/unity/README.md) 或 [领域记忆](../../memory/unity/MEMORY.md) |

混合任务以最终交付为准：包含功能实施或修复时进入开发流程，由选定模式调用所需专门能力。
独立操作与只读分析不初始化完整开发 recipe、不运行通用设计检查；已有脚本、截图或报告按对应 skill 的实际需要取用。

专门操作若需要新增 / 修改受管源码、任务脚本、适配器或持久资产，进入开发流程后实施，原操作目标作为验证。
分流不授予写入、场景 / Renderer 修改、Play 或重启权限；仍遵守实际执行 skill 的授权与通道约束。

用户已明确选择专门 skill，或当前任务已完成分流时，沿用该分支；内部资源协作不反跳本入口，不重新初始化 recipe。
任务从查询 / 操作升级为实施时只补齐开发分支；同一任务已有判断与上下文直接复用。

## 资源归属

模式、能力与模板由开发节点按需取用；知识与模板之间的契约引用、专门 skill 之间的实际执行依赖可以直接连接。
外部 Unity 任务入口指向本文件，不从角色或总清单直接下钻 modes / capabilities / templates。
临时产物按 [会话产物规范](../../knowledge/meta/interfaces/task-artifacts.md) 归属，实际结果交角色记录。
