# GizmoRender 功能回归

在已连接本项目的 Unity Editor 中运行：

```sh
unityctl status
unityctl script execute Assets/Mine/Scripts/GizmoRender/Validation~/GizmoRenderChecks.cs
```

成功返回 JSON：`passed=67`、`failed=0`、`visibilityCases=48`、`knownFailureControls=1`。失败抛出带完整明细的异常，不能仅根据 CLI 有输出判定通过。

覆盖成员 Enabled / Presence / 全局 Policy / 两个回调通道的 48 种组合，空键重复扫描、唯一新增与改名校验、改组清理、Unity 已销毁接口来源、嵌套及异常状态恢复、六种实际组件的接口与回调存在性。一个故意错误的断言检查验证器能够失败。颜色状态断言按 Unity `Gizmos.color` 的 Color32 存储精度比较；BaseColor 仍按浮点精度比较。

使用非持久配置与不发出几何的 Source stub。缓存、EditorPrefs、Gizmos 状态和临时隐藏对象均在 finally 恢复；脚本核验原场景脏状态与意外错误日志。不创建资产，不保存场景或 Renderer，不进入 Play。

验证边界：脚本证明分派、配置键和状态合同；不证明人工窗口点击、图元画面或所有可视化均已迁入。六个真实组件的绘制体由接手差分另行核对，本次仅入口和命名空间替换。

新输出放 `tmp/sessions/<date-task-id>/output/`，本目录只保留验证源码与入口。
