# Fullscreen Postprocess Shader Structure

> Unity 6 URP fullscreen postprocess guidance.
> General material Shader conventions: [standard Shader structure](../../standard/shader/shader-structure.md).

## 全屏后处理 Shader 结构参考

适用于全屏后处理 Shader（屏幕空间效果、图像滤镜、Post-processing 等），在继承普通 Shader 结构的基础上，增加以下偏好。参考实现：
- [SSSM.shader](Assets/Mine/Shaders/PostProcess/SSSM/SSSM.shader) + [SSSMFeature.cs](Assets/Mine/Shaders/PostProcess/SSSM/SSSMFeature.cs)
- [SSO.shader](Assets/Mine/Shaders/PostProcess/SSO/SSO.shader) + [SSOFeature.cs](Assets/Mine/Shaders/PostProcess/SSO/SSOFeature.cs)

### 0. 后处理 vs 普通 Shader 差异速览

| 维度 | 普通 Shader | 全屏后处理 Shader |
|---|---|---|
| 渲染状态 | 按需设置 Cull/ZWrite/ZTest | 总是 `Cull Off ZWrite Off ZTest Always` |
| 纹理绑定 | `_MainTex` 等材质纹理 | `_BlitTexture`（Unity 内置）、`_CameraOpaqueTexture`、自定义 RT |
| Vertex Shader | 自定义 `Vert` 处理模型变换 | 使用内置 `Vert`（来自 `Blit.hlsl`），仅处理 UV 和位置 |
| 多 Pass 关系 | 各 Pass 独立供不同 LightMode 使用 | 各 Pass 是管线步骤，在 Feature 中编排调用顺序 |
| 参数来源 | 材质属性面板 + CBUFFER | CBUFFER（经 Feature 的 `PropertyToID` 传入） |
| Parameters 块 | 完整暴露可调参数 | 可精简或留空（参数由 Feature 控制） |

### 1. 整体布局

```
Properties                     —— 可精简或留空（大部分参数由 C# Feature 传入）
HLSLINCLUDE
  #include                      —— Core.hlsl + Blit.hlsl（全屏后处理必备）
  #include                      —— DeclareDepthTexture / DeclareNormalsTexture 等
  #include                      —— 自有功能库

  // ── 参数 ──
  CBUFFER_START(UnityPerMaterial)
    float _Xxx;                  —— 可能很少甚至没有（参数走 Feature 的 SetFloat）
  CBUFFER_END

  // 工具函数 / 采样封装
  // Frag 函数（每个 Pass 一个）
ENDHLSL
SubShader
  Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
  Cull Off ZWrite Off ZTest Always   —— 后处理通用状态

  Pass "Xxx_PassName"
    HLSLPROGRAM
      #pragma vertex Vert           —— 统一使用 Blit.hlsl 的 Vert
      #pragma fragment Frag_Xxx
      #pragma shader_feature _ XXX_KEYWORD
    ENDHLSL
```

**Unity 6 特有**：
- 必须 `#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"`（内置 `Vert` + `Varyings` + `_BlitTexture`）
- Vertex Shader 统一使用 `#pragma vertex Vert`（来自 Blit.hlsl，无需自定义）
- 片元输入使用 `Varyings`（来自 Blit.hlsl），通过 `input.texcoord` 获取 UV
- `_BlitTexture` 为 Unity 内置的源图纹理，无需在 Shader 中手动声明 TEXTURE2D_X
- 基于 RenderGraph 的 `UnsafePass` + `SetRenderFunc` 模式

### 2. 参数最小化

后处理效果在调试阶段结束后（通常首次验收后），进行参数硬编码以降低性能消耗：

**① 计算密集型参数 → 关键字模式切换**

步进次数、采样次数、采样半径等影响循环次数的参数，避免在运行时通过 Material.SetFloat 传递，改为关键字编译不同变体：

```hlsl
// ✗ 避免：运行时动态步进（每次循环判断，GPU 无法优化分支）
#pragma shader_feature _ BLUR_BILATERAL_LOW BLUR_BILATERAL_MEDIUM BLUR_BILATERAL_HIGH
// ✓ 优选：编译时确定循环次数 / 采样模式
```

C# Feature 中通过 `EnableKeyword` / `DisableKeyword` 控制：

```csharp
material.DisableKeyword("BLUR_BILATERAL_LOW");
material.DisableKeyword("BLUR_BILATERAL_MEDIUM");
material.DisableKeyword("BLUR_BILATERAL_HIGH");
switch (settings.bilateralIntensity) {
    case BilateralIntensity.Low:  material.EnableKeyword("BLUR_BILATERAL_LOW");  break;
    case BilateralIntensity.High: material.EnableKeyword("BLUR_BILATERAL_HIGH"); break;
    default:                      material.EnableKeyword("BLUR_BILATERAL_MEDIUM"); break;
}
```

**② 功能开关 → 关键字二值切换**

划线类型、阴影类型、合成模式等离散选项也通过关键字切换：

```hlsl
#pragma shader_feature _ SSO_Basic SSO_Sobel SSO_DDXY
#pragma shader_feature _ SSO_SHADOW_NONE SSO_SHADOW_HARD SSO_SHADOW_SOFT
```

**③ 保留为参数的情形**

缩放系数、强度、阈值等精细调节且不影响性能的参数仍保持为 CBUFFER 变量，通过 Feature 的 `PropertyToID` 传入：

