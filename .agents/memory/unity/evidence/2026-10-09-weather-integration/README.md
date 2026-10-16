# Weather 整合阶段历史证据

`plan/` 是 2026-10-08 的需求、设计、兼容契约和阶段清单。
`implementation/` 保留 10-08～10-09 独有的暂存文档、实现文本、旧 Renderer 基线与迁移记录。
用于追溯 Rain 去 blur、三种旧算法合并、Profile 与 Feature 接线及当时验证边界。
这是阶段历史，不是同功能的第二个可编辑源。plan 中的未勾选项原样保留，不能由日期推断已验证。

当前执行入口：`Assets/Mine/Shaders/PostProcess/Weather/VALIDATION.md` 及同功能 `Validation~/`；
当前代码集中在 Weather 功能目录；旧 `Assets/Mine/Scripts/Weather/` 已退役。
运行当前验证仍需 Unity 6 / URP 17、该项目资源与 unityctl；本历史暂存树不宣称可直接运行。
独有 C#/HLSL 是历史文本，依赖旧属性、GUID、Renderer 状态，禁止作为复用工具或重新导入 Assets。
localUV 的 before/work/运行数据对应当前 dirty 工作，不纳入本次归档和清理。
