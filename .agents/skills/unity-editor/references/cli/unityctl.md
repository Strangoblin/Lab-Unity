# unityctl 命令参考

按任务选命令，不自动串跑整套命令。连接/故障处理见 [availability](../availability.md)。

| 类别 | 命令签名 |
|---|---|
| 状态 | unityctl status；unityctl bridge status |
| Bridge | unityctl bridge start / stop |
| Editor 进程 | unityctl editor run / stop；unityctl wait --timeout 60 |
| 编译 | unityctl asset refresh |
| Play | unityctl play enter / exit / pause / step |
| 日志 | unityctl logs [-n 50] [--stack] [--full] |
| 场景 | unityctl scene list；unityctl scene load <path> [--additive] |
| 快照 | unityctl snapshot [--depth 4] [--id 14200 --components] [--screen] [--filter "type:Rigidbody"] |
| UI 点位 | unityctl snapshot query 400 300 |
| UI 操作（Play） | unityctl ui click --name "StartButton" / --id 14200 / 400 300 |
| Prefab | unityctl prefab open <path>；unityctl prefab close [--save / --discard] |
| 对话框 | unityctl dialog list；unityctl dialog dismiss [--button "OK"] |
| C# | unityctl script eval / execute / lookup-type / members，见 [roslyn](roslyn.md) |
| 窗口图 | unityctl screenshot window GameView <path>；unityctl screenshot list-windows |

`editor stop`（别名 close/kill）终止 Editor，有未保存内容会丢失；退出播放用 play exit。
`play step` 仅 Play 可用；play pause 可在 Edit Mode 预置暂停。
logs 默认显示自上次编译/play 清除边界以来的记录，--full 忽略该边界；不要把旧日志当本次结果。
实例 ID 会跨 Play 改变；能按名称定位则优先名称。

窗口截图的 capture 不可靠，详见 [截图限制](screenshots.md)。完整相机 PNG 用 [unity-render-debug](../../../unity-render-debug/SKILL.md)；耗时用 [unity-render-benchmark](../../../unity-render-benchmark/SKILL.md)。
