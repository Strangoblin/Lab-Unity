# Blitter API 速查

> Unity 6 URP 17+ 全屏后处理绘制 API。替代旧版 `cmd.Blit()`。

---

## 核心 API

```csharp
using UnityEngine.Rendering;

// 1. 最基本的 Blit — 全屏绘制材质
Blitter.BlitTexture(
    CommandBuffer cmd,
    RTHandle source,        // 源纹理
    Vector2 scale,          // 缩放 (Vector2.one = 无缩放)
    Material material,      // 包含全屏 shader 的材质
    int pass = 0            // shader pass index
);

// 2. Blit + 自定义缩放
Blitter.BlitTexture(
    cmd,
    source,
    new Vector4(1, 1, 0, 0), // scaleBias
    material,
    pass
);

// 3. 直接绘制 camera target
Blitter.BlitCameraTexture(
    cmd,
    source,
    destination,
    material,
    pass
);
```

## 旧 vs 新

```csharp
// ❌ Unity 2022 旧写法
cmd.Blit(sourceRT, destRT, material, pass);

// ✅ Unity 6 新写法
Blitter.BlitTexture(cmd, sourceHandle, Vector2.one, material, pass);
```

## 注意事项

- `Blitter` 需要 `using UnityEngine.Rendering;`
- `source` 参数类型是 `RTHandle`，不是 `RenderTexture`
- 在 RenderGraph 的 `SetRenderFunc` 回调中使用（此时 cmd 是 `RasterGraphContext.cmd`）
- 不需要手动 SetRenderTarget（RenderGraph 已处理）
