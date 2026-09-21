> This file contains function-independent Script structure guidance.
> Feature / RenderPass integration is documented under `references/shader/postprocess/`.

# Standard Script Structure

> 内化自 Assets/MarkDowns/（2026-08-24，meta-developer 迁移，路径已修正为项目根相对）
> 记录日期: 2026-06-15
> 参考实现: [CamController.cs](Assets/Mine/Scripts/CamController/CamController.cs)
> Script template families: [../../../templates/script/README.md](../../../templates/script/README.md)
> Shared style reference: [shader-structure.md](../shader/shader-structure.md)

---

## 普通 MonoBehaviour 编写结构参考

适用于常规组件脚本（游戏逻辑、控制器、工具类等），核心原则：**结构分明、字段分组、职责单一**。

### 1. 整体布局

```
using 声明                       —— UnityEngine → 其他命名空间 → 项目内部
namespace Mine.XXX                —— 项目级命名空间
{
    /// <summary>                  —— XML 文档注释（类职责概述）
    /// ...
    /// </summary>
    /// <remarks>                  —— 可选：附加说明（层级结构、使用前提）
    /// ...
    /// </remarks>
    public class XxxController : MonoBehaviour
    {
        // ── 分组标题 ──────────────────────────────────────

        [Header("分组名")]
        [SerializeField] private Type _fieldName;
        ...

        private Type _privateField;

        // ════════════════════════════════════════════════════════════
        //  方法区块标题 — 一行概述
        // ════════════════════════════════════════════════════════════

        private void MethodName() { ... }
    }
}
```

### 2. 命名约定

| 类型 | 命名模式 | 示例 |
|------|----------|------|
| 类名 | PascalCase + 职责后缀 | `CameraRigController`, `FurRenderer` |
| 命名空间 | `Mine.功能分组` | `Mine.CamController` |
| public 字段 | PascalCase | 一般不使用（用 SerializeField private） |
| SerializeField private | `_camelCase` | `_yawPivot`, `_mouseSensitivity` |
| 非序列化 private | `_camelCase` | `_cam`, `_yawAngle` |
| 方法名 | PascalCase | `HandleRotation`, `LockCursor` |
| 局部变量 | camelCase | `horizontal`, `move` |
| 常量 / static readonly | PascalCase 或 `k_PascalCase` | `PropertyToID`, `k_StepSizeID` |

### 3. 结构规则

**① 字段按可见性与用途分区**

Inspector 暴露字段在上，运行时私有字段在下，按 `[Header]` 分组：

```csharp
[Header("层级引用")]
[SerializeField] private Transform _yawPivot;

[Header("旋转")]
[SerializeField] private float _mouseSensitivity = 2f;

// 私有运行时字段在分组下方
private Camera _cam;
private float  _yawAngle;
```

分组不只是排版。**效果参数的分组有固定语义** —— 按 Technical / Performance / Artistic /
Debug 分层，其中 Performance 必须划档位而非逐个暴露。判据见
[parameter-panel.md](parameter-panel.md)。

**② 方法按功能区块组织**

每个区块用 `// ════════════...══` 包裹块分隔，内含区块标题 + 一行概述：

```csharp
// ════════════════════════════════════════════════════════════
//  双层级旋转 — 第一层 Yaw（世界 Y）/ 第二层 Pitch（局部 X）
// ════════════════════════════════════════════════════════════
private void HandleRotation() { ... }
```

区块顺序：生命周期（Awake/Start/Update）→ 初始化辅助 → 功能模块（按调用顺序）。

**③ 单个方法职责单一**

每个方法只做一件事。复杂流程通过多个小方法的调用链完成，而不是一个大方法：

```csharp
// ✓ 拆分为独立步骤
private void Update()
{
    HandleCursorInput();
    HandleRotation();
    HandleMovement();
}

// ✗ 避免：单个 Update 包含所有逻辑
```

