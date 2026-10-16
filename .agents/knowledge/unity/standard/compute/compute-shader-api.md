# ComputeShader C# API 速查

> Unity 6 Compute Shader 调度标准写法。

---

## 基础调度

```csharp
ComputeShader shader;

// 1. 获取 kernel
int kernel = shader.FindKernel("CSMain"); // ← 必须与 compute 文件中的 #pragma kernel 名一致
// ⚠️ FindKernel 失败返回 -1 → "thread group size must be above zero"

// 2. 设置参数
shader.SetTexture(kernel, "_Result", outputRT);
shader.SetTexture(kernel, "_Input", inputRT);
shader.SetFloat("_Intensity", 1.5f);
shader.SetVector("_ScreenSize", new Vector2(width, height));
shader.SetMatrix("_ViewProjInv", matrix);

// 3. Dispatch
int tgX = Mathf.CeilToInt(width  / 8.0f);  // ← 除以 NUMTHREAD_X
int tgY = Mathf.CeilToInt(height / 8.0f);  // ← 除以 NUMTHREAD_Y
shader.Dispatch(kernel, tgX, tgY, 1);
```

## RenderGraph 中 Dispatch

```csharp
using (var builder = renderGraph.AddComputePass<PassData>("MyCS", out var passData))
{
    passData.shader = m_Shader;
    passData.kernel = m_Shader.FindKernel("CSMain");

    builder.UseTexture(inputTex, AccessFlags.Read);
    builder.UseTexture(outputTex, AccessFlags.Write);

    builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) =>
    {
        ctx.cmd.SetComputeTextureParam(data.shader, data.kernel, "_Result", data.output);
        ctx.cmd.SetComputeTextureParam(data.shader, data.kernel, "_Input",  data.input);
        ctx.cmd.DispatchCompute(data.shader, data.kernel, data.tgX, data.tgY, 1);
    });
}
```

## 常用 Set 方法

| 方法 | HLSL 对应 | 用途 |
|------|----------|------|
| `SetFloat` | `float` | 标量参数 |
| `SetInt` | `int` | 整数参数 |
| `SetVector` | `float2/3/4` | 小向量 (如 screen size) |
| `SetMatrix` | `float4x4` | MVP 等矩阵 |
| `SetTexture` | `Texture2D / RWTexture2D` | 纹理 |
| `SetBuffer` | `StructuredBuffer / RWStructuredBuffer` | 结构化缓冲区 |
| `SetComputeXXXParam` | 同上 | RenderGraph 内部用 |

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| `FindKernel("CSMain") = -1` | kernel 名大小写不匹配或未声明 | 检查 compute 文件中 `#pragma kernel CSMain` |
| `thread group size must be above zero` | dispatch 时 kernel index =-1 | 检查 FindKernel 返回值 |
| `SetTexture` 无效果 | 纹理格式不支持 | 检查 `enableRandomWrite = true` 和 `RenderTextureFormat` |
