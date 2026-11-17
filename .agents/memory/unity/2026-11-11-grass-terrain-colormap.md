# 草的颜色改为采样地形颜色图（2026-11-11）

`GrassWind.shader` 的 basecolor 从「基础色梯度 × 风色 × 屏幕空间地面色」的多色混合，改成单一来源：**地形颜色图**。

## 最终公式

```hlsl
half4 grassTex  = _Grass.Sample(sampler_Grass, i.uv);
clip(grassTex.a - _AlphaClipThreshold);
half4 groundColor = _TerrainColorMap.Sample(sampler_TerrainColorMap, i.groundUV);
half4 basecolor = groundColor * (i.height + 1.0) * grassTex;
```

`i.height` 是**运动后**的高度，不是 `uv.y`：顶点着色器先算出位移后的世界位置 `displacedWS = grassPos + fieldWind + terrain + TBN偏移`，再取 `dot(displacedWS - instancePos, grassNormal)`——相对实例根部、沿叶片朝向的投影，单位米。草被风吹弯或踩下时高度变低、颜色变暗，回弹后恢复；静止时根部 0、叶尖等于实际叶片长度（demo 网格 0.2 m × size 4 ≈ 0.8 m）。这个量必须在 vert 里算并插值到 frag，frag 里重算位移等于把风场采样做两遍。删掉的东西：`_Base_Color_A/B`、`_Wind_Color_A/B`、`_GroundColorTexture`、`_GroundColorBlend`、`_SceneColorTex` 及其 `cameraUV`；`.mat` 里残留的旧属性 Unity 下次保存时自动丢弃，不影响。

## 链路

`TerrainSurfaceRenderer.ColorMap` → `TerrainManager.SurfaceColorMap` → `GrassTerrainSource.TryGetGroundColor(out GrassGroundColorSource)`（新加的虚方法，基类默认 false）→ `TerrainGrassSource` 覆写成 `(ColorMap, grid.Origin.xz, grid.WorldSize)` → `GrassChunk` 构造时写进 `MaterialPropertyBlock` 的 `_TerrainColorMap` / `_TerrainColorMapTransform`（xy = 世界原点，zw = 世界尺寸）。

**必须用实例根部 `instancePos.xz` 反算 UV，不能用位移后的顶点 `positionWS.xz`**：风偏移和踩踏偏移都在顶点上，用后者会让叶尖跨格串色。公式 `(worldXZ - origin) / size` 与 `TerrainMeshBuilder` 写的网格 UV 完全一致，这也是两条链路能共用一张图的前提。

色源缺失（如 `ProceduralGrassTerrain`）时绑 `Texture2D.whiteTexture` 与单位变换，shader 不分支。

## 为什么不走屏幕空间

`_SceneColorTex`（`_CameraOpaqueTexture`）在 C# 里从来没被设置过，`_GroundColorBlend` 也被硬写 0，是死链路；即便接上也是错的——采的是「屏幕像素背后最靠前的不透明物体」，草叶竖直薄片会取到远处物体或天空。颜色图是几何对应的，没有这个问题。

## 曾经的缺口（已于同日修复）

草原先走 `Graphics.DrawMeshInstancedIndirect` + `beginContextRendering`，不进 `CullResults`，对深度 prepass、`_CameraNormalsTexture`、阴影、后处理全部不可见；本项目 PC_Renderer 是 ForcePrepass，`_CameraDepthTexture` 只由 prepass 产生，草与地形都不在其中。**2026-11-11 已改为「`GrassDrawRequests` 请求表 + `GrassRenderFeature` 在管线内 `cmd.DrawMeshInstancedIndirect`」，shader 补了 `DepthOnly` / `DepthNormals`，地形 shader 同样补齐**——细节、坑与验证见 [草与地形写入深度 / 法线](2026-11-11-grass-terrain-depth-normals.md)。草在阴影贴图与 `RenderObjects` feature 里仍不可见，属于该记录里的遗留项。

相关：[旧体系退役](2026-11-11-terrain-legacy-removal.md)、[草接入地形](2026-11-09-terrain-grass-integration.md)。
