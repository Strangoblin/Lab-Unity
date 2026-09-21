# VolumeComponent — 后处理参数定义

> Unity 6 URP 添加自定义后处理 Feature 的标准参数定义方式。

---

## 模板

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[System.Serializable]
[VolumeComponentMenu("Post-processing/MyEffect")]   // ← 菜单路径
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]  // ← 指定管线
public class MyEffect : VolumeComponent, IPostProcessComponent
{
    // ── 参数 ──
    [Tooltip("Effect intensity.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Blur radius in pixels.")]
    public ClampedIntParameter radius = new ClampedIntParameter(4, 1, 16);

    [Tooltip("Tint color.")]
    public ColorParameter tint = new ColorParameter(Color.white, hdr: false, showAlpha: false, showEyeDropper: true);

    // ── 兼容性 ──
    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false; // ← 后处理通常为 false
}
```

## 可用参数类型

| 类型 | 示例 | 用途 |
|------|------|------|
| `ClampedFloatParameter` | `new(0.5f, 0, 1)` | 范围浮点 (Volume 面板有滑条) |
| `FloatParameter` | `new(1.0f)` | 无限制浮点 |
| `ClampedIntParameter` | `new(4, 1, 16)` | 范围整数 |
| `IntParameter` | `new(4)` | 无限制整数 |
| `ColorParameter` | `new(Color.white)` | 颜色拾取器 |
| `BoolParameter` | `new(false)` | 开关 |
| `Vector2Parameter` | `new(Vector2.one)` | 二维向量 |
| `Texture2DParameter` | `new(null)` | 纹理引用 |
| `NoInterpFloatParameter` | `new(1.0f)` | 浮点（体积混合时不插值） |

## 在 Feature 中读取参数

```csharp
// MyEffect 由 VolumeManager 自动注入
MyEffect m_Settings;  // ← Feature 中的引用（通过 VolumeStack 获取）

void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
{
    var stack = VolumeManager.instance.stack;
    m_Settings = stack.GetComponent<MyEffect>();
    if (m_Settings == null || !m_Settings.IsActive()) return;

    float intensity = m_Settings.intensity.value;
    int radius = m_Settings.radius.value;
    Color tint = m_Settings.tint.value;
}
```

## Feature 接入 Volume 完整流程（Volume 驱动模式）

> 提取自 SSL（2026-08-27 解耦记录，原 [SSL.md](Assets/Mine/Shaders/PostProcess/SSL/SSL.md) 的接入方法完整归档）。
> 适用于需要 Volume 驱动（多 Volume 混合、运行时动画、Profile 资产化）的后处理 Feature。

### 1. Volume 组件定义

```csharp
[System.Serializable, VolumeComponentMenu("SSL")]
public class SSLVolume : VolumeComponent, IPostProcessComponent
{
    public ClampedIntParameter   maxSteps     = new ClampedIntParameter(32, 1, 256);
    public ClampedFloatParameter maxDistance  = new ClampedFloatParameter(10f, 0.1f, 100f);
    // ...其余参数同 Feature Settings
    public BoolParameter         enabled      = new BoolParameter(true);

    public enum SSLType { Fog, Light }
    public SSLTypeParameter sslType = new SSLTypeParameter(SSLType.Light);   // 自定义 VolumeParameter<Enum>

    public bool IsActive() => enabled.value && intensity.value > 0f;
    public bool IsTileCompatible() => false;   // 后处理不支持分块
}
```

要点：
- 枚举参数需自定义 `VolumeParameter<SSLVolume.SSLType>`（如 `SSLTypeParameter`），Unity 不直接支持泛型枚举序列化
- 菜单属性：Unity 6 用 `[VolumeComponentMenu("...")]` + `[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]`（见上文模板，与 URP 17 内置 Bloom / SplitToning 一致）。`[VolumeComponentMenuForRenderPipeline]` 自 2023.1 起过时，Unity 6 下报 **CS0619 编译错误**，不要使用

### 2. Feature 中每帧读取 Volume

```csharp
// ── Volume 参数 ──（RecordRenderGraph 开头）
var stack = VolumeManager.instance.stack;
var vol = stack.GetComponent<SSLVolume>();
if (vol == null || !vol.IsActive() || !cameraData.postProcessEnabled)
    return;   // 无 Volume / 未激活 / 后处理关闭 → 跳过整个 pass

// ── 材质参数搬运 ──
sslMaterial.SetInt("_MaxSteps", vol.maxSteps.value);
sslMaterial.SetFloat("_MaxDistance", vol.maxDistance.value);

// ── 关键字切换（Volume 枚举驱动）──
sslMaterial.DisableKeyword("SSL_FOG");
sslMaterial.DisableKeyword("SSL_LIGHT");
switch (vol.sslType.value)
{
    case SSLVolume.SSLType.Fog:   sslMaterial.EnableKeyword("SSL_FOG");   break;
    case SSLVolume.SSLType.Light: sslMaterial.EnableKeyword("SSL_LIGHT"); break;
}
```

- `VolumeManager.instance.stack` 每帧获取当前生效 Volume 栈（全局 + 相机 + 区域 Volume 混合结果）
- `GetComponent<T>()` 返回栈中该组件的最终混合值；`IsActive()` 与 `postProcessEnabled` 决定是否执行
- **每次渲染前必须重新搬运**（Volume 参数可运行时变化），与 settings 直读的最大区别

### 3. Volume 模式 vs 纯 settings 模式

| 维度 | Volume 驱动 | 纯 Feature Settings |
|------|------------|-------------------|
| 参数来源 | Volume 栈（多 volume 混合、运行时动画、Profile 资产化） | Renderer Feature 资产保存，简单直接 |
| 读取时机 | 每帧 `stack.GetComponent<T>()` 搬运 | Feature 序列化字段直读 |
| 开关判断 | `vol.IsActive() && postProcessEnabled` | shader / material 有效性检查 |

恢复 Volume 模式的通用步骤：定义 VolumeComponent → Feature 读取块替换 settings 参数 → 关键字切换改枚举驱动 → 在 Volume Profile 资产添加组件块。

## 关键注意事项

- `[VolumeComponentMenu]` + `[SupportedOnRenderPipeline]` 是 Unity 6 的写法；`[VolumeComponentMenuForRenderPipeline]` 已在 2023.1 过时并在 Unity 6 报错（本条曾长期写反，2026-09-20 实测纠正）
- 参数在 Volume Profile 中序列化，**修改脚本不会丢失配置**（但重命名类型会丢失）
- `IsTileCompatible() = false` 表示不支持分块渲染（绝大多数后处理都返回 false）
