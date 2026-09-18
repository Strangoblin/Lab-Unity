# compile — 编译验证

> `unityctl asset refresh` + 自动修复循环。

---

## 快速编译

```bash
unityctl asset refresh
# 输出：compilation succeeded / compilation failed
# 失败时会列出具体错误文件、行号、错误码
```

## 编译模式

### 快速模式（研发用）

编译一次，报错即停，**不自动修复**。等待人工分析错误。

```
unityctl asset refresh
  ├── compilation succeeded → 继续下一步
  └── compilation failed → 报告错误文件+行号 → ⏸️ 暂停，等待人工
```

### 完整模式（生产用）

编译失败后自动读取错误、定位文件、修复、重新编译。

```
unityctl asset refresh
  ├── compilation succeeded → 继续下一步
  └── compilation failed
        ├── 读取错误列表
        ├── 定位错误文件和行号
        ├── 自动修复（最多 3 次）
        └── 重新编译
              ├── 成功 → 继续
              └── 3 次后仍失败 → 暂停，报告 mode 层处理退出
```

## 自动修复策略

| 错误类型 | 自动处理 |
|---------|---------|
| `error CSxxxx` 语法错误 | 读取文件 → 修复语法 |
| `error CS0246` 找不到类型 | 检查 using / 命名空间 → 补充引用 |
| `error CS0103` 名称不存在 | 检查变量/方法声明 → 修正拼写或补充声明 |
| 其他未知错误 | 报告 → 暂停（不盲目修复） |

## 引用

- Shader 编译后的 [2D / 3D 渲染检查](shader-validation.md)：区分资源刷新、Pass 编译和实际 GPU 输出。
- 错误诊断详情：[../../../rules/shader-development.md](../../../rules/shader-development.md)
