# NormalSmoother

按位置合并顶点、按面法线夹角聚类，重建一个带平滑法线的网格；结果可写入顶点法线或顶点色。

移植自 CelToon `Assets/Mine/Scripts/NormalSmoother.cs`（2026-10-17），算法与参数语义保持一致，代码按本项目脚本规范重排。窗口版同步落在 `Editor/NormalSmootherWindow.cs`。

## 入口

| API | 说明 |
|---|---|
| `NormalSmoother.CreateSmoothedMesh(Mesh sourceMesh, Settings settings)` | 返回新网格；`sourceMesh` 为空返回 `null`，不可读时告警并返回 `null` |
| `NormalSmoother.StorageMode` | `VertexColors` / `VertexNormals` |
| `NormalSmoother.Settings` | `angleThreshold` / `positionEpsilon` / `storageMode` |

命名空间：`Mine.NormalSmoother`。

## 参数

| 字段 | 范围 | 常用值 | 说明 |
|---|---|---|---|
| `angleThreshold` | 0–180 | 60 | 同一位置内，面法线夹角不超过该值的面归为一簇；180 表示整组归一 |
| `positionEpsilon` | ≥ 1e-6 | 1e-4 | 位置量化步长，决定哪些顶点被视为“同一位置” |
| `storageMode` | 枚举 | `VertexColors` | `VertexNormals` 写顶点法线；`VertexColors` 写编码法线 `n * 0.5 + 0.5`，alpha 恒为 1 |

`CreateSmoothedMesh` 会夹紧越界参数，调用方无需自行 Clamp。

## 算法

1. **拐角**：每个三角形顶点记录源顶点索引、面法线与面面积。
2. **位置分组**：位置按 `positionEpsilon` 取整成键，同位置拐角归为一组。
3. **聚类**：组内按面法线夹角做连通 BFS，夹角不超过阈值即连通；硬表面两侧因此不会被互相平滑。
4. **簇法线**：簇内面法线按面积加权求和后归一化；退化时回退到首个面法线。
5. **重建**：输出顶点键为 `(源顶点, 位置组, 簇)`，按簇拆分顶点；法线、切线、颜色、骨骼权重、8 个 UV 通道与子网格结构一并保留。

## 使用

```csharp
Mesh smoothed = NormalSmoother.CreateSmoothedMesh(sourceMesh, new NormalSmoother.Settings
{
    angleThreshold  = 60f,
    positionEpsilon = 0.0001f,
    storageMode     = NormalSmoother.StorageMode.VertexColors
});
```

Shader 侧按顶点色解码：

```hlsl
float3 smoothNormal = vertexColor.rgb * 2.0 - 1.0;
```

## Editor 窗口

菜单：`Tools/Normal Smoother...`，文件：`Editor/NormalSmootherWindow.cs`。按本项目标准 Baker 形态实现，输入固定为单个 Mesh：

| 区域 | 行为 |
|---|---|
| Input | 只接受一个 Mesh 资产（FBX 内的网格同样以 Mesh 引用传入）；不可读时禁用 Bake |
| Smoothing Settings | 角度阈值、位置容差、存储模式 |
| Asset Path | 固定资产路径，Save 与 Load 共用；Browse 只改绑同一字段 |
| Bake | 调用 `CreateSmoothedMesh`，结果移交窗口；显示进度条 |
| Load | 回看路径上的 Mesh；路径不是 Mesh 时拒绝载入 |
| Save | 同名 Mesh 原位更新（`CopySerialized`）并保留 GUID；覆盖前确认；只保存目标资产 |
| Preview | 预览当前资产网格；关窗释放 `PreviewRenderUtility` 与临时材质 |

示例固定路径为 `Assets/Mine/Generated/NormalSmoother/SmoothedNormal.asset`，可在路径行内改绑。窗口不再收集多目标、不遍历子级 / 非激活对象，也不回写场景引用。

## 约束

- 源网格必须在导入设置中开启 Read/Write，否则返回 `null` 并输出警告。
- 返回网格归调用方所有：运行时用完 `Destroy`，Editor 临时预览用完 `DestroyImmediate`，落盘前另存资产。
- `VertexColors` 模式会覆盖源顶点色，颜色通道被法线占用。
- 顶点数超过 65535 时自动切换 `UInt32`，否则沿用源网格索引格式。
- 骨骼权重与 `bindposes` 一并复制，蒙皮网格可直接替换 `sharedMesh`。
