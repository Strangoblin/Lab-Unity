# Picker — GPU 屏幕空间选物 + 描边系统

> 日期: 2026-06-18
> 路径: `Assets/Mine/Scripts/Picker/`
> 依赖: Unity 6 URP 17+, Input System, RenderGraph

---

## 功能概述

GPU Picker 是一套纯 GPU 驱动的屏幕空间选物方案，由两个子系统组成：

1. **GPU Picker** — 鼠标点击 → 屏幕空间 GPU 查找物体 ID，CPU 零遍历
2. **Outline Drawer** — 选中物体 → 屏幕空间后处理描边，与主场景合成

两部分通过 `selectedObjectID` 耦合，点击任意可选物体后自动出现黄色描边。

---

## 架构

```
┌─────────────────────────────────────────────────────────────┐
│                      每帧渲染流程                            │
│                                                              │
│  ┌─────────────────────┐     ┌──────────────────────┐       │
│  │ PickerPass (Unsafe)  │     │ OutlinePass (Unsafe)  │       │
│  │                     │     │                      │       │
│  │  MRT 绘制 ──────────┼─┐   │  Mask Pass ──────────┼─┐     │
│  │    ObjID (ARGB32)   │ │   │    DrawRenderer      │ │     │
│  │    Depth             │ │   │    → R8 Mask RT      │ │     │
│  │    Normal            │ │   │                      │ │     │
│  │                     │ │   │  Composite Pass ─────┼─┤     │
│  │  直写持久化 RT ─────┼─┤   │    四邻采样 + Blit   │ │     │
│  │                     │ │   │    → Camera Target   │ │     │
│  └─────────────────────┘ │   └──────────────────────┘ │     │
│                          │                            │     │
│  ┌───────────────────────┼────────────────────────────┼─┐   │
│  │ PickerReadback (CPU)  │                            │ │   │
│  │                       │                            │ │   │
│  │  Input System 点击 ───┼─→ AsyncGPUReadback ────────┼─┤   │
│  │  (Mouse.current)      │   Request 1×1 px           │ │   │
│  │                       │   → RGB24 解码 ObjectID ───┼─┤   │
│  │                       │   → outlinePass.selectedID │ │   │
│  └───────────────────────┘                            │ │   │
│                                                       │     │
│  Frame Debugger 层级:                                  │     │
│    Picker/MRT                                         │     │
│    Picker/Mask                                         │     │
│    Picker/Composite                                    │     │
└─────────────────────────────────────────────────────────────┘
```

### 数据流（点击→描边）

```
点击屏幕
  → Mouse.current.leftButton.wasPressedThisFrame
  → Camera.ScreenToViewportPoint → RT 像素坐标
  → AsyncGPUReadback.Request(持久化 RT, 1×1 px)
  → 回调: data[0..2] → RGB24 解码 → ObjectID = r<<16|g<<8|b
  → outlinePass.selectedObjectID = id

下一渲染帧:
  → OutlinePass.FindRendererByID(id) → 找到 MeshRenderer
  → cmd.DrawRenderer → R8 Mask RT (物体=255, 背景=0)
  → OutlineComposite.shader: 四邻采样 → 边缘检测
  → Blitter.BlitCameraTexture → 描边合成到 Camera Target
```

---

## 文件清单

### C# Scripts

| 文件 | 类 | 职责 |
|---|---|---|
| `PickerFeature.cs` | `PickerFeature : ScriptableRendererFeature` | URP Feature 注册，暴露 PickerPass |
| `PickerPass.cs` | `PickerPass : ScriptableRenderPass` | MRT 绘制 + 持久化 ObjectID RT |
| `PickerReadback.cs` | `PickerReadback : MonoBehaviour` | 鼠标输入 → AsyncGPUReadback → 更新 Outline |
| `OutlineFeature.cs` | `OutlineFeature : ScriptableRendererFeature` | URP Feature 注册，暴露 OutlinePass |
| `OutlinePass.cs` | `OutlinePass : ScriptableRenderPass` | Mask 绘制 + 全屏描边合成 |

### Shaders

