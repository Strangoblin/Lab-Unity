---
name: unity-render-debug-skill
description: 统一全屏 Shader Debug 与完整相机 PNG 流程，新增共享 skill、取像脚本及 Debug 目录候选
date: 2026-10-10
---

# Unity 渲染调试与截图流程合并

用户要求将 `SingleCameraRequest → RenderTexture → ReadPixels → PNG` 总结为 skill，并合并已有全屏 Shader Debug 流程；随后提出通用 Debug 资源独立归属的问题。

## 实施

- 新建共享 [unity-render-debug](../../../skills/unity-render-debug/SKILL.md)：Feature 真实中间输出、独立全屏材质、Game View 观察与完整相机 PNG 统一入口。
- 细节放 [camera-capture.md](../../unity-developer/references/standard/rendering/camera-capture.md)，可执行取像放 [camera-offscreen-capture.cs](../../unity-developer/scripts/roslyn/camera-offscreen-capture.cs)，不把长代码嵌入 skill。
- 旧 feature-script-structure / fullscreen-structure / shader-validation 等入口改为引用统一流程；撤销 ReadPixels 全面禁用等与受控离屏回读冲突的旧描述。
- skill 单份权威源；Claude 新增相对发现软链，Codex 沿用 skills 目录软链，无平台实体副本。
- 明确 Game/Base 相机、真实 Renderer、单相机与 Stack/UI 边界、PNG 显示证据与线性 HDR 数值边界，以及调用方 / helper 各自恢复职责。
- 独立 forward-test 补入并发字段归属、禁止整份旧快照覆盖、双 Pass 输入 / 索引退回复制、深度有效性及固定执行阶段约定。

## P1–P3 与验证

- 新 skill / reference 均少于 80 行；feature-script-structure 从 116 行精简为 34 行，长骨架引用既有模板。
- 通用 helper 以实际 Unity / URP 程序集离线编译通过：0 warning / 0 error；API 引用及 norm 检查通过。
- 官方 skill quick_validate 通过；Claude / Codex / SSOT 三个入口文件内容一致。
- 架构 strict 通过：无断链、发现副本漂移、hook 漂移、绝对路径或阶段错误。受保护旧输出目录通过工具 OUTPUT 注入改为会话 output，不改测试源。
- 两项 MCP 契约检查经隔离 runner 通过，生产 state / 审计指纹保持不变；本轮未改变 MCP 判定、策略或 hooks。
- 本轮没有操作 Editor 或执行 GPU 截图；历史流程实测与本次新 helper 的离线编译证据分开记录。
- 验证产物位于 `tmp/sessions/2026-10-10-render-debug-skill-8df2/`；输出用于本轮核验，不作为长期权威源码。

## 待确认布局

推荐通用工具集中于 `Assets/Mine/Special/Debug/`，功能自己的 Debug 分支 / Validation 留在功能目录。实际当前 Feature 在 Scripts/Debug，两个 Shader 在 PostProcess/Debug。

精确 [候选清单](../references/debug-resource-layout.md) 包含 9 项移动与 1 项旧目录 meta 删除，保留文件 GUID；尚未移动或删除 Assets。后续按根 AGENTS.md 取得清单确认并由 unity-developer 执行门禁与验证。

未提交本轮改动；并发工作区的其他内容保持原样。
