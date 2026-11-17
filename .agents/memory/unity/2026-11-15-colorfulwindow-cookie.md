# 彩窗光空间彩色透射图（2026-11-15）

从主光视角把透射玻璃层渲染成光空间 RT（RGB 透射 + 归一化深度），地面与 SSL 用深度比较判断玻璃前后。
主光彩色 Cookie + 单窗平面 mask 方案同日退役，保留在 Git 历史与遗留资产中。

## 已验证的数据事实（URP 17.3）

- 主光 Cookie 是光空间 2D 投影 + 矩阵，没有深度；`Light.cookie` 只调制 `light.color`，
  不能区分玻璃前后。主光 cookie 采样用纹理自身 wrap；Clamp 边缘黑像素会变成大片伪阴影（历史问题）。
- 阴影空间是光源对齐的深度图，来自覆盖主相机视锥的阴影相机；Cookie/透射图的包围盒来自主光 transform +
  尺寸/偏移，二者不共享相机或边界。
- 彩色透射图是**场景渲染输出**：一个 Pass 收集 Layer 内 Renderer，从主光视角渲染 RGB+深度。
  与输入型 Cookie 方向相反：Cookie 是光 → 纹理，透射图是物体 → 光空间纹理 → 接收者。

## 实现

- `Assets/Mine/Special/Renderer/TransparentDepth/ColoredShadow/`
  - `ColoredShadowFeature.cs`：`BeforeRenderingOpaques` Raster Pass；Layer 内 Renderer world bounds 并集 →
    光空间包围盒（padding 1.25、depthPadding 20）；`FindObjectsByType` 每帧重算，支持动态玻璃；
    发布 `_ColoredShadowTex`（ARGBHalf：RGB 透射、A 归一化深度，背景白 A=1）、
    `_ColoredShadowWorldToLight`、`_ColoredShadowParams`、`_ColoredShadowDepth`。
  - `ColoredShadowFunction.hlsl`：`ColoredShadow_Transmission(positionWS)`；UV 越界/无玻璃/玻璃前回白，
    玻璃后取玻璃色；循环内 `SAMPLE_TEXTURE2D_LOD`。
  - `README.md`：契约与单层/相机 culling/分辨率限制。
- `ColorfulWindow/ColoredGlass.shader`：Forward Unlit 可见窗面 + `LightMode="ColoredShadow"` Pass
  （`ZTest Always`，输出 `float4(color, normalizedDepth)`）。
- `ColorfulWindowGround.shader`：主光 shadow × `ColoredShadow_Transmission`；保留 ShadowCaster。
- SSL：单模式 HG Light；去掉 Cookie/`_LIGHT_COOKIES`/窗平面；每步先 shadow，`shadow<=0` 跳过，
  再取透射色；相位每像素一次；固定 `/32` 质量归一化。
- 测试场景：Pane Layer 30；`ColorfulWindowPane.mat` 用 `Render/ColoredGlass`；主光 `cookie=null`；
  测试 Renderer 挂 `ColoredShadowFeature` + `SSLFeature`（PC_RPAsset list index 2，默认 Renderer 用户保留为 2）；
  地面/墙体测试 Shader 直接读透射图。生产地面（PBRToon / `LightFunction.hlsl`）仍未接入。

## Y 翻转与深度比较（2026-11-15 后续）

- **现象**：原 `n(p) > glass.a` 在 D3D12 下玻璃深度梯度上下翻，表现为一半漏光、一半近处无光；
  `1 - n(p) < glass.a` 只是把深度关于 0.5 镜像，深度边界看似修好，颜色图仍上下翻。
- **检查**：D3D12，`SystemInfo.usesReversedZBuffer = True`；彩色图是纯颜色 RT（`DepthBits.None`），
  A 通道是原始归一化光空间 z，没有光源相机/深度附件，所以 **reversed-Z 不参与彩色图比较**；
  URP opaque shadow map 才走 reversed-Z。
- **根因**：玻璃 Pass 用自定义 clip（`positionCS = float4(uv*2-1, 0.5, 1)`），没有相机 projection 的 Y 翻转；
  D3D RT 的 V 方向与自定义 clip 不一致，玻璃颜色/深度图上下翻。
- **修复**：接收端在 `UNITY_UV_STARTS_AT_TOP` 时 `uv.y = 1 - uv.y`（颜色和玻璃深度一起采样）；
  恢复物理比较 `n(p) > glass.a + bias`；`TextureDesc.depthBufferBits = DepthBits.None` 显式化。
- **证据**：`coloredshadow-flip-inside/outside/dynamic.png`；动态图的白色带是窗洞未被玻璃覆盖的
  **无玻璃区**（透射图背景白），不是玻璃漏光。

## 证据

`tmp/sessions/2026-11-15-colorfulwindow-cookie-01/output/`：

- `coloredshadow-inside.png`：室内彩色地面光斑 + 彩色体积光柱。
- `coloredshadow-outside.png`：室外白色阳光，无彩色光柱穿墙。
- `coloredshadow-dynamic.png`：玻璃平移 0.3 + 旋转 12°；彩色图案跟随玻璃，窗洞未覆盖部分透出白色阳光。
- `ssl-quality-low/medium/high.png`：三档 Debug 均值 10.14 / 10.63 / 10.63。

Shader 编译：SSL Pass 0、地面 Forward/ShadowCaster、玻璃 Unlit/ColoredShadow 均 `ShaderUtil` 无消息；
C# `ColoredShadowFeature` 类型编译通过。

## 当前边界

- **单层**：透射图只存一个颜色/深度；玻璃 Pass `ZTest Always`，重叠玻璃以最后绘制者为准。
- **RendererList 使用主相机 culling**：相机看不到的玻璃不会进入该相机透射图。
- 包围盒来自层内 Renderer world bounds 并集，`FindObjectsByType` 每帧；适合少量动态玻璃。
- 光空间 RT 1024²；玻璃自身屏幕像素不受影响，地面/体积细节受 RT 分辨率限制。
- 生产地面尚未接入接收函数；旧 Cookie 资产/`ColorfulWindowLight.cs` 留在功能目录作参考，不再被场景读取。
- 全量 C# 编译依赖 `InteractionManager/InteractionProcessor.cs`；当前该项目文件缺失，
  Sand/Snow 已被用户移到 `~` 目录以绕过，编译才通过。恢复交互系统时需先补回基类。
