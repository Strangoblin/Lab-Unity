# unityctl Command Reference

> Unity Editor 远程控制 CLI。完整命令层级参考。

---

## 命令层级

### Status & Bridge

```bash
unityctl status              # 检查 Unity、bridge、连接状态
unityctl bridge start/stop   # 管理 bridge 守护进程
```

### Editor Lifecycle

```bash
unityctl editor run/stop     # 启动或停止 Unity Editor
unityctl wait                # 阻塞等待 Unity 连接（最长 120s）
unityctl wait --timeout 300  # 自定义超时
```

> ⚠️ `editor stop`（别名 `close/kill`）终止的是 **Editor 进程本身**，不是退出播放模式——
> 有未保存内容时不可逆。退出播放模式用 `play exit`。

### Compilation

```bash
unityctl asset refresh       # 编译脚本，失败时返回错误
```

### Play Mode

```bash
unityctl play enter/exit     # 进入/退出 Play Mode
unityctl play pause          # 切换暂停（Edit Mode 也可用——预置 play 时暂停）
unityctl play step           # 前进一帧（仅 Play Mode）
```

### Screenshots

```bash
unityctl screenshot window GameView <path>   # ✅ 取 Game View 窗口（Edit Mode 亦可）
unityctl screenshot list-windows             # 列出可截的 Editor 窗口
unityctl screenshot capture [path]           # ❌ 不可靠，改用 window，见下
```

**`capture` 不可靠，不要用**（2026-09-20 实机复现，5 次尝试）：

- **落盘不可靠**：5 次只成功 1 次，且成功那次也在 `Warning: screenshot file not written after 5s` 之后才出现。
- **报出的 Resolution 是垃圾值**：`2798x72` / `1288x1138`，而真正写出的文件是 **642×522** —— 报的尺寸与产物不是同一张图。
- **丢弃传入目录**：传 `/tmp/x.png`，真实落点是 `<project>/Screenshots/x.png`（只保留 basename）。
  而 stdout 的 `Screenshot captured: <path>` **回显的是你的输入、不是真实落点**。

需要画面时用 `window GameView`：路径与尺寸都正确，且 Edit Mode 下即可用。

> 项目约定：视觉结果由人工在 Game View 观察，截图仅用于确实需要留档的场景。

### Logs & Diagnostics

```bash
unityctl logs                # 显示自上次清除以来的日志（编译/play 时自动清除）
unityctl logs -n 50          # 限制条目数
unityctl logs --stack        # 包含堆栈
unityctl logs --full         # 忽略清除边界
```

### Scenes

```bash
unityctl scene list          # 列出场景
unityctl scene load <path>   # 加载场景
unityctl scene load <path> --additive  # 加性加载
```

### Script Execution

```bash
# 直接 eval C# 表达式
unityctl script eval 'Application.version'
unityctl script eval 'GameObject.FindObjectsOfType<Camera>().Length'
unityctl script eval --id -1290 'target.transform.position'
unityctl script eval -u UnityEngine.SceneManagement 'SceneManager.GetActiveScene().name'

# 执行 .cs 文件
unityctl script execute /tmp/MyScript.cs
unityctl script execute /tmp/SpawnObjects.cs -- Cube 5 'My Object'

> ⚠️ `script execute <file>` **忽略 `-u`**，且只自动注入 `System.Text` 与 `UnityEngine`。
> 需要 `System.Reflection` / `Resources` / `UnityEngine.Rendering.Universal` 等时必然编译失败。
> 多程序集需求一律改用 `-u`：
>
> ```bash
> unityctl script eval -t 300 \
>   -u System -u System.Text -u System.IO -u System.Reflection -u System.Threading.Tasks \
>   -u UnityEngine -u UnityEngine.Rendering -u UnityEngine.Rendering.Universal \
>   "$(cat /tmp/MyScript.cs)"
> ```
>
> 另：eval 环境是反射注入的独立程序集，**`internal` 成员不可达**（只能内联常量）；
> 带 lambda / 局部函数的内插表达式与 `x.Sum(p => ...)` 会被拒绝。

# 超时控制（默认 30s）
unityctl script eval -t 600 -u UnityEditor 'return BuildPipeline.BuildPlayer(opts).summary.result.ToString();'

# 异步
unityctl script eval 'await Task.Delay(500); return GameObject.Find("Boss") != null;'

# 类型查找
unityctl script lookup-type <Name>
unityctl script members <Type> [--filter X] [--static]
```

