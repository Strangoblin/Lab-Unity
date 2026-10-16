# RenderGraph API 速查

> Unity 6 URP 入口 `RecordRenderGraph`，不用旧版 `Execute`。
> 完整模板：`../../../templates/shader/postprocess/urp-renderpass.cs`

---

## 关键 API 对照

| 旧写法 (Unity 2022) | 新写法 (Unity 6) |
|---------------------|-----------------|
| `Execute(ScriptableRenderContext, ref RenderingData)` | `RecordRenderGraph(RenderGraph, ContextContainer)` |
| `cmd.Blit(source, dest, material, pass)` | `Blitter.BlitTexture(cmd, source, scale, material, pass)` |
| `cmd.SetGlobalTexture("_MyTex", rt)` | `builder.UseTexture(handle, AccessFlags.Read)` |
| `RenderTexture.GetTemporary()` | RenderGraph 自动管理 |
| `ref RenderingData` | `frameData.Get<UniversalResourceData>()` |
| `ConfigureInput(Color)` | `builder.UseTexture` 声明 |

## 核心步骤

1. `frameData.Get<UniversalResourceData>()` → 取 camera color
2. `renderGraph.AddRasterRenderPass<PassData>(name, out passData)` → 添加 pass
3. `builder.UseTexture(tex, AccessFlags.Read)` → 声明输入
4. `builder.SetRenderAttachment(tex, 0, AccessFlags.Write)` → 声明输出
5. `builder.SetRenderFunc(...)` → Blitter.BlitTexture
6. 多 pass: `UniversalRenderer.CreateRenderGraphTexture(...)` → 临时 RT

## Compute Pass

`renderGraph.AddComputePass<PassData>(...)` → `SetRenderFunc` → `cmd.DispatchCompute`
