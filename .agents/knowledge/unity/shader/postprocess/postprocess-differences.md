# Unity 6 URP 17+ 全屏后处理 Shader 差异总结

> 内化自 Assets/MarkDowns/（2026-08-24，meta-developer 迁移；原为 memory 格式，转为 reference 文档）
> 基于将旧版 Kuwahara 后处理迁移到 Unity 6 的实战经验，对比新管线与 Unity 2022 URP 的所有关键差异。

---

## 一、Shader 侧变更

### 1.1 Vertex Shader — 全屏绘制方式

| Unity 2022 (旧) | Unity 6 (新) |
|---|---|
| 自定义 `Vert`，接受 `POSITION` 语义，通过 `TransformObjectToHClip(input.positionOS)` 变换 | `Blit.hlsl` 提供的 `Vert`，接受 `SV_VertexID`，通过 `GetFullScreenTriangleVertexPosition` 生成全屏三角形 |

```hlsl
// 旧（不再工作）
struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
Varyings Vert(Attributes input) {
    output.positionCS = TransformObjectToHClip(input.positionOS);
    output.uv = input.uv;
}

// 新（使用 Blit.hlsl）
// 不需要自定义 Attributes/Varyings/Vert
// #include "Blit.hlsl" 即可，然后 #pragma vertex Vert
```

> **原因**：`Blitter.BlitCameraTexture` 内部用 `DrawTriangle` 绘制**全屏三角形**（3 顶点，`SV_VertexID` 索引），而非旧版 `cmd.Blit` 的 4 顶点全屏四边形。

### 1.2 纹理绑定 — `_MainTex` → `_BlitTexture`

| Unity 2022 (旧) | Unity 6 (新) |
|---|---|
| `cmd.Blit(source, dest, material)` 自动设置 `_MainTex` | `Blitter.BlitCameraTexture` 通过 `MaterialPropertyBlock` 设置 `_BlitTexture` |

```hlsl
// 旧
TEXTURE2D_X(_MainTex);
float3 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv).rgb;

// 新（Blit.hlsl 已声明 TEXTURE2D_X(_BlitTexture)）
float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
```

> **例外**：如需使用 `_MainTex`（如 SSO Composite Pass），必须在 C# 中手动设置：
> ```csharp
> cmd.SetGlobalTexture("_MainTex", data.tempMain);
> ```

### 1.3 必须包含 Blit.hlsl

```hlsl
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
```

该文件提供：

| 符号 | 说明 |
|---|---|
| `TEXTURE2D_X(_BlitTexture)` | 输入纹理声明 |
| `float4 _BlitScaleBias` | 缩放偏移参数 |
| `Vert(Attributes input)` | 全屏三角形顶点函数 |
| `Attributes` / `Varyings` | 输入输出结构体 |
| `sampler_LinearClamp` 等 | 全局采样器（通过 `GlobalSamplers.hlsl`） |

### 1.4 Varyings 命名 — `uv` → `texcoord`

Blit.hlsl 定义的结构体中字段名为 `texcoord`：

```hlsl
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 texcoord   : TEXCOORD0;    // 注意：是 texcoord，不是 uv
    UNITY_VERTEX_OUTPUT_STEREO
};
```

```hlsl
// 旧
half4 Frag(Varyings input) : SV_Target { float2 uv = input.uv; }

// 新
half4 Frag(Varyings input) : SV_Target { float2 uv = input.texcoord; }
```

### 1.5 Render States 必须显式设置

旧版 `cmd.Blit` 内部自动设置渲染状态，新版需在 Pass 中显式声明：

```hlsl
Pass
{
    ZWrite Off
    ZTest Always
    Cull Off
    Blend One Zero
}
```

> 不设置会导致深度测试不通过或混合错误，Pass 输出黑色。

### 1.6 SubShader Tag

建议加上管线标签，避免与 Built-in 渲染管线冲突：

```hlsl
Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
```

### 1.7 Properties 区块

`Properties` 中的 `_MainTex` 等**不再影响纹理绑定**，仅用于 Inspector 显示。实际绑定由 `Blitter.MaterialPropertyBlock` 或 `cmd.SetGlobalTexture` 驱动：

```hlsl
Properties
{
    _MainTex ("Base Color", 2D) = "white" {}   // 仅 Inspector 显示
    _Radius ("Radius", Range(1, 10)) = 5
}
```