| 文件 | Shader 名 | Pass | 用途 |
|---|---|---|---|
| `Picker.shader` | `Picker/Picker` | PickerMRT (0) | MRT 输出：RGB24 ObjectID / Depth / Normal |
| | | UniversalForward (1) | 可见渲染：ID→色相映射 |
| `OutlineMask.shader` | `Picker/OutlineMask` | OutlineMask | 选中物体 → R8 mask (1.0) |
| `OutlineComposite.shader` | `Picker/OutlineComposite` | OutlineComposite | 全屏后处理：四邻采样 + 描边色 + 原色混合 |

---

## 关键设计

### ObjectID RGB24 编码

```
Shader:  r = (id >> 16) & 255,  g = (id >> 8) & 255,  b = id & 255
         输出 float4(r/255, g/255, b/255, 1.0) → SV_Target0

Readback: id = data[0] << 16 | data[1] << 8 | data[2]
```

- ID 范围 0–16,777,215
- 使用标准 ARGB32 linear RT（`sRGB = false`）
- 比单通道 R8 方式免去了格式混用问题

### 持久化 RT + CPU Readback

- `EnsureObjIDRT(width, height)` 按需创建 ARGB32 linear RenderTexture
- `ImportTexture` 直接绑到 MRT Pass attachment 0，跳过拷贝 Blit
- `PickerReadback.Update` 每帧刷新 RT 引用（分辨率变化时自动重建）

### 描边算法

- Mask RT: R8, 物体=1.0 (255), 背景=0
- Composite Shader: 采样上/下/左/右四邻，中心在物体内→原色，邻居在物体内→描边色
- `_OutlineWidth` 控制描边像素宽度（× texelSize）

### SRP 兼容

- `AddUnsafePass` + `cmd.DrawRenderer` — 绕过 RenderGraph 的 RendererList 格式限制
- Unity 6 要求显式设置 `ZWrite Off / ZTest Always / Cull Off`
- 全屏 Blit 使用 `Blitter.BlitCameraTexture` + `Blit.hlsl`

---

## 使用方式

### 前置条件

1. **URP Renderer** 需添加 `PickerFeature` + `OutlineFeature`（位于 `BeforeRenderingPostProcessing`）
2. **可选物体** 需使用 `Picker/Picker` shader，设置唯一 `_ObjectID`
3. **PickerReadback** MonoBehaviour 挂载到场景 GameObject

### 添加可选物体

```csharp
var mat = new Material(Shader.Find("Picker/Picker"));
mat.SetInt("_ObjectID", uniqueId);  // 1–16,777,215
renderer.sharedMaterial = mat;
```

物体会在场景中自动可见（Forward Pass 根据 ID 生成色相），同时参与 GPU Picker 检测。

### Debug 视图

在 PC_Renderer 的 PickerFeature Inspector 中切换 `DebugView`:
- `Off` — 正常渲染
- `ObjectID` — 显示 ObjectID RT（极暗，需放大检查）
- `Depth` — 灰度深度图
- `Normal` — RGB 法线图

### 性能

- Pickable 物体数 N：每帧 **N 次 DrawCall**（单次 MRT）
- ObjectID RT: 全分辨率 ARGB32（≈ 8MB @ 1080p），可降采样
- Readback: 1×1 像素 AsyncGPUReadback（< 1ms 延迟）

---

## 已知限制

- ObjectID 上限 16,777,215（24-bit），如需更多可扩展 Alpha 通道
- 透明物体不支持（`renderQueueRange = opaque`）
- Readback 延迟 1-2 帧（AsyncGPUReadback），快速连续点击可能读到上帧数据
- Editor Game 视图缩放可能导致鼠标坐标偏移（已用 `ScreenToViewportPoint` 修正）

---

## 扩展点

- 深度/法线 RT 已生成未使用，可用于深度感知选物、法线加权描边
- 描边宽度可改为可变宽度（Jump Flood Algorithm 多 Pass）
- MRT 可降采样（half/quarter res）提升性能
- 多选支持：`HashSet<int>` 替代单个 `selectedObjectID`
