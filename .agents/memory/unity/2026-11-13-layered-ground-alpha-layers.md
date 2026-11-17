---
name: layered-ground-alpha-layers
description: 单网格多层地面的层数组设计——纯 alpha 叠层语义、Texture2DArray 权重、通道扩缩与实测代价
date: 2026-11-13
---

# 单网格多层地面：层数组 + 纯 alpha 叠层

`InteractionManager/Demo/LayeredGroundSurface.cs` + `Shaders/LayeredGround.shader`。
一块网格上混合任意数量地形层，**只着色一次**：所有层的 BRDF 参数按权重插值成一份输入，
再调 `InteractionGroundShade`。层数不增加光照成本。

## 语义：只有 alpha，没有高度

`weights = lerp(weights, oneHot(layer), alpha)`，各层权重和为 1。
等价于「总颜色 = lerp(当前颜色, 该层颜色, alpha)」，后刷的层自然叠在上面。
笔刷的 falloff（smoothstep）乘在 alpha 上。

**高度模型是错的**，已整体删除（`_HeightMap` / `_HeightBlend` / 细节噪声 / 覆盖掩码）。
当时的做法是「splat 高度 + 每层偏移量做加性偏置」，症状是交汇处沙地 weight/height 全 1
也盖不住雪：偏置**加在 splat 之外**，`snow = 0 + 0.5 = 0.5` 反而拿到 21% 权重。
中途用 `coverage = saturate(splat * 8)` 门控偏置修好了现象，但那是给错误的模型打补丁——
多层材料真正需要的只有 alpha。若将来某种层真的要高度（草），那是**该层自己的参数**，不是混合通道。

## 层数组

- `LayeredGroundLayerSettings { Name, BaseColor, ShadowTint, Smoothness, Metallic }`，
  `[SerializeField] List<> _layers`，上限 `MaxLayers = 8`（必须与 shader 的 `LAYER_MAX` 一致）。
- 组件是**唯一真相**：shader Properties 里不再有逐层颜色，全部走 `MaterialPropertyBlock`
  （`_LayerBaseColor[8]` / `_LayerShadowTint[8]` / `_LayerSmoothness[8]` / `_LayerMetallic[8]` / `_LayerCount`）。
  材质上只留 `_PatchStrength`。
- 权重是 `Texture2DArray`（R8，一层一片），shader 里 `SAMPLE_TEXTURE2D_ARRAY(..., i)` 循环混合，
  和仍为 0 时退回第 0 层。**需要 `#pragma target 4.5`**，否则纹理数组起不来。
- 笔刷层列表直接读 `surface.Layers` 生成 popup，`Paint(..., int layerIndex, alpha)`。
  加一层 = popup 多一项 + 笔画数据多一个通道，不需要改任何枚举（旧 `LayeredGroundLayer` 已删）。

## 笔画数据（`LayeredGroundPaintData`）

- 稀疏 16×16 tile，`Resolution 256`，每个 texel 存 N 个 float（N = 层数），默认第 0 层为 1。
- `EnsureLayerCount(n)`：增层补 0 通道；删层把剩余通道**重新归一化**。
  注意这会把残余放大：某 texel 原本「泥 99.8% + 沙 0.2%」，删掉泥后变成 100% 沙。
  这是 alpha 合成下「删掉最上层」的数学结果，不是 bug；全 0 时退到第 0 层。
- 旧场景兼容：`_layerCount` 字段初始化就是 3，旧 tile 的 stride 也正好是 3，
  加载后 `_layers` 为空会自动填 Grass/Sand/Snow 默认值——**用户已刷的 223 个 tile 无需重建场景**。

## 代价（实测，4 层）

- `Paint` 1.16 ms/笔（3 层旧实现 0.98 ms）：`Texture2DArray` 没有子矩形上传接口，
  只能 `SetPixels32(colors, layer)` **整片**上传，4 片 × 65536 像素。
- `Rebuild()` 4.45 ms；增/删层（resize + 全量重建）约 5.9 ms，一次性。
- 若层数继续涨，优先换 `ComputeBuffer` + 自定义采样，而不是继续加纹理片。

## 共用 BRDF 的 metallic

`InteractionGroundShade(...)` 新增 `metallic` 参数：`diffuse *= (1 - metallic)`、
高光染成 `lerp(white, albedo, metallic)`。Grass/Sand/Snow 三个单层 shader 传 `0.0`，
数学上与改动前逐位一致。

## 验证方式

- 只读审计用户场景：`Layers.Count`、`PaintData.LayerCount`、`PaintedTileCount`、指定 texel 权重。
- 临时 additive 场景（y=100 避免 z-fighting）跑增删层 / 半透明叠加 / 纹理片读回，再 `SubmitRenderRequest` 截图。
- 脚本清材质脏属性：`SerializedObject` 的 `m_SavedProperties.m_Floats/m_Colors/m_TexEnvs/m_Ints` 按
  `Shader.GetPropertyCount()` 白名单删（`m_Floats` 等要用 `FindPropertyRelative`）。

## 笔刷静默失效：`_layerPixels` 与纹理不同步

上线后遇到「笔刷完全没反应」：`Paint` 在 `_layerPixels == null` 时直接返回 false，
而 `_layerPixels` 的创建藏在「纹理需要重建」的分支里——一旦 `_layerWeights` 已经存在且
深度正确（域重载、局部重建、反复 `Initialize`），CPU 像素缓冲就永远不会补上。
`Rebuild` 同样因为 `_layerPixels == null` 直接 return，于是「不报错、不刷新、看起来笔刷坏了」。

