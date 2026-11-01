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
- **URP 兼容模式下 vertex shader 传 texcoord 不可靠**：全屏或烘焙 shader 改用 `positionSS : SV_Position`，在 fragment 内换算 UV
- **一次性烘焙不要用 `cmd.Blit`**：`CommandBuffer.DrawProcedural` 才能保证全屏三角形实际执行（LUT / 预积分这类"跑一遍就出图"的工具）
- 纹理存在性检测不要依赖 `GetDimensions`：未绑定 `TEXTURE2D` 时默认纹理尺寸不稳定，用显式全局浮点开关（如 `_UseFGDLut`）切换分支