**④ 优先使用新 Input System**

项目启用 Input System Package 时，所有输入读取通过 `UnityEngine.InputSystem` 命名空间：

```csharp
using UnityEngine.InputSystem;

// 鼠标
Mouse.current.delta.x.ReadValue()
Mouse.current.leftButton.wasPressedThisFrame

// 键盘
Keyboard.current.wKey.isPressed
Keyboard.current.escapeKey.wasPressedThisFrame
```

不使用旧版 `Input.GetAxis` / `Input.GetKeyDown`，除非项目设为 Both 模式。

### 4. 文件组织

```
Assets/Mine/Scripts/
  └── 功能分组/
      ├── XxxController.cs       — 主脚本
      └── XxxController.md       — 技术文档（参数、架构、使用方式、扩展点）

agents/unity-developer/references/standard/script/
  └── script-structure.md        — 本文件（全局脚本规范，2026-08-24 内化，归入 unity-developer）
```

与 Shader 组织保持一致：代码与文档同目录，全局规范内化于 `agents/unity-developer/references/`。

### 5. 代码编排细节

- `using` 顺序：`UnityEngine` → `UnityEngine.*` 子系统 → 项目命名空间
- 字段声明：对齐类型与变量名，提高可读性（`private Camera _cam;` / `private float  _yawAngle;`）
- 局部变量遵循：先声明、后计算、变量名含义清晰
- 避免魔法数字：有意义的阈值抽为常量或 SerializeField
- `[Range]` 标注角度、比例等有物理上下限的字段

### 6. 注释规范

**核心原则：代码内只作区块分隔与简要说明，细节交由 `.md` 文档。**

**区块注释：**

使用 `// ════════════...══` 分隔线包裹区块，含区块名称 + 一行职责概述：

```csharp
// ════════════════════════════════════════════════════════════
//  双层级旋转 — 第一层 Yaw（世界 Y）/ 第二层 Pitch（局部 X）
// ════════════════════════════════════════════════════════════
```

注释要素：
- **区块名称** — 是什么功能模块
- **一行概述** — 做什么 + 关键实现思路

**XML 文档注释：**

类级别使用 `<summary>` 说明职责，`<remarks>` 补充结构或使用前提：

```csharp
/// <summary>
/// 相机控制系统：双层级旋转 + WASD 局部空间移动。
/// 挂载于 CameraRig 根节点。
/// </summary>
///
/// <remarks>
/// 层级结构：
/// CameraRig (本脚本)
/// └── YawPivot   — 第一层，绕世界 Y 轴水平旋转
///     └── PitchPivot — 第二层，绕局部 X 轴垂直旋转
///         └── Main Camera
/// </remarks>
```

**禁止行为：**
- 不在方法内部逐行写注释（除非逻辑非常反直觉）
- 不在代码中写详细的参数说明、算法推导——这些都归 `.md`
- 不用散乱的 `// ---` 或 `// =====` 做分隔，统一使用 `// ════════...══` 包裹块
- 不用 `#region`（与区块注释线冲突，风格不统一）

**对应的 `.md` 文件中应包含：**
- 参数表（名称、类型、默认值、说明）
- 输入映射（键位→行为）
- 架构设计思路（为什么这样分层的理由）
- 关键实现细节
- 使用方式
- 扩展方向

---
## 静态工具类脚本结构参考

适用于纯计算、无状态的工具类（如噪声生成、数学库）。

### 整体布局

```csharp
using UnityEngine;

public static class XxxUtility
{
    public enum SomeEnum { A, B }

    // ── 公开 API ──

    public static ResultType PublicMethod(...) { ... }

    // ── 私有辅助 ──

    private static float HelperMethod(...) { ... }

    // ── 常量 / 查找表 ──

    private static readonly int[] _perm = { ... };
}
```

- 使用 `static class`
- 公开 API 在上，私有实现在下
- 大型查找表放在文件末尾