修法两条，都要留：
1. **CPU 缓冲与 GPU 纹理分开判断**，各自校验长度；
2. `Paint` 入口走 `EnsureState()`，状态不全就先 `Initialize()` 一次再判。

配套的 UX 坑：笔刷默认选第 0 层（底色），落在底色上等于什么都没画，也会被当成「笔刷坏了」。
`OnEnable` 里默认选最后一层。

排查这类「点了没反应」先读四个字段的真实状态，别猜：
`_sample` 长度 / `_layerPixels` 是否 null / `_layerWeights.depth` / `_block` 是否 null。
另外脚本直接改运行时数据**不会**标记场景 dirty，也不会进 Undo——测试笔迹要用
「正向公式反解」还原（`old = (new - t*target) / (1-t)`），再和测试前的截图逐像素对比确认复原。

## 交互作为层参数（`LayeredGroundInteractionSource`）

每层多一组参数：`Interaction`（None/Grass/Snow/Sand）+ `ImprintDepth`（视差）、
`NormalStrength`（坡度）、`Darkening`（明暗）。Surface 走 MPB 推 `_LayerInteraction[8]` /
`_LayerImprintDepth[8]` / `_LayerNormalStrength[8]` / `_LayerDarkening[8]`。

shader 里不按层逐个算交互，而是先汇总成四组**按场**的量（`LayeredGroundFields`，
x=grass / y=snow / z=sand）：

- `weight` — Σ 绑定该场的层权重（决定这一场要不要参与）
- `depth`  — Σ w·落差（视差）
- `scale`  — Σ w·落差·法线强度（坡度）
- `dark`   — Σ w·明暗

于是「一个像素落在雪上就只有雪在动，落在过渡带按权重同时吃两边」是免费的：
同一套 alpha 权重既混合 BRDF，也混合交互场。每场都有 `weight > 0.002 && _Valid > 0`
的 early-out，未使用的场（绝大多数像素）不付采样和视差的代价。

沙的细波纹乘 `weight.z` 淡入；草地在层上只做压暗（草叶本身由草系统处理）。
坡度最后走 `ApplyInteractionSlope` 叠回网格法线，网格不是水平面也不丢朝向。

已验证（临时场景 + 伪造交互场）：三层各自只响应自己的场、互相不串（雪 0.194 / 沙 0.111 /
草 0.057，其余 0.0000），圆外无变化，`_ParallaxSteps` 0→24 在圆沿产生 0.082 的偏移。

**伪造交互场时别手搓 `Matrix4x4.LookAt(camPos, center, Vector3.forward)`**：
这个朝向会让 Unity 的 LookAt 把世界 +Z 映射成相机的 −Y（实测行向量 `(0,0,-1,8)`，相机是 `(0,0,1,0)`），
整块交互域上下翻转，世界中心算出 uv.y≈1.17 → `InteractionDomainMask` 判成域外，采样全 0——
看起来像"shader 不响应"。`SyncMatrices` 的无相机分支原本正是这条路径，已改成
`Matrix4x4.TRS(camPos, Quaternion.LookRotation(center - camPos, Vector3.forward), Vector3.one).inverse`，
与 `_orthoCamera.worldToCameraMatrix` 逐位一致（同时删掉了 `GL.GetGPUProjectionMatrix`——
相机分支用的是 `projectionMatrix`，两边必须是同一空间，否则宽高比/翻转对不上）。

## 场景收编：三个互动地形 → 一块 LayeredGround

`InteractionSurfacesDemo` 现在是「一块混地形 + 一层草叶」：

- `SnowPatch` / `SandPatch` 删除（它们的雪/沙材质与 `BuildSnowSandPatch` 保留，别的验证场景还在用）
- `GrassPatch` 保留草叶与 `TrySample` 采样源，`FlatGrassTerrain._renderGround=false`
  （`CreateSurface` 在挂完 MeshFilter 后直接 return，不再建 MeshRenderer）
- `LayeredPatch` 挪到原点（原来在 (0,0,12)），笔迹跟着对象走，229 格原样保留
- 三层绑定：Grass→Grass 0.10/0.5/0.10、Sand→Sand 0.30/1.0/0.12、Snow→Snow 0.22/0.9/0.18

## 编辑器里「场全 0」先排除播放循环没跑

play 模式下 `Time.frameCount` 停在 2、`_lastAreaPos` 不动、`_originRT` 全 0 时，
不是接线坏了而是编辑器循环没推进（`isPaused=False` 也可能这样）。判据：连调两次看 frameCount。
验证可以绕过循环——反射拿 `SyncMatrices` / `RenderOrigin` / `Tick` 手动驱动，
`SyncMatrices → RenderOrigin → Tick(1/30)` 连打 30 步后三张场都有值
（草 0.79 / 雪 0.79 / 沙 −0.79）。

视觉 A/B 也走这条路：同一相机渲两次，中间把 `_Interaction*Valid` 临时置 0 再还原，
两张图逐像素差 —— 6.19% 像素变化、最大 0.85，变化集中在球体的压痕上，说明地面确实吃到了场。

## 相关

- [交互多子集](2026-11-06-interaction-multisubset.md)
- [现场插件架构](2026-11-05-field-plugin-architecture.md)
