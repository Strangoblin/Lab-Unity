# Snowy 早期原型历史证据

2026-09-07 预览图和同批旧参数检查源码，加上后续 flip/motion 阶段的旧源码与材质基线。
背景见 `2026-09-20-snowy-wrapup.md`：最终 Snowy 已变为噪声雪斑/气氛双 Pass，
再由 Weather 整合。本目录保留前因与迭代状态，不作为当前 Snowy/Weather 验证结果。
旧 Shader 属性、`SnowyParticle.hlsl` 与 Renderer 配置已变化；检查 .cs 仅历史文本，不能直接运行。
依赖当时 Unity 6、URP、Snowy 资源与相机状态；本轮只做原字节归档校验，没有运行 Editor。
完整正式历史基线由 Weather `Validation~/Legacy/` 的现存说明管理，勿把此树重新导入 Assets。