---

## 二、C# 侧变更

### 2.1 RenderGraph 替代 Execute

| 方面 | Unity 2022 | Unity 6 |
|---|---|---|
| Pass 方法 | `Execute(ScriptableRenderContext, ref RenderingData)` | `RecordRenderGraph(RenderGraph, ContextContainer)` |
| 资源获取 | `renderingData.cameraData.renderer.cameraColorTargetHandle` | `frameData.Get<UniversalResourceData>().activeColorTexture` |
| 临时 RT | `RTHandle` + `ReAllocateHandleIfNeeded` | `TextureHandle` + `UniversalRenderer.CreateRenderGraphTexture` |
| 资源释放 | 手动 `tempRT?.Release()` | RenderGraph 自动管理 |
| Blit 调用 | `cmd.Blit(source, dest, material)` | `Blitter.BlitCameraTexture(cmd, source, dest, material, pass)` |
| 缓冲区分配 | `CommandBufferPool.Get()` | `AddUnsafePass<T>()` 内通过 `CommandBufferHelpers.GetNativeCommandBuffer()` |
| 属性传递 | 直接设置 | 通过 `PassData` 类传入 `SetRenderFunc<T>` |

### 2.2 绘制物体 API

| API | 可用 | 说明 |
|---|---|---|
| `CommandBuffer.DrawRenderers` | ❌ | 已移除 |
| `RasterCommandBuffer.DrawRenderers` | ❌ | 不存在 |
| `RasterCommandBuffer.DrawRendererList` | ✅ | RenderGraph 路径，需预构建 `RendererList` |
| `ScriptableRenderContext.DrawRenderers` | ✅ | 兼容模式 `Execute()` 路径 |

**RenderGraph 正确做法**：

```csharp
// 1. 构建 RendererList（在 RecordRenderGraph 中）
var drawSettings = new DrawingSettings(
    new ShaderTagId("UniversalForward"), new SortingSettings(camera));
drawSettings.overrideMaterial = overrideMaterial;
var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
RendererListHandle rlHandle = renderGraph.CreateRendererList(
    ref drawSettings, ref filterSettings);

// 2. 在 RasterRenderPass 中使用
builder.UseRendererList(rlHandle);
builder.SetRenderFunc((data, ctx) => {
    ctx.cmd.DrawRendererList(data.rendererList);
});
```

**兼容模式做法**：

```csharp
public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
{
    var drawSettings = new DrawingSettings(...);
    context.DrawRenderers(cullingResults, ref drawSettings, ref filterSettings, ref rsb);
}
```

> Unity 内置 Shadow Caster 使用 `CreateShadowRendererList`（引擎内部 API，自动筛选 `LightMode="ShadowCaster"` 的 Pass），然后 `cmd.DrawRendererList`。

### 2.3 CullingResults 获取

`CullingResults` 仅在 `AddRenderPasses` 的 `renderingData.cullResults` 中可用，`RecordRenderGraph` 无此参数。如需使用，在 `AddRenderPasses` 时预先存储。

### 2.4 关键字 API 变更

| API | Unity 2022 | Unity 6 |
|---|---|---|
| `cmd.EnableKeyword(string)` | ✅ | ❌ 参数改为 `GlobalKeyword` |
| `cmd.DisableKeyword(string)` | ✅ | ❌ 同上 |
| `ShaderKeywordStrings` | `public` | `internal` |

```csharp
// Unity 6
cmd.EnableKeyword(new GlobalKeyword("_SOME_KEYWORD"));
cmd.DisableKeyword(new GlobalKeyword("_SOME_KEYWORD"));
```

### 2.5 RenderGraph 全局纹理设置

`RasterCommandBuffer.SetGlobalTexture` 只接受 `TextureHandle`。对外部 `RenderTexture`，需使用 `SetGlobalTextureAfterPass`：

```csharp
// ❌ cmd.SetGlobalTexture("_Tex", renderTexture);
// ❌ cmd.SetGlobalTexture("_Tex", rtHandle);
// ✅
TextureHandle handle = renderGraph.ImportTexture(rtHandle);
builder.SetGlobalTextureAfterPass(handle, Shader.PropertyToID("_Tex"));
```

### 2.7 ClearRenderTarget 受 Viewport 影响

