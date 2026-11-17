# 草与地形写入深度 / 法线（2026-11-11）

目标：让草和 `TerrainManager` 地形都出现在 `_CameraDepthTexture` 与 `_CameraNormalsTexture`，为 SSGI 供数据。两者此前**都不可见**，原因不同。

## 前提：ForcePrepass 决定「没有 pass 就等于不存在」

`PC_Renderer.asset` → `PC_RPAsset` 的 `m_RequireDepthTexture: 1`、`m_CopyDepthMode: ForcePrepass`、深度预渲染关闭。此模式下 `_CameraDepthTexture` **只由 prepass 产生**，而 URP 的 `DepthOnlyPass` / `DepthNormalOnlyPass` 是按 **shader 的 `LightMode` tag** 选 pass 的，属于绘制层而非相机层：没写 `DepthOnly`（或 `DepthNormals`）pass 的材质，前向照常出图，深度/法线缓冲里却是空的。SSGI / AO 会把这块表面当成无限远而漏光。

另一个易漏点：`_CameraNormalsTexture` **不是常驻的**——只有某条 pass 声明 `ConfigureInput(... | ScriptableRenderPassInput.Normal)`（本项目即 SSGI）时才生成，否则只有 `DepthOnly`。所以 prepass 绘制必须同时准备 `DepthOnly` 与 `DepthNormals` 两套 pass，运行时按 `resources.cameraNormalsTexture.IsValid()` 二选一。

## 地形：补 pass 即可

`TerrainSurface.shader` 原本只有 `Forward`（`UniversalForwardOnly`）。现在补：

| Pass | Tag | 关键点 |
|---|---|---|
| `DepthOnly` | `DepthOnly` | `ZWrite On` + `ColorMask 0`，fragment 只 `return 0` |
| `DepthNormals` | `DepthNormals` | 顶点用 `TerrainMeshBuilder` 写入的平滑法线；`Frag_Normals` 按 `_GBUFFER_NORMALS_OCT` 分支 |

法线 fragment 与 URP 内建一致：`_GBUFFER_NORMALS_OCT` 打开时 `PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5))`，否则直接 `half4(normalWS, 0)`。项目里默认编译的是非 OCT 路径（`R8G8B8A8_SNorm` 存 3 分量）。

## 草：必须把绘制搬进 pass 序列

草不是场景 Renderer，进不了 `CullResults`；而且原实现走 `Graphics.DrawMeshInstancedIndirect` + `beginContextRendering`，那些 queued draw 只在 `RenderSingleCamera` 末尾的 `context.Submit()` 落地，**跑在整条 pass 序列之外**——既进不了 prepass，也进不了后处理。改法：

```
GrassChunkManager.BeginContextRendering
    GrassDrawRequests.Clear() → UpdateChunks → RefreshVisibility
    每个可见块 chunk.Publish(mesh, material, layer) → GrassDrawRequests.Add(mesh, material, argsBuffer, mpb, layer)

GrassRenderFeature（挂在 PC_Renderer.asset）
    AfterRenderingPrePasses → GrassDepthNormalsPass：cmd.DrawMeshInstancedIndirect(..., "DepthNormals"/"DepthOnly")
    AfterRenderingOpaques   → GrassOpaquePass：cmd.DrawMeshInstancedIndirect(..., "Forward")
```

- Manager 只负责每帧**重建请求表**，不再自己绘制；`Publish` 取代了原来的 `Draw`（`VisibleCount == 0` 时不入表），`ClearChunks` 先 `GrassDrawRequests.Clear()`。
- `Gather()` 在 feature 里按相机 `cullingMask` 过滤，并用 `Material.FindPass(passName)` 解析 PassIndex（`DrawMeshInstancedIndirect` 的 `shaderPass` 要的是索引，不是名字）；缺失的 pass 名用静态 `HashSet` 只告警一次。
- Feature 跳过 Overlay 相机、非 Game/Scene 相机，以及请求表为空的情况（其它场景开着 feature 也不会白画）。

### 为什么是 AfterRenderingPrePasses，不是 BeforeRenderingPrePasses

RG 路径的自定义 pass 落点是固定的：`RecordCustomRenderGraphPasses` 在 `BeforeRenderingPrePasses`(UniversalRenderer.cs:1011)、`AfterRenderingPrePasses`(1130)、`AfterRenderingOpaques`(1260)、`AfterRenderingSkybox`(1275)。URP 自己的 prepass 带 `ConfigureClear(ClearFlag.All, Color.black)`（`DepthNormalOnlyPass` 旧路径）或 RG 版 `SetRenderAttachment(cameraNormalsTexture, Write)` + `SetRenderAttachmentDepth(depthTexture, ReadWrite)`，**放在它前面会被 Clear 抹掉**。`AfterRenderingOpaques` 则保证光照/阴影全局已就绪，且早于天空盒、SSGI 与后处理。

