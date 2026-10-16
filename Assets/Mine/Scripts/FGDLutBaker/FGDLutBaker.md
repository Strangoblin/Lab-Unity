# FGDLutBaker — FGD LUT 预积分 BRDF 烘焙工具

**路径:** `Assets/Mine/Scripts/FGDLutBaker/`
**类型:** 静态烘焙工具 + 场景管理器（MonoBehaviour）+ Editor Window
**依赖:** Unity 6 URP, `ImageBasedLighting.hlsl` (URP Core 包)

---

## 功能概述

将 GGX BRDF 菲涅尔/几何遮蔽/法线分布项 + Disney Diffuse 的半球积分预烘焙到一张 2D LUT。

运行时配合 [ENVFunction.hlsl](../../Special/HLSL/ENVFunction.hlsl) 的 `BRDF_Env` 使用：由场景中的 `FGDLutManager` 探测自身挂载的 LUT 并设置全局，自动启用 FGD 裂项近似；无 LUT 时回退 Karis 分析近似。

> Baker 与 Manager 完全解耦：Baker 只负责生产纹理，Manager 只负责探测与下发，互不调用。

---

## 文件清单

| 文件 | 职责 |
|------|------|
| `FGDLutBaker.cs` | 静态工具类：`Bake()` / `LogDiagnostics()`（只生产纹理） |
| `FGDLutManager.cs` | 场景组件（MonoBehaviour）：自行探测挂载的 LUT 并下发/清除全局状态 |
| `FGDPacker.shader` | Hidden/Mine/FGDPacker — 逐像素调用 `IntegrateGGXAndDisneyDiffuseFGD` |
| `FGDLutBaker.md` | 本文档 |

### Editor

| 文件 | 职责 |
|------|------|
| `Assets/Mine/Scripts/FGDLutBaker/Editor/FGDLutBakerWindow.cs` | 可视化面板：参数调节、烘焙/载入/保存、预览（不驱动场景） |

---

## 公共 API

```csharp
// 烘焙（GPU pixel shader → Readback）
Texture2D lut = FGDLutBaker.Bake(resolution: 128, sampleCount: 1024);

// 打印四角像素值
FGDLutBaker.LogDiagnostics(lut);
```

## 场景管理器（FGDLutManager）

全局纹理下发由场景组件 `FGDLutManager` 负责，Baker 不再持有运行时状态。Manager 完全被动：**不接收任何外部驱动**，只自行探测自己身上挂载的 LUT。

**挂载方式：** 在场景任意物体上 `Add Component → FGDLutManager`，把烘焙好的 LUT（建议先 Save 为 .asset）赋给 Inspector 的 `Lut` 字段。

**生命周期（自动探测）：**

| 时机 | 行为 |
|------|------|
| `OnEnable` | 探测：有 LUT → 设置全局 `_FGDLut` + `_UseFGDLut = 1`；无 LUT → 清除 |
| `OnDisable` | 清除全局，回退 Karis 分析近似 |
| `OnValidate` | Inspector 修改 LUT 时立即刷新（编辑态同样生效） |

> 注意：同一场景建议只保留一个 `FGDLutManager`（后启用的实例覆盖全局状态）。全局变量名 `_FGDLut` / `_UseFGDLut` 由本类独占管理。

---

## LUT 通道布局

| 通道 | 存储值 | 运行时解码 | 说明 |
|------|--------|-----------|------|
| R | scale（F₀=0 时的积分） | `(1−F₀) × R` | Schlick Fresnel bias |
| G | bias（F₀=1 时的积分） | `F₀ × G` | Schlick Fresnel scale |
| B | diffuse − 0.5 | `B + 0.5` | Disney Diffuse 响应 |
| A | 未使用 | — | 保留 |

---

## Editor 窗口

菜单栏 `Tools → FGD Lut Baker...`

| 控件 | 说明 |
|------|------|
| Resolution / Sample Count | 烘焙参数 |
| Asset Path | Save / Load 共用 .asset 路径，默认 `Assets/Textures/FGD_LUT.asset` |
| Bake | GPU 烘焙新 LUT |
| Load | 从 Asset Path 载入纹理，不恢复烘焙参数 |
| Save | 保存到 Asset Path；覆盖其他同类型资产前确认，原位更新保留 GUID |
| Preview | 当前纹理预览 |

---

窗口独立继承 `EditorWindow`，在本工具内持有 Asset Path、Bake / Load / Save 与资产所有权，只对目标调用 `SaveAssetIfDirty`。

## ENVFunction 集成

[ENVFunction.hlsl](../../Special/HLSL/ENVFunction.hlsl) 的 `BRDF_Env` 通过全局浮点数 `_UseFGDLut` 自动选择路径：

```
_UseFGDLut = 0（默认）→ Karis 2013 分析近似（移动端友好）
_UseFGDLut = 1        → FGD 裂项近似（更精确的 Fresnel/Geometry/Diffuse）
```

调用方无需任何修改 — 签名与原始 `BRDF_Env` 完全兼容。

---

## 性能

| 指标 | 128² + 1024spl | 256² + 4096spl |
|------|-----------------|-----------------|
| GPU 时间 | ~0.1s | ~1.5s |
| 内存 | 128KB (RGBAHalf) | 512KB |

烘焙为一次性 Editor 操作，不在运行时执行。

---

## 已知限制

- 依赖 `ImageBasedLighting.hlsl`（URP Core 包），仅 Unity 6 URP 17+ 验证
- 使用 `GetDimensions` + `_UseFGDLut` toggle 进行 LUT 检测
- Metal 平台已验证
