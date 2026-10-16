# CurveGenerator — 曲线预计算工具

**路径:** `Assets/Mine/Scripts/CurveGenerator/`
**类型:** 编辑器工具 + 运行时数据资产
**依赖:** `CurveAsset` (ScriptableObject), `CurveGeneratorWindow` (`EditorWindow`)

---

## 功能概述

将用户指定父物体下的**直系子物体**作为控制点，烘焙为均匀采样的曲线数据资产（`CurveAsset`），供 GPU 粒子模拟（如轨迹跟随）直接读取。支持 Catmull-Rom 和 Bezier 两种插值，支持 2D 平面约束。

---

## 架构

```
CurveGeneratorWindow (Editor, Tools → Curve Generator...)
  │  GUI: Control Parent 字段 + 曲线类型/维度/采样数/Loop + Output 路径
  │  预览: 读取父物体直系子物体 → SceneView Handles 绘制控制点 + 曲线 + 切线
  │
  ├─→ CurveBake (Runtime 静态数学)
  │     Result struct / Bake / ProjectPositions
  │     SampleUniform(pts, samples, closed, SegmentSampler)
  │       ── 共享采样骨架：均匀分配余数 + 每段保底 1 样本 + 曲率/法线块
  │
  ├─→ Curves/CatmullRomCurve   ── SampleSegment: p0..p3 推导（过控制点）
  ├─→ Curves/BezierCurve       ── SampleSegment: 相邻点自动手柄（过控制点）
  │
  └─→ CurveAsset (ScriptableObject)
        持久化: 采样数据 + 元信息
        CreateAssetMenu: "Mine/Curve Asset"
```

### 类关系

| 类 | 位置 | 职责 |
|---|---|---|
| `CurveGeneratorWindow` | `Scripts/CurveGenerator/Editor/` | 独立 EditorWindow：GUI + Scene 预览 + Generate / Load / Save |
| `CurveBake` | `Scripts/CurveGenerator/` | 无状态数学工具: 投影 + 共享采样骨架 `SampleUniform` |
| `Curves/BezierCurve` | `Scripts/CurveGenerator/Curves/` | Bezier 段公式 + `SampleSegment` 回调 |
| `Curves/CatmullRomCurve` | `Scripts/CurveGenerator/Curves/` | Catmull-Rom 段公式 + `SampleSegment` 回调 |
| `CurveAsset` | `Scripts/CurveGenerator/` | ScriptableObject: 曲线数据容器 |

> 采样循环、首尾点、法线/曲率计算集中在 `CurveBake.SampleUniform`；各曲线类只提供段内 `Position`/`Tangent` 公式，避免逐字重复。

### 数据流

```
Control Parent (Transform，拖入窗口)
  │  GetComponentsInChildren 直系子物体
  ▼
控制点位置 (Vector3[])                 用户操作
  │                                    Tools → Curve Generator...
  ├── ProjectPositions() ─── 2D 模式: 投影到 XY/XZ/YZ 平面
  │
  ├── Bake → SampleUniform ── 均匀采样（闭合/开放 + 曲率/法线）
  │
  ▼
CurveBake.Result                       内存预览
  │  positions[] tangents[] arcLengths[]
  │
  ├── SceneView Handles ────── 黄色 CP 球 + 青色曲线 + 橙色切线
  │
  └── Save → CurveAsset.asset ─ 持久化到磁盘（覆盖需确认）
```

---

窗口独立继承 `EditorWindow`，在本工具内持有 Asset Path、Generate / Load / Save 与资产所有权。Load 恢复曲线资产中的数据和曲线类型；覆盖同类型资产保留 GUID，保存只写目标资产。

## 配置参数

### Window 面板

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Control Parent | Transform | — | 场景父物体，其**直系子物体**即控制点（≥2 个） |
| Curve Type | Enum | CatmullRom | CatmullRom（过控制点）/ Bezier（过控制点，自动手柄） |
| Dimension | Toolbar | XYZ | XYZ / XY / XZ / YZ，投影到指定平面 |
| Samples | Slider | 256 | 采样分辨率 (16–4096)，均匀分配至各段、每段保底 1 样本 |
| Loop | Toggle | false | 首尾闭合 |
| Asset Path | Text | `Assets/Mine/Data/Curves/NewCurve.asset` | Save / Load 共用资产路径（Browse... 可选） |