### Scene Snapshot

```bash
unityctl snapshot                          # 场景层级树（默认深度 2）
unityctl snapshot --depth 4                # 更深遍历
unityctl snapshot --id 14200 --components  # 展开一个物体及所有属性
unityctl snapshot --screen                 # 包含屏幕空间边界和可见性
unityctl snapshot --filter "type:Rigidbody"  # 过滤
unityctl snapshot query 400 300            # 屏幕坐标 (400,300) 处是什么 UI 元素
```

### UI Interaction (Play Mode only)

```bash
unityctl ui click --name "StartButton"    # 按名称查找并点击（推荐）
unityctl ui click --id 14200              # 按 instance ID 点击
unityctl ui click 400 300                 # 按屏幕坐标点击
```

### Prefab Editing

```bash
unityctl prefab open Assets/Prefabs/Player.prefab
unityctl prefab close / close --save / close --discard
```

### Dialog Detection

```bash
unityctl dialog list                   # 列出检测到的弹出对话框
unityctl dialog dismiss                # 关闭第一个对话框
unityctl dialog dismiss --button "OK"  # 点击特定按钮
```

---

## 典型工作流

```bash
unityctl asset refresh       # 编辑 C# 后编译
unityctl snapshot            # 验证场景状态（结构化、低成本）
unityctl play enter
unityctl snapshot            # 运行时状态检查
unityctl logs                # 检查错误/警告
unityctl play exit
# 视觉效果由人工在 Game 视图确认
```

## 故障排除

| 问题 | 解决方案 |
|------|---------|
| Bridge 无响应 | `unityctl bridge stop && unityctl bridge start` |
| Editor 未连接 | 正常 — 指数退避，最长 15 秒 |
| 编译后连接断开 | 正常 — domain reload，自动重连 |
| "Project not found" | `unityctl setup` 或 `unityctl config set project-path <path>` |
| 不确定 Unity 何时就绪 | `unityctl wait --timeout 300` |
| 命令超时 | 可能原生对话框阻塞：`unityctl dialog list` |
| 进度条卡住 | `unityctl dialog list` 检查，等待或关闭 |

## 宿主隔离故障处理

`unityctl bridge` 与 Unity Editor 必须运行在同一宿主、进程/网络命名空间中。沙盒环境与真实宿主环境之间的 bridge **不互通**：沙盒内启动的 bridge 不能连接真实环境中的 Editor，真实环境中的 bridge 也不能由沙盒命令当作可用连接复用。

因此，如果普通命令环境报告 `Socket permission denied`、stale bridge，或 `Unity not connected`，但 Unity Editor 正在运行，应先判断命令与 Editor 是否位于不同执行环境，而不是诊断为插件缺失。`unityctl status` 同时显示 Editor/bridge 存在也不代表二者可通信，最终以 `Unity connected` 和 `unityctl wait` 成功为准。

在与 Unity Editor 相同的宿主环境中执行并保持 bridge：

```bash
cd <project-root>
unityctl bridge stop
unityctl bridge start
unityctl bridge status
unityctl status
```

如果 bridge 健康但 `Unity Connected: False`，不要立即重启 Editor；先触发一次已有 Editor C# 文件的重新导入以执行 `[DidReloadScripts]` 重连，然后使用 `unityctl wait --timeout 60` 验证。关闭 Editor 只应在确认没有未保存内容后进行。

## 最佳实践

1. **结构化优先**：能用 `snapshot`、`logs`、`script eval` 验证的优先结构化；画面质量由人工在 Editor 观测
2. **快照优于评估**：用 `snapshot` 观察场景，`ui click` 交互，`eval --id` 定制操作
3. **名称优先于 ID**：`--name` 比 `--id` 更稳定（instance ID 在 Play Mode 间会变）
4. **总是用 Write 工具创建 .cs 文件**：不用 shell heredoc（在 C# 单引号处会断）
5. **取像用 `screenshot window GameView`**：`capture` 不可靠（见 Screenshots 段）
