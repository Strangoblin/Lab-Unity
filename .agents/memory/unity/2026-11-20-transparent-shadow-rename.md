# ColoredShadow → TransparentShadow 改名与透明玻璃接入（2026-11-20）

## 改名范围

- 目录 `Special/Renderer/ColoredShadow/` → `Special/Renderer/TransparentShadow/`（目录 `.meta` 同步搬移）。
- `ColoredShadowFeature.cs` → `TransparentShadowFeature.cs`：类名 `TransparentShadowFeature`，
  内嵌 `TransparentShadowPass`，默认 `shaderTag = "TransparentShadow"`，Profiling / RT 名
  `Transparent Shadow Map` / `_TransparentShadowMap`。
- `ColoredShadowFunction.hlsl` → `TransparentShadowFunction.hlsl`：函数
  `TransparentShadow_Transmission(positionWS)`，宏 `TRANSPARENT_SHADOW_FUNCTION_INCLUDED`。
- 全局：`_TransparentShadowTex` / `_TransparentShadowWorldToLight` / `_TransparentShadowParams` /
  `_TransparentShadowDepth`。
- `.meta` 均为 `git mv`，GUID `1d1550d7…` / `627af4e9…` 不变；`ColorfulWindowTestRenderer.asset`
  只需同步 `m_Name`、`m_EditorClassIdentifier` 与 `shaderTag`。
- 引用同步：`ColoredGlass.shader`、`ColorfulWindowGround.shader`、`SSL.shader` 的 include / 函数调用；
  `ColorfulWindow.md`、`SSL.md`、`Special/Renderer/README.md`。

## 玻璃层约定（2026-11-20 最终）

- 不做按名字解析：`Settings.layerMask` 直接按索引选层，由使用者手动把它对齐到
  `TransparentWithShadow` 层（当前 Layer 8）。新增或移动层时同步改掩码即可。
- `layerMask` 与 `passEvent` 同属 `[Header("Event")]` 组（空间范围 × 帧内时机），与
  MaskBlur / MaskRBR 的 Event 面板约定一致；`layerMask == 0` 时 Warn 一次。
- 测试资产 `layerMask` 仍指向 Layer 30，兼容现有测试场景；测试场景迁移到 Layer 8 后改回即可。
- `PC_Renderer.asset` 的 `m_OpaqueLayerMask` / `m_TransparentLayerMask` **保持默认 247 不动**
  （用户明确要求）。因此该掩码之外的层（含 Layer 8）不会由主 Renderer 正常绘制；
  `TransparentWithShadow` 层要正常显示，必须按 URP 常规方式把该层加进 Renderer 掩码与相机
  Culling Mask，Feature 不代改 Renderer 配置。
- 普通透明渲染与透射图相互独立：可见窗面走普通透明队列；Feature 只额外把该层物体画进透射图。
- `ProjectSettings/TagManager.asset` 的层名由用户手动维护（`ProjectSettings/` 未入库、写保护）。

## 测试玻璃 Shader

- `ColoredGlass.shader` 的可见 Pass 由 Opaque/SRPDefaultUnlit 改为普通透明：
  `RenderType/Queue = Transparent`、`LightMode = UniversalForward`、`Blend SrcAlpha OneMinusSrcAlpha`、
  `ZWrite Off`、`ZTest LEqual`、`Cull Off`，由 URP 正常距离排序。
- 透明度取 `_BaseMap.a × _BaseColor.a`；Pane 材质当前 alpha = 1，需要半透明时在材质面板调低。
- `TransparentShadow` Pass 保持不变（`ZTest Always`，输出 RGB 透射 + A 归一化光空间深度），
  与可见 Pass 的队列/排序互不影响。
- 普通玻璃要进入透射图仍必须提供 `TransparentShadow` Pass；仅靠层无法凭空产生透射色与深度。

## 边界与验证

- 透射图仍为单层、`FindObjectsByType` 求包围盒、RendererList 受相机 culling 限制。
- 生产地面 PBRToon / `LightFunction.hlsl` 仍未接 `TransparentShadow_Transmission`；
  接收端只有 `ColorfulWindowGround.shader` 与 `SSL.shader`。
- 本次只做静态检查（全仓 grep 旧标识清零、GUID 引用未动），未运行 Editor 编译与出图。

## 2026-11-20 收敛到主管线

改名完成后演示没有再走独立测试 Renderer：

- `PC_Renderer` 接入 `TransparentShadowFeature`（`layerMask = 256`，Layer 8）与 `SSLFeature`（BeforeRenderingPostProcessing）；`PC_RPAsset` 移除 index 2 的测试 Renderer，`m_DefaultRendererIndex` 回到 0。
- `RenderScene.unity` 增加 Layer 8 的 `Plane (8)`，使用 `ColorfulWindowPane.mat`。
- `ColorfulWindowTest.unity`、`ColorfulWindowTestRenderer.asset`、`ColorfulWindowGround.mat` / `ColorfulWindowWall.mat` 删除；`ColorfulWindowGround.shader` 保留作接收端参考。上一节「测试资产 layerMask 仍指向 Layer 30」由此被取代。
- 主管线地面仍是 PBRToon，未接 `TransparentShadow_Transmission`；彩窗颜色目前只经 SSL 出现在体积光里。