### CurveAsset 资产格式

| 字段 | 类型 | 说明 |
|------|------|------|
| `curveType` | Enum | CatmullRom / Bezier |
| `dimension` | Enum | XYZ / XY / XZ / YZ |
| `loop` | bool | 是否闭合 |
| `positions` | Vector3[] | 均匀采样的曲线点 |
| `tangents` | Vector3[] | 每点的归一化切线方向 |
| `arcLengths` | float[] | 累计弧长（从起点起算） |
| `totalLength` | float | 曲线总长度 |
| `sampleCount` | int | 采样点数 |
| `controlPointCount` | int | 控制点数量 |
| `controlPointPositions` | Vector3[] | Bake 时控制点位置快照 |

---

## 曲线类型

### Catmull-Rom

- C1 连续（位置 + 切线连续）
- 曲线精确穿过每个控制点
- 适合：有机运动轨迹、相机路径

### Bezier（自动手柄）

- 自动从相邻控制点计算手柄（Catmull-Rom 1/6 因子转换）
- 曲线精确穿过每个控制点
- 手柄不可单独调整（简化版，保证平滑）
- 适合：与 Catmull-Rom 交叉验证、后续扩展手动手柄

### 2D 模式

选择 XY/XZ/YZ 后，所有控制点投影到目标平面（取平均深度），生成的曲线完全平直。

---

## 使用方式

### 前置条件

1. 场景中有 1 个父物体，且其下有 ≥2 个直系子物体作为控制点（空物体即可）
2. 打开 `Tools → Curve Generator...`

### 操作流程

1. 将场景中的控制点父物体拖入窗口 `Control Parent` 字段——子物体被当作控制点
2. 选择 Curve Type 和 Dimension
3. 点击 `Generate`——Scene 视图显示控制点与曲线预览
4. 移动子物体位置后重新 Generate 刷新
5. 满意后点击 `Save`，存为 `.asset`（路径已存在时弹覆盖确认）

### 运行时读取

```csharp
CurveAsset curve = Resources.Load<CurveAsset>("Curves/MyCurve");

// 归一化采样 (t ∈ [0,1])
curve.Sample(0.5f, out Vector3 pos, out Vector3 tangent);

// 或直接访问数组
for (int i = 0; i < curve.positions.Length; i++)
    Debug.Log(curve.positions[i]);
```

---

## 文件清单

```
Assets/Mine/Scripts/CurveGenerator/
├── CurveGenerator.md              ← 本文档
├── CurveGeneratorWindow.cs.meta* ← Unity 生成的 GUID 元文件
├── CurveBake.cs                   ← 曲线烘焙算法 + 共享采样骨架
├── CurveAsset.cs                  ← ScriptableObject 数据容器
├── Curves/
│   ├── BezierCurve.cs             ← Bezier 段公式
│   └── CatmullRomCurve.cs         ← Catmull-Rom 段公式
└── Editor/
    └── CurveGeneratorWindow.cs    ← EditorWindow（GUI + Scene 预览 + Save）
```

> `*.cs.meta` 由 Unity 自动维护，`git mv` 迁移窗口时保留 GUID 以防引用丢失。

---

## 扩展点

- **BSpline / Hermite**：在 `Curves/` 下新增曲线类（段公式 + `SampleSegment` 回调），并在 `CurveBake.Bake()` 中登记类型
- **手动手柄 Bezier**：控制点改为 `Transform[2]`（位置 + 手柄）
- **3D 曲线管**：将 `positions[]` + `tangents[]` + `tubeRadius` 上传 GPU ComputeBuffer
- **实时烘焙**：`CurveBake` 是纯静态方法，可在运行时调用（如动态生成的曲线）
