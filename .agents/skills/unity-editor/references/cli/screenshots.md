# 窗口截图限制

使用 `unityctl screenshot window GameView <path>` 取 Editor 窗口，或 list-windows 查可截窗口。
完整、固定分辨率相机图按 [unity-render-debug](../../../unity-render-debug/SKILL.md) 的 SingleCameraRequest 流程。

`unityctl screenshot capture [path]` 不可靠（2026-09-20 实机 5 次尝试）：

- 5 次仅 1 次落盘，且在 5s 未写文件 warning 后出现。
- 返回 Resolution 与实际 PNG 不符；stdout 回显输入路径不代表真实产物路径。
- 传入目录被丢弃，文件可能落在项目 Screenshots 下，仅保留 basename。

需要截图时检查实际产物与尺寸；不以命令成功证明视觉质量。
窗口/UI 与相机渲染图的目的不同，按目标选工具。视觉效果由人工在 Editor 观察，只有留档或受控对照需要截图。
