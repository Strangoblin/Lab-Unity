# Blitter 全屏后处理 — Unity 6 vs 2022 差异

> 完整模板：`../../../templates/shader/postprocess/fullscreen-postprocess.shader`。这里只列差异对照和常见错误。

---

## Unity 2022 vs Unity 6

| 要素 | Unity 2022 (旧) | Unity 6 (新) |
|------|----------------|-------------|
| 输入纹理 | `TEXTURE2D(_MainTex)` | `TEXTURE2D_X(_BlitTexture)` (Blit.hlsl 提供) |
| 采样宏 | `SAMPLE_TEXTURE2D` | `SAMPLE_TEXTURE2D_X` |
| Vertex | 手动 `SV_VertexID` | Blit.hlsl `Vert()` |
| Frag 参数 | 手写 struct | Blit.hlsl `Varyings` |
| Blit.hlsl | 不需要 | **必须 include** |
| `_MainTex_ST` | 需要声明 | **不需要** |
| `#pragma target` | 可选 | 建议 `2.0`（避免 Metal 警告） |

## 必装 Include

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
```

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| `undeclared '_BlitTexture'` | 缺少 Blit.hlsl | 加 include |
| `undeclared 'Vert'` | 同上 | 同上 |
| `redefinition of 'Varyings'` | 手写了一遍 | 删除手写，用 Blit.hlsl 的 |
| `SAMPLE_TEXTURE2D requires SamplerState` | 宏版本不匹配 | 改用 `SAMPLE_TEXTURE2D_X` |
| Metal: `vertex output not completely written` | output 字段未初始化 | 显式初始化全部字段 |

## 多 Pass

参考 `Kuwahara.shader` 6-pass：共用 `HLSLINCLUDE`，每 pass 独立 `HLSLPROGRAM`。
