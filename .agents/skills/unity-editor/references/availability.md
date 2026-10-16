# Editor 连接与可用性

`unityctl status` 必须先查。Editor 进程存在和 bridge 存在不等于可通信；以 Unity connected 及等待成功为准。

## 流水线深度

| 状态 | 行为 |
|---|---|
| 已连接 | Research：代码→编译→人工观测；Production：设计→代码→所需编译/运行→收尾；Experiment：Plan→有限编译/验证迭代 |
| 未连接 | 完成独立知识/代码工作，跳过依赖 Editor 的编译/运行并说明原因，不宣称渲染通过 |

## Bridge

与 Editor 相同宿主中执行 `unityctl bridge start`（幂等）；任务确需启动时用 `unityctl editor run`。
`unityctl wait --timeout 60` 等待连接，保持单次阻塞不超过 60 秒；更长连接观察分段执行并给进度。

| 现象 | 处理 |
|---|---|
| 编译后断连 | domain reload，可等待自动重连 |
| 命令超时 | `unityctl dialog list` 检查阻塞对话框；不能默认 dismiss 用户对话框 |
| Bridge 断开 | 核对宿主后按任务范围依次 bridge stop / bridge start |
| Project not found | unityctl setup 或 config set project-path <实际项目> |
| Bridge 健康但未连接 | 先考虑现有 Editor 脚本 reimport 触发 DidReloadScripts；不立即重启 Editor |

## 宿主隔离

Bridge 与 Editor 必须在同一宿主、进程/网络命名空间。
命令侧 Socket permission denied、stale bridge 或 Unity not connected，而 Editor 实际运行时，先确认执行环境；不要误判插件缺失，也不在错误宿主反复启动 bridge。
在 Editor 同宿主核对 `unityctl bridge status` / `unityctl status`，必要时重启 bridge 并分段等待。
关闭/重启 Editor 只在任务授权且已确认没有会丢失的未保存内容时执行。
`editor stop` / close / kill 是停止进程；退出播放模式使用 `play exit`。

## 验证工具

| 目标 | 工具 |
|---|---|
| 层级/组件/属性 | snapshot --components / --filter |
| UI 屏幕位置 | snapshot --screen |
| 运行行为/错误 | logs / logs --stack |
| 值或状态 | script eval |
| 测试结果 | test run |

超过 60s 无进展或持续断连时报告状态，停止依赖步骤。CLI 签名见 [命令索引](cli/README.md)。
