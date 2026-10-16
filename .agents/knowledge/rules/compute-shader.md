---
paths:
  - "**/*.compute"
---
# Compute Shader 开发规范

> 完整模板与错误表见 [references/shader/postprocess/compute-shader.md](../unity/shader/postprocess/compute-shader.md) + C# 侧 [references/standard/compute/compute-shader-api.md](../unity/standard/compute/compute-shader-api.md)

## Metal 兼容

- `[numthreads(8,8,1)]` 推荐（64 线程/组），不要用 `(16,16,1)`
- 乘积 `a*b*c ≤ 1024`
- 必须检查 dispatch id 边界：`if (id.x >= (uint)_ScreenSize.x || id.y >= (uint)_ScreenSize.y) return;`

## Kernel 声明

- `#pragma kernel KernelName` 必须与 C# `FindKernel("KernelName")` 大小写完全一致
- 多 kernel 时，每个 kernel 独立的 `[numthreads]` 和函数体

## 纹理采样

- Depth 纹理必须用点采样：`_CameraDepthTexture.SampleLevel(PointClampSampler, uv, 0)`
- 声明：`SamplerState PointClampSampler;`

## 项目约定

- 文件头必须有 `// ═══` 分隔注释块
- 公共函数提取到 `.hlsl` include 文件
- 避免在 compute shader 中写 >100 行的单个 kernel
