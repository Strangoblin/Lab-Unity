# Metal Platform Notes

> macOS Metal GPU 上的 Unity Shader / Compute Shader 特殊行为。
> 你运行在 macOS (Darwin 24) + Apple Silicon / AMD GPU。

---

## Compute Shader 限制

| 项目 | Metal 限制 | 备注 |
|------|-----------|------|
| Max threads per group | **1024** | `numthreads(a,b,c)`: `a*b*c ≤ 1024` |
| Max X / Y per group | 1024 / 1024 | `numthreads(64, 64, 1) = 4096 → ❌` |
| 推荐 thread group | `8×8` (64 threads) | 比 `16×16` 更安全 |
| Max thread group memory | **16 KB** (Apple Silicon), 32 KB (AMD via macOS) | groupshared 变量总和 |
| Max buffers per compute | 31 | `RWBuffer` / `RWTexture` 总数 |
| Max textures per compute | 31 | Read + Write 纹理总计 |
| Texture array (depth slice) | 必须用 `Texture2DArray` 显式声明 | `useTex2DArray` 参数不影响 Compute Shader |

## Shader 差异

| 项目 | DirectX / Vulkan | Metal |
|------|-----------------|-------|
| `SV_IsFrontFace` | 可用 | 某些 pass 不可用，需用 Cull 或 faceSign |
| `ddx` / `ddy` (fine) | 可用 | 在部分 pass (非 Forward) 中精度不同 |
| `clip()` | 任意用 | **导致 GPU 崩溃** 如果大量像素被丢弃 |
| `TEXTURE2D_X` | 启用纹理分块 | 退化为 `TEXTURE2D`（不支持分块） |
| `#pragma target 2.0` | 可选 | **建议显式声明**，省略会触发 Metal 警告 |
| vertex output 未初始化 | DX 默认 0 | **Metal 报错**：所有 output 字段必须显式初始化 |

## 纹理格式

| C# 声明 | Metal 实际 | 说明 |
|---------|-----------|------|
| `ARGB32` (stencil) | 不支持 stencil | 深度+模板必须用 `Depth/Stencil` format |
| `RFloat` (单通道) | `R32Float` | 无符号 `R16_UNorm` 可用 |
| `RHalf` (半精度) | `R16Float` | Apple GPU 可以更高效读取 |
| `ARGBHalf` | `RGBA16Float` | HDR 颜色的标准选择 |

## 调试技巧

- **Xcode GPU Frame Capture**：能抓帧，但 Unity Editor 下不稳定
- **推荐方式**：用 `unityctl logs` 查看 Metal 验证层错误（`[Metal]` 前缀）
- **性能分析**：用 `unityctl script eval` 调用 `Profiler.BeginSample/EndSample`
- **常见 Metal 错误代码**：
  - `IOAF code 0xe00002e8` → thread group 大小非法
  - `IOAF code 0xe00002e9` → buffer 绑定越界
  - `IOAF code 0xe00002c0` → 纹理格式不匹配

## 参考资料

- Unity Manual: [Writing shaders for Metal](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-Metal.html)
- Apple: [Metal Shading Language Specification](https://developer.apple.com/metal/Metal-Shading-Language-Specification.pdf)
