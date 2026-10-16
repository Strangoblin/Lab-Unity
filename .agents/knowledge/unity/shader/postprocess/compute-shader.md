# Compute Shader 参考 — Metal 兼容写法

> Unity 6 + Metal 平台 Compute Shader 模板和注意事项。

---

## 最小模板

```hlsl
// ⚠️ 文件名必须为 .compute，放在 Editor 能识别的路径下
#pragma kernel CSMain

// ═══ 全局变量 (C# 端通过 Shader.SetXXX 设置) ═══
RWTexture2D<float4> _Result;
Texture2D<float> _CameraDepthTexture;  // ← Unity 提供 GBUFFER_DEPTH
SamplerState PointClampSampler;        // ← 点采样器
float2 _ScreenSize;                     // ← 自定义参数

#define NUMTHREAD_X 8   // Metal 推荐 8x8
#define NUMTHREAD_Y 8

[numthreads(NUMTHREAD_X, NUMTHREAD_Y, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    // ⚠️ Metal 要求必须检查边界
    if (id.x >= (uint)_ScreenSize.x || id.y >= (uint)_ScreenSize.y) return;

    float2 uv = (id.xy + 0.5) / _ScreenSize;
    float depth = _CameraDepthTexture.SampleLevel(PointClampSampler, uv, 0);

    // ... 计算逻辑 ...

    _Result[id.xy] = float4(result, 1.0);
}
```

---

## Thread Group 大小指南 (Metal)

| 操作类型 | 推荐 thread group | 原因 |
|---------|------------------|------|
| 逐像素处理 | `[numthreads(8, 8, 1)]` | Metal GPU 最佳 64 线程/组 |
| 水平模糊 | `[numthreads(64, 1, 1)]` | 1D 操作可用更大 X |
| 垂直模糊 | `[numthreads(1, 64, 1)]` | 同上 |
| 复杂计算 | `[numthreads(4, 4, 1)]` | 减少寄存器压力 |

**Metal 不支持 `[numthreads(16, 16, 1)]`（256线程）在某些 GPU 上可能失败。**
最大 thread group 内存: 16KB (Apple Silicon), 32KB (AMD GPU via macOS)。

---

## C# Dispatch 计算

```csharp
// 正确 dispatch:
int threadGroupsX = Mathf.CeilToInt(screenWidth  / 8.0f);
int threadGroupsY = Mathf.CeilToInt(screenHeight / 8.0f);
cmd.DispatchCompute(shader, kernelIndex, threadGroupsX, threadGroupsY, 1);
//                        ↑ 必须除以 group 大小 (8)，不是 thread 数
```

---

## 常见 Metal 错误

| 错误 | 原因 | 修复 |
|------|------|------|
| `thread group size must be above zero` | kernel index 为 -1 (找不到 kernel) | 检查 `#pragma kernel` 名字和 `FindKernel` 字符串一致 |
| `Thread group size too large` | numthreads 乘积 > 1024 | 用 8×8 替代 16×16 |
| `buffer overflow` (静默) | dispatch 数量计算错误 | 检查 CeilToInt 除法 |
| `RWTexture` 写入黑屏 | 数据格式不匹配 | C# 端 `RenderTextureFormat` 与 HLSL 类型对齐 |
| `SampleLevel` 不工作 | 纹理类型不对 | Depth 纹理必须用 `PointClampSampler` 而非 `LinearClampSampler` |

---

## Unity 6 RenderGraph Dispatch 模式

```csharp
// Unity 6 新 API:
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

class PCSSRenderPass : ScriptableRenderPass
{
    // ... 
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer context)
    {
        var textureHandle = ...;
        var builder = renderGraph.AddComputePass("PCSS", out PassData data);
        data.shader = shader;
        // ...
        builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) =>
        {
            ctx.cmd.SetComputeTextureParam(shader, kernel, "_Result", data.output);
            ctx.cmd.DispatchCompute(shader, kernel, tgX, tgY, 1);
        });
    }
}
```