```csharp
internal static readonly int StepSizeID    = Shader.PropertyToID("_StepSize");
internal static readonly int MaxDistanceID = Shader.PropertyToID("_MaxDistance");
// ...
material.SetFloat(Settings.StepSizeID, settings.stepSize);
```

### 3. 管线解耦

将完整的后处理效果按功能拆分为多个 Pass，每个 Pass 职责单一、可独立调试，在 C# Feature 中编排完整的管线流程：

**① 分 Pass 原则**

```
Pass 0: 效果生成（如 DDA 步进、SSO 轮廓检测）
Pass 1: 可选处理（如模糊、降噪）——可跳过
Pass 2: 合成（如与原图混合、输出最终结果）
```

示例（SSSM 三 Pass 管线）：

```hlsl
// Pass 0 — DDA 步进生成阴影遮罩
half4 Frag_SSSM_DDA(Varyings input) : SV_Target { ... }

// Pass 1 — 水平双边模糊
half4 Frag_BlurH(Varyings input) : SV_Target { ... }

// Pass 2 — 垂直双边模糊
half4 Frag_BlurV(Varyings input) : SV_Target { ... }
```

C# Feature 中编排：

```csharp
// Pass 0: Ray March → 阴影遮罩
Blitter.BlitCameraTexture(cmd, source, shadowRT, material, 0);

// Pass 1 & 2: 模糊（可选）
if (enableBlur) {
    Blitter.BlitCameraTexture(cmd, shadowRT, blurRT, material, 1);
    Blitter.BlitCameraTexture(cmd, blurRT, shadowRT, material, 2);
}

// 输出全局纹理供其他 Shader 使用
cmd.SetGlobalTexture("_SSSM_ShadowMask", shadowRT);
```

**② C# Feature 结构**

```csharp
public class XxxFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        // 面板参数
        internal static readonly int ParamID = Shader.PropertyToID("_Param");  // PropertyToID 预处理
    }

    class XxxPass : ScriptableRenderPass
    {
        class PassData { /* RenderGraph 传递数据 */ }

        public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
        {
            // 1. 设置材质参数
            // 2. 关键字切换
            // 3. 创建临时 RT
            // 4. 编排 Pass 调用顺序
        }
    }

    public Settings settings = new Settings();
    public override void Create() { /* 初始化 */ }
    public override void AddRenderPasses(...) { /* 入队 */ }
}
```

**③ 全局纹理输出**

后处理生成的中间结果（如阴影图、轮廓图）通过 `cmd.SetGlobalTexture` 输出为全局纹理，供其他 Shader 采样复用，避免重复计算：

```hlsl
// 消费方 Shader 中采样
TEXTURE2D_X(_SSSM_ShadowMask);
float shadow = SAMPLE_TEXTURE2D_X(_SSSM_ShadowMask, sampler_LinearClamp, uv).r;
```

### 4. 其他偏好

**① PropertyToID 预处理**

所有参数 ID 在 `Settings` 内部类中以 `static readonly int` 形式预计算，避免每帧重复调用：

```csharp
internal static readonly int StepSizeID      = Shader.PropertyToID("_StepSize");
internal static readonly int MaxDistanceID   = Shader.PropertyToID("_MaxDistance");
internal static readonly int StepCountID     = Shader.PropertyToID("_StepCount");
```

**② Debug 开关**

每个 Feature 设置一个 Debug 开关（如 `SSSMFeature`, `SSOFeature`），控制是否在屏幕上显示中间结果。非调试模式下，仅生成全局纹理供其他 Shader 使用，不修改场景画面：

```csharp
[Header("Debug")]
public bool SSSMFeature = true;  // ON = 显示阴影图, OFF = 后台生成

// 使用
if (data.showShadow)
    Blitter.BlitCameraTexture(cmd, data.shadowRT, data.source);
```

**③ 全屏后处理通用渲染状态**

所有 Pass 统一设置，在 SubShader 层级声明：

```hlsl
SubShader
{
    Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
    Cull Off ZWrite Off ZTest Always
    // ...
}
```

单个 Pass 内不重复声明，减少冗余。

**④ 采样封装**

对于频繁使用的采样操作（如带深度/法线的采样），封装为独立函数，与 BlurFunction.hlsl 等自有库的引用方式一致：

```hlsl
float SampleDepth(float2 uv) {
    float rawDepth = SampleSceneDepth(uv);
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

float3 SampleNormal(float2 uv) {
    return normalize(SampleSceneNormals(uv));
}
```

### 5. 后处理 Shader 文件组织

```
Assets/Mine/Shaders/XxxPostEffect/
  ├── XxxPostEffect.shader       — Shader 代码
  ├── XxxPostEffectFeature.cs    — C# Feature（RendererFeature + ScriptableRenderPass）
  └── XxxPostEffect.md           — 技术文档（同普通 Shader 规范）
```

### 6. 调试注意事项

- 使用 `half4(color, 1)` 直接返回中间值来可视化（如返回 positionWS、normal、depth 等），方便在屏幕上看各步骤效果
- Debug 开关独立于效果开关，二者互不干扰
- `ConfigureInput(ScriptableRenderPassInput.Color | Depth | Normal)` 确保 RenderGraph 正确传入所需纹理
- 需要把独立 Shader 全屏输出到真实相机管线人工观察 → 复用 `Shaders/PostProcess/Debug/` 调试资源与固定 Feature，优先绑定材质；输入优先级和完整流程见 [feature-script-structure.md](feature-script-structure.md)「屏幕调试方法」。
