# RTHandle API 速查

> Unity 6 URP 纹理生命周期管理。推荐使用 RTHandle 而非裸 RenderTexture。

---

## 创建 RTHandle

```csharp
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 现代方式 — Unity 6
RTHandle myTexture;

// 首次创建或重新分配
void EnsureTexture(int width, int height)
{
    if (myTexture == null)
    {
        myTexture = RTHandles.Alloc(
            width, height,
            colorFormat: GraphicsFormat.R8G8B8A8_UNorm,
            depthBufferBits: DepthBits.None,
            filterMode: FilterMode.Bilinear,
            wrapMode: TextureWrapMode.Clamp,
            name: "MyTexture"
        );
    }
    else
    {
        // ⚠️ Unity 6 用 RenderingUtils
        RenderingUtils.ReAllocateIfNeeded(ref myTexture, 
            new Vector2Int(width, height),
            GraphicsFormat.R8G8B8A8_UNorm);
    }
}

// 释放
void DisposeTexture()
{
    myTexture?.Release();
    myTexture = null;
}
```

## 常用格式

| GraphicsFormat | 用途 | 每像素大小 |
|----------------|------|-----------|
| `R8G8B8A8_UNorm` | 标准颜色 | 4 bytes |
| `R16G16B16A16_SFloat` | HDR 颜色 | 8 bytes |
| `R32_SFloat` | 单通道浮点（深度） | 4 bytes |
| `R8_UNorm` | 单通道（mask） | 1 byte |
| `R16G16_SFloat` | 双通道浮点（法线） | 4 bytes |

## 注意

- RenderGraph 模式下，纹理生命周期由 graph 管理，不要手动 release
- `RenderingUtils.ReAllocateIfNeeded` 只在尺寸变化时才重建
- `RTHandle.rt` 可以获取底层 `RenderTexture` 对象（调试用）