旧版 `CommandBuffer.ClearRenderTarget` 不受 viewport 影响，始终清整张 RT。Unity 6 的 `RasterCommandBuffer.ClearRenderTarget` **会受当前 viewport 影响**，仅清除 viewport 范围内区域。

```csharp
// ❌ 错误：Clear 时 viewport 可能是上一步的残留值，仅清除了部分区域
builder.SetRenderFunc((data, ctx) =>
{
    ctx.cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 1f, 0);
    ctx.cmd.SetViewport(new Rect(0, 0, res, res));  // ← 太晚了
    ctx.cmd.DrawRendererList(data.rendererList);
});

// ✅ 正确：先设 viewport，再 clear
builder.SetRenderFunc((data, ctx) =>
{
    ctx.cmd.SetViewport(new Rect(0, 0, res, res));  // ← 先设全屏 viewport
    ctx.cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 1f, 0);
    ctx.cmd.DrawRendererList(data.rendererList);
});
```

> **症状**：RT 上出现分块残留（旧帧数据未被清除的区域）和拖尾（上一帧内容残留）。

### 2.8 RTHandle 包装外部 RenderTexture

`Blitter.BlitTexture` 需要 `RTHandle`，外部 `RenderTexture` 需包装：

```csharp
private RTHandle m_Handle;
m_Handle = RTHandles.Alloc(m_RenderTexture);
Blitter.BlitTexture(cmd, m_Handle, Vector2.one, material, 0);
```

---

## 三、跨平台注意事项

### 3.1 Reversed-Z 深度判断

Metal 使用 reversed-Z（near=1, far=0），`rawDepth >= 0.9999` 只在常规 Z 下生效。跨平台统一用 `Linear01Depth`：

```hlsl
// ✅ 跨平台兼容
float linearDepth = Linear01Depth(rawDepth, _ZBufferParams);
if (linearDepth > 0.9999) return;  // 天空
```

### 3.2 SetRenderFunc Lambda 闭包陷阱

`RecordRenderGraph` 中的局部变量被 `SetRenderFunc` lambda 捕获后，lambda **延迟执行**时栈变量已失效，读取到垃圾值：

```csharp
// ❌ 错误：viewM 在 RecordRenderGraph 返回后失效
public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
{
    Matrix4x4 viewM = ComputeView();  // 栈局部变量
    Matrix4x4 projM = ComputeProj();

    builder.SetRenderFunc((data, ctx) =>
    {
        ctx.cmd.SetViewProjectionMatrices(viewM, projM); // ← Lambda 捕获的 viewM/projM 已失效!
    });
}

// ✅ 正确：通过 PassData 传递
class PassData { public Matrix4x4 view, proj; }

public override void RecordRenderGraph(...)
{
    var passData = new PassData();
    passData.view = ComputeView();   // 存入堆对象
    passData.proj = ComputeProj();

    builder.SetRenderFunc((data, ctx) =>
    {
        ctx.cmd.SetViewProjectionMatrices(data.view, data.proj); // ← 从 PassData 读取
    });
}
```

> **症状**：阴影完全错位、随视角闪烁、纹理剧烈拉伸——所有由错误矩阵导致的渲染异常。

> **原则**：任何 `SetRenderFunc` lambda 用到的变量，**必须**存入 `PassData`（分配在堆上），不能依赖 lambda 对栈变量的捕获。

---

## 四、快速排查清单

全屏后处理 Pass 输出黑色时，按顺序检查：

1. [ ] **Vertex Shader**：使用 `Blit.hlsl` 的 `Vert`（`SV_VertexID` 全屏三角形）？
2. [ ] **纹理名**：使用 `_BlitTexture` 而非 `_MainTex`？
3. [ ] **包含 Blit.hlsl**：HLSLINCLUDE 中添加了 `#include "Blit.hlsl"`？
4. [ ] **texcoord**：Fragment 中用 `input.texcoord` 而非 `input.uv`？
5. [ ] **Render States**：Pass 中设置了 `ZWrite Off, ZTest Always, Cull Off`？
6. [ ] **C# RecordRenderGraph**：使用 `Blitter.BlitCameraTexture` 而非 `cmd.Blit`？
7. [ ] **TextureHandle**：使用 `TextureHandle` 而非 `RTHandle`？
