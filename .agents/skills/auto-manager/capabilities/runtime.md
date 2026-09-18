# runtime — 运行时验证

> 按验证目标选择静态渲染或 Play Mode，并检查日志。

## Shader 验证分支

Shader 任务先按 [2D / 3D 渲染检查](shader-validation.md) 选择验证载体。静态 3D 材质可在隔离预览场景验证；需要帧间行为或运行时初始化时再执行下述 Play Mode 流程。2D 后处理保留真实相机管线检查。

---

## 命令

```bash
unityctl play enter            # 进入 Play Mode
sleep 3                         # 等待模拟运行几帧
unityctl logs -n 20             # 检查日志
unityctl play exit              # 退出 Play Mode
```

## 日志获取

```bash
unityctl logs -n 30             # 最近 30 条
unityctl logs --stack           # 带堆栈跟踪
```

> 日志在进入 Play Mode 后自动清除，所以只显示当前会话日志。

## 自动诊断规则

| 日志模式 | 自动处理 |
|---------|---------|
| `NullReferenceException` | 定位空引用 → 检查序列化/域重载 → 修复 |
| `Thread group size must be above zero` | 检查 kernel index 是否有效 → 打印调试信息 |
| `error CSxxxx` | 编译错误 → 读文件 → 修复语法 |
| 无日志输出 | 属性缺少 `[SerializeField]` 或初始化守卫错误 |

> 研发模式下：仅报告诊断建议，**不自动修复**，等待人工决策。
> 生产模式下：自动修复，同一错误连续 3 次失败后兜底退出。

## 引用

- 错误诊断详情：[../../../rules/shader-development.md](../../../rules/shader-development.md)
