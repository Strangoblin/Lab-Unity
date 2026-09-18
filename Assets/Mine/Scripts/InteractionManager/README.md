# InteractionManager — 正交交互系统

> 基于正交相机 + Compute Shader 的实时交互模拟系统。
> 支持水面波纹、雪地变形等效果，通过自定义渲染管线获取物体穿透深度作为输入。

---

## 架构

```
UniversalInteractionManager           ← 总控
  ├── 正交相机 (CustomRenderer)       ← 渲染交互物体深度到 originRT
  ├── _InteractionOriginTex           ← 共享输入 RT (Manager 管理)
  └── IUniversalInteractionProcessor  ← 处理器接口
        └── WaterInteractionProcessor  ← 水面波纹 (Verlet 波方程)
```

**关键设计**: Manager 只管理共享输入，Processor 自管输出 RT。

---

## 文件夹

| 路径 | 内容 |
|------|------|
| `InteractionManager.cs` | 总控：正交矩阵、相机跟随、originRT、全局 Shader 属性 |
| `IInteractionProcessor.cs` | 接口：Initialize / Process / BindGlobalTextures / Release |
| `Water/` | 水面交互：Compute Shader + Processor |
| `Shaders/` | 渲染 Shader：Debug 可视化 + InteractorObject |

---

## 设置步骤

1. 创建 `CustomRendererData` 资产 → 添加到 URP Renderer List
2. 场景中创建正交相机子物体，挂载 `UniversalAdditionalCameraData`，Renderer 设为 CustomRenderer
3. 在 GameObject 上挂载 `UniversalInteractionManager`，拖入相机
4. 同一 GameObject 上挂载 `WaterInteractionProcessor`，拖入 WaterInteraction.compute
5. 场景中放置 debug 平面（材质用 `InteractionManager/InteractionDebug`）
6. 交互物体使用 `InteractionManager/InteractorObject` Shader

---

## Inspector 参数

### UniversalInteractionManager

| 参数 | 说明 |
|------|------|
| Ortho Camera | 正交相机引用 |
| Follow Target | 跟随物体（空=静态） |
| Area Size | 交互区域边长 (m) |
| Ortho Height | 相机 Y 高度 |
| Ortho Near/Far | 深度裁剪范围 |
| Resolution | RT 分辨率 (px) |

### WaterInteractionProcessor

| 参数 | 默认值 | 说明 |
|------|--------|------|
| Wave Speed | 0.15 | 像素空间波速，已归一化到 areaSize=10 |
| Damping | 0.995 | 每帧能量衰减 |
| Object Force | 1.0 | 物体下压力度 |

---

## 纹理命名

| 纹理 | 管理者 | 用途 |
|------|--------|------|
| `_InteractionOriginTex` | Manager | 相机渲染的交互输入 |
| `_InteractionWaterTex` | WaterProcessor | 波方程输出（debug shader 采样） |
| `_InteractionWaterPTex` | WaterProcessor | Verlet h_prev（内部） |
| `_CustomDepthTexture` | CustomRenderer | 正交深度纹理 |

---

## 扩展

新增交互类型只需实现 `IUniversalInteractionProcessor`：

```csharp
public class SnowInteractionProcessor : MonoBehaviour, IUniversalInteractionProcessor
{
    public void Initialize(int resolution, RenderTexture sourceRT) { }
    public void Process(float deltaTime, Vector2 worldDelta) { }
    public void BindGlobalTextures() { }
    public void Release() { }
}
```

然后挂载到 Manager 同一 GameObject 即可，Manager 通过 `GetComponent<IUniversalInteractionProcessor>()` 自动发现。