### RG 绑定的具体写法

```csharp
builder.SetRenderAttachment(resources.cameraNormalsTexture, 0, AccessFlags.Write);   // 仅 withNormals 时
builder.SetRenderAttachmentDepth(resources.cameraDepthTexture, AccessFlags.ReadWrite);
builder.SetGlobalTextureAfterPass(resources.cameraDepthTexture, CameraDepthTextureId);
builder.SetGlobalTextureAfterPass(resources.cameraNormalsTexture, CameraNormalsTextureId); // 仅 withNormals
builder.AllowGlobalStateModification(true);
```

同一个 pass 里把法线写进颜色附件、深度写进深度附件，所以「法线差异」的读数同时证明了深度那次绘制发生在管线内。

## 两个实现坑

- **`ScriptableRenderPass.profilingSampler` 是 protected**：在 feature 的对象初始化器里写 `new GrassDepthNormalsPass { profilingSampler = ... }` 会 CS0122，必须放进 pass 自己的构造函数。
- **`RenderTexture.GetTemporary` 重载空洞**：`(w, h, GraphicsFormat, GraphicsFormat)` + 想再给 `RenderTextureReadWrite` 的写法不存在，会静默变成 `(int, int, GraphicsFormat, RenderTextureMemoryless)`。用 `new RenderTextureDescriptor(w, h, colorFormat, depthStencilFormat) { msaaSamples = 1, sRGB = true }` 再 `GetTemporary(descriptor)`。

## 验证（`Validation~/GrassDepthNormalsChecks.cs`）

差分法：同一相机渲染两次（feature 开 / 关），各读回一次 `_CameraNormalsTexture`，统计变化像素；再统计法线朝上的像素比例确认地面本身也在缓冲里。

```
GRASS_DEPTH_NORMALS_CHECK: requests=6, normalsChanged=59210 (6.42 %), maxChannelDelta=255,
                           groundNormalRatio=49.12 %, size=1280x720
GRASS_DEPTH_NORMALS_CHECK_PASS
```

默认路径（SSGI 关闭 → 只有 `DepthOnly`）另有一次冒烟：`GRASS_DEFAULT_PATH_CHECK_PASS: ssgiActive=False, normalsTexture=True, gpuProgram=Metal`，控制台无 RG / 管线报错。证据图：`Validation~/2026-11-11-depth-normals/{feature-on,feature-off,feature-on-ssgi-off}.png`。

### 读回法线的三条弯路

- **`Shader.GetGlobalTexture("_CameraNormalsTexture")` 渲染后是 `UnityBlack` 4×4**：URP 会在每帧结束把全局纹理还原。RG 管理的 RT 只能用 `Resources.FindObjectsOfTypeAll<RenderTexture>()` 按名字前缀 `_CameraNormalsTexture_{W}x{H}` + `graphicsFormat == R8G8B8A8_SNorm` 去找。第一次验证读到的 `normalsChanged=0` 就是这个坑。
- **SNORM 解码要按有符号字节**：`(raw > 127 ? raw - 256 : raw) / 127f`，不是 `b/255*2-1`。用错公式时朝上比例恒为 0.00 %，改对后 49.12 %。
- **深度无法数值读回**：`AsyncGPUReadback` 直接拒绝 `D32_SFloat_S8_UInt`（"None (0) graphics format is not supported as source for async read back"）。深度只能靠结构证据（同一 draw 把 `cameraDepthTexture` 当深度附件且 `ZWrite On`）+ 法线差分间接成立，**不要声称「已读回深度」**。

## 遗留

- 草在阴影贴图 / `RenderObjects` feature 里仍不可见；要覆盖得再补相应时刻的 pass。
- 移动端 / 非 ForcePrepass 配置未测；若改成 `m_CopyDepthMode` 非 2，`DepthOnly` pass 仍是必要的（法线缓冲与深度预渲染都按 tag 选 pass），只是深度来源会多一条拷贝路径。

相关：[草色改采地形颜色图](2026-11-11-grass-terrain-colormap.md)（本轮修掉其中的「未解决」）、[草接入地形](2026-11-09-terrain-grass-integration.md)、[CustomRenderer → CustomFeature](2026-11-04-customrenderer-to-feature.md)（同为 RG 自录 pass 的前例）。
