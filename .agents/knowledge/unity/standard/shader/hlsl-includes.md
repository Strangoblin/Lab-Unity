# URP HLSL Include 速查

> Unity 6 URP 17+ 常用 include。写 shader 时对照此表，不凭记忆。

---

## 必装

### Core.hlsl
`#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"`

| 函数 | 签名 | 用途 |
|------|------|------|
| `TransformObjectToHClip` | `float4(float3 positionOS)` | OS → HCS |
| `TransformObjectToWorld` | `float3(float3 positionOS)` | OS → WS |
| `TransformObjectToWorldNormal` | `float3(float3 normalOS)` | OS 法线 → WS |
| `TransformWorldToHClip` | `float4(float3 positionWS)` | WS → HCS |
| `GetVertexPositionInputs` | `VertexPositionInputs(float3 OS)` | 一次获取全空间位置 |

| 宏 | 示例 | 用途 |
|----|------|------|
| `CBUFFER_START` / `CBUFFER_END` | `CBUFFER_START(UnityPerMaterial)` | 常量缓冲区 |
| `TEXTURE2D_X` | `TEXTURE2D_X(_BlitTexture);` | 后处理纹理声明 |
| `SAMPLE_TEXTURE2D_X` | `SAMPLE_TEXTURE2D_X(tex, sampler, uv)` | 后处理纹理采样 |

> ❌ 后处理不要用 `TEXTURE2D` 不带 `_X`。

### Blit.hlsl
`#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"`

提供 `Vert()` / `Varyings` / `_BlitTexture` / `sampler_LinearClamp` — 不需要手写。
**Unity 2022 → 6**：`_MainTex` → `_BlitTexture`，`_MainTex_ST` 不再需要。

### CBUFFER 约定

```hlsl
CBUFFER_START(UnityPerMaterial)  // 材质属性（Material.SetXXX）
    float4 _Color;    // ← Color → float4
    float  _Size;     // ← Float → float
CBUFFER_END
TEXTURE2D(_Tex);       // ← 纹理在 CBUFFER 外
SAMPLER(sampler_Tex);  // ← 采样器也在外
```

可用块：`UnityPerMaterial`（每材质）、`UnityPerDraw`（每物体）、`UnityPerFrame`（每帧）。

---

## 可选

### Lighting.hlsl
`#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"`

`GetMainLight()` / `GetAdditionalLight(uint, float3)` / `InitializeInputData(...)`

### Shadows.hlsl
`#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"`

`TransformWorldToShadowCoord` / `MainLightRealtimeShadow` / `AdditionalLightRealtimeShadow`

---

## Metal 平台

详见 [platform/metal-notes.md](../../platform/metal-notes.md)。速记：
- Compute: `[numthreads(8,8,1)]`，不用 `(16,16,1)`
- `#pragma target 2.0` 必须声明
- vertex output 全部字段必须显式初始化
- 避免 frag 中大量 `clip()`
