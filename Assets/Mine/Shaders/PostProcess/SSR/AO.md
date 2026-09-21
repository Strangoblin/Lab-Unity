# AO — 屏幕空间环境光遮蔽（SSAO / HBAO）

基于 [SSGI 骨架 Phase 4](SSGI_ScreenSpace_Outline/SSGI_Content_Skeleton.md) 实现。入口为 `AOFeature.cs`，Shader 为 `PostProcess/AO`。与 SSR / DiffuseGI 各自独立配置，互不依赖；几何工具复用 DiffuseGI 引入的 `ScreenSpaceTrace.hlsl`。

## 使用

1. 在实际相机使用的 Universal Renderer Data 上添加 **AO Feature**。
2. 将本目录 `AO.shader` 拖入 `settings.shader`。使用序列化引用，确保构建包含 Shader；未指定时不执行。
3. 从 Medium 档、`Mode = HBAO`、`Radius = 1`、`Intensity = 1` 开始，在接触面（墙角、地面与道具）观察遮蔽。
4. Debug 依次检查 AO / Depth / Normal；Off 显示遮蔽乘到场景色上的结果。
5. `Intensity = 0` 且 `Debug = Off` 时完全跳过本效果。Debug 可独立查看未乘强度的遮蔽。

Feature 在 `AfterRenderingSkybox` 执行，要求 RenderGraph 开启。输入为深度、法线与当前场景色；使用 `ConfigureInput` 请求并在各 Pass 声明读依赖。普通 Game/SceneView 基础相机参与，Overlay、Preview 和 Reflection 相机跳过。

> **事件选择（与 DiffuseGI 同源）**：遮挡只需要"不透明 + 天空盒"的深度与法线，且合成必须落在 URP 拷贝 `_CameraOpaqueTexture`（`AfterRenderingSkybox` 处的颜色拷贝）**之前**。URP 17 RenderGraph 中同事件下自定义 Feature 先于内置 Pass 执行（`UniversalRendererRenderGraph.cs:1275/1283/1287/1303`），因此该事件同时满足两条约束；晚于颜色拷贝时透明物体折射到的拷贝不含 AO。

## 管线与资源

```text
本帧深度 + 法线
  → Trace（低分辨率，SSAO 半球核 / HBAO 水平线积分，输出可见度 r）
  → BlurHorizontal → BlurVertical（深度/法线双边权重）
  → Composite（全分辨率，双线性上采样后乘到场景色）
```

| 文件 | 职责 |
|---|---|
| `AOFeature.cs` | 档位、资源依赖、四阶段编排、相机筛选 |
| `AO.shader` | uniforms、四个 Pass、全屏入口 |
| `AOFunction.hlsl` | 遮蔽估计、双边滤波与合成 |
| `ScreenSpaceTrace.hlsl` | 复用：深度采样、眼深/世界重建与投影（几何层，无 BRDF 权重） |

AO RT 使用线性 `R8_UNorm`（单通道可见度），非 MSAA，无 mip。低分辨率档位在宽高上各除 1/2 或 1/4。临时纹理由 RenderGraph 管理；合成读原场景色、写独立目标，最后更新 `resources.cameraColor`。滤波后 `_AOTexture` 通过 `SetGlobalTextureAfterPass` 发布，供同一相机同帧的后续 Feature 消费（当前无线程使用者）。材质在 Create/Dispose 中释放，参数保存到 PassData 并在执行阶段绑定。

## 算法

### 可见度约定

Trace 输出**可见度** `V ∈ [0,1]`：1 = 完全无遮挡，0 = 完全遮蔽。天空与无效引导（无法线、法线退化）返回 1。滤波与合成都按可见度处理，合成时才乘 `Intensity`：

```text
occlusionApplied = saturate((1 - V) × Intensity)
Final.rgb        = Scene.rgb × (1 - occlusionApplied)
```

`Intensity = 1` 为全量遮蔽，`> 1` 为夸张（超过 1 后受 saturate 限制），`0` 为关闭。

### SSAO（半球核）

视图无关的世界空间实现，天然支持正交投影：

```text
P = WorldPosition(uv)
for i in N:  S = P + cosineHemisphere(xi_i, N) × radius
             S 投影回屏幕 → 比较 S 的眼深与该像素几何眼深
             命中条件：sampleEyeDepth - occluderEyeDepth > bias
                       且 centerEyeDepth - occluderEyeDepth < radius（距离门限）
V = 1 - 命中数 / N
```

`bias` 防止共面自遮蔽；距离门限丢弃半径外的遮挡物。半球方向用与 DiffuseGI 相同的 cosine 采样与静态像素旋转（无时域历史，不用逐帧抖动）。

### HBAO（水平线积分）

沿旋转后的屏幕方向做切线平面上的仰角追踪：

```text
每个方向 θ：axis = (cosθ, sinθ) 的视空间水平向量
            up   = normalize(N - axis × dot(N, axis))   —— 切线平面在该竖直面内的"上"
            sineBias = sin(angleBias)
每个步进：S 采样深度 → 视空间位置，δ = S - P
          horizon = dot(δ, up) / |δ|                    —— 相对切线平面的仰角正弦
          tangentDist = |δ - up × dot(δ, up)|           —— 切线平面内距离，用于衰减
          occluded = max(occluded, saturate(horizon - sineBias) × saturate(1 - tangentDist / radius))
V = 1 - Σ occluded / 方向数
```

**与 Bavoil 2008 的差异（有意简化，勿当作等价实现）**：

- 仰角相对**切线平面**测量（`up` 取自 `N`），不是原文的"视线向量 → 水平线"完整切线框架；等价效果是平面对相机时遮蔽为 0，掠射面允许累积。
- `angleBias` 以弧度正弦形式进入，补偿低模几何的过度自遮蔽；原文还包含按方向的地平线权重与 30° 固定偏移。
- **不做 16 层 de-interleaved 纹理**：方向与步进都在同一像素内完成，缓存局部性低于原文；`High` 档 8 方向 × 12 步是当前上限。
- 步长按眼深折算屏幕像素（`0.5 × screenHeight × P[1][1] / eyeDepth`），并夹在 `[1, 64]` 像素内；正交投影用 `P[1][1]` 的线性解释。

### 双边模糊与上采样

滤波是 5 点双边核，权重 = 高斯空间权 × 眼深指数权 × 法线幂权。低分辨率结果在合成阶段用**双线性**上采样（不做几何引导的 resolve）。AO 本身是低频信号，边缘误差表现为接触阴影略糊，属已知限制。

## 参数

| 分组 | 参数 | 默认值 | 含义 |
|---|---|---|---|
| Technical | mode | HBAO | SSAO（半球核）或 HBAO（水平线积分） |
| Technical | radius | 1 | 世界空间采样半径，决定遮蔽的作用范围 |
| Technical | bias | 0.02 | SSAO 眼深偏移，抑制共面自遮蔽（HBAO 不使用） |
| Technical | angleBias | 10 | HBAO 切线角偏移（度），抑制低模表面的过度自遮蔽（SSAO 不使用） |
| Technical | falloff | 1 | 距离衰减系数（当前仅作为参数保留，见"已知限制"） |
| Technical | depthSigma | 0.15 | 滤波几何深度阈值，单位同线性视深度 |
| Performance | performance | Medium | 见档位表 |
| Artistic | intensity | 1 | 遮蔽强度；0 且 Debug=Off 时整体跳过 |
| Debug | debug | Off | Off / AO / Depth / Normal |

| 性能档 | 宽高除数 | SSAO 样本数 = HBAO 方向数 | 步进/方向 |
|---|---|---|---|
| Low | 4 | 4 | 6 |
| Medium | 2 | 6 | 8 |
| High | 2 | 8 | 12 |

## 已知限制与开放项

- **合成是场景色整体相乘**：Forward 后处理拿不到逐像素"环境光 / 间接光"分量，因此 AO 也会压暗直接光照。这是 URP 自带 SSAO 的 `AfterOpaque` 路径同样的近似（`ShaderLibrary/SSAO.hlsl` + `SSAO_*_AfterOpaque` 变体）。真正"只乘到间接光"需要逐像素材质光照数据或 Deferred。
- **低分辨率 + 双线性上采样**：接触阴影边缘偏糊；几何引导的 resolve 未实现。
- `falloff` 传入 `_AOParams.z` 但当前 HBAO 分支未消费，属预留参数（避免与 `radius` 语义重叠）；SSAO 分支同样未消费。
- HBAO 的 de-interleaved 分组、按方向权重、30° 固定偏移未实现（见算法节的差异列表）。
- 逐帧抖动与时域累积（骨架 Phase 5）未实现：静止画面有静态噪声，运动时无重投影。
- 屏幕外与遮挡背面无信息：大半径会因深度缓冲缺失而产生边缘变亮/变暗。
- 半透明物体不参与遮挡判定（深度只含不透明 + 天空盒），透明面之后的场景不产生 AO。
- 与 DiffuseGI 的联动（`IndirectDiffuse × AO`）未接线：`_AOTexture` 虽已发布，但该全局纹理只在同相机同帧内有效，且 AO Feature 未启用时槽位无绑定（会被读成黑色），跨 Feature 消费需要额外契约，属后续工作。

## 已知陷阱

- **跨帧回读 `_AOTexture` 一定读到全 0**：`SetGlobalTextureAfterPass` 发布的是 RenderGraph 托管纹理，帧结束即归还池；下一帧 `Shader.GetGlobalTexture("_AOTexture")` 返回的是内置 `UnityBlack`（4×4、`R8G8B8A8_SRGB`），`_GITexture` 同理。2026-09-21 据此一度误判为「AO 输出全 0、分辨率只有 4×4」。要判定效果只能用同帧可读的调试视图（Debug=Depth / Normal）或离屏相机对照采集（见 §验收）。
- **Debug=AO 的灰度会被后处理改写**：它写在同一颜色目标上，随后经过 `Assets/Settings/DefaultVolumeProfile.asset` 的色彩分级（ColorAdjustments / SplitToning / ShadowsMidtonesHighlights / Tonemapping 等）。同一画面在后处理开启时灰度上限只有 209/255，关闭后才出现 255。**取原始可见度必须关掉探针相机的 `renderPostProcessing`**，并在结论里标注后处理状态。
- **`Intensity = 0` 是"整体跳过"的哨兵值**（`AddRenderPasses` 的短路条件），把它抬到 `0.0001` 会顺带改变相机管线路径（`ConfigureInput` 请求法线 → DepthNormals prepass；自写 `activeColorTexture` → 中间色 RT）。做减法对照时必须先确认两侧管线路径一致，否则测到的是路径差异而不是 AO 差异。这条教训来自 DiffuseGI 的一次错误归因，见 [DiffuseGI.md §事件排序](DiffuseGI.md)。AO 的开/关对照因此固定用 `intensity 0.0001`（最小非零）而不是 0。
- Debug=Depth / Normal 直出，不参与合成；它们与 AO 共用同一次 Trace 后的资源，不建第二套管线。
- 天空像素在 AO / Depth 视图下会被显示为白（可见度 1）与远平面深度，属预期。

## 验收

读数来自 **Play Mode 离屏相机对照采集**：把主相机（642×522）复制到一张 `ARGB32` RT、关闭该相机的后处理，逐配置渲染后回读（脚本 `.codex/tmp/ao/verify3.cs`；`Debug=AO` 直出可见度）。

| 项 | 方法 | 结果 |
|---|---|---|
| 编译 | `unityctl asset refresh` + `logs -l error`；`ShaderUtil.GetShaderMessages` | 通过，Shader 0 条消息 |
| 持久化 | 运行时读 `PC_Renderer.asset` 的 Feature 列表与反射读 Pass 事件 | 8 个 Feature，`AOFeature` 已注册、`m_Active=1`、事件 `AfterRenderingSkybox` |
| 可见度范围 | Debug=AO 原始读数（Medium 档） | HBAO 均值 0.983 / 最小 0.69 / 最大 1.00；SSAO 均值 0.984 / 最小 0.61 / 最大 1.00 |
| 天空不被遮蔽 | 图像顶部 5% 行（天空） | 100% 像素可见度 = 1.00（均值 0.9999） |
| 遮蔽分布 | 可见度 < 0.9 的像素占比 | HBAO 6.5% / SSAO 6.6%；无像素低于 0.5（未见"整屏变黑"） |
| 合成方向 | `intensity 0.0001 → 1`（两侧管线路径一致）的逐像素差 | 只变暗不变亮（正差值 0 px）：HBAO 21.6% 像素变暗、平均 −1.77/255；SSAO 5.8%、平均 −5.21/255 |
| 合成公式 | 逐像素校验 `lin(composite) / lin(base) == V` | 中位误差 0.0000（多数像素位精确相等）；残差为 `ARGB32` 回读的 8 bit 量化放大，集中在暗部，非公式偏差 |
| 确定性 | `timeScale = 1` 与 `timeScale = 0` 两次采集 | 六张图读数逐字节一致（无时域项） |
| 档位接线 | 静态核对 `GetTier` → `PassData` → `SetInt`（**未做运行期 uniform 回读**） | 与档位表一致 |

**未测**（本轮按收尾策略留作开放项）：SSAO 与 HBAO 的目视质量对比、大半径下的屏幕边缘行为、运动稳定性、正交投影下的 `radius` 手感、档位的运行期 uniform 回读。逐像素读数与采集脚本见当日 memory（`.agents/agents/unity-developer/memory/2026-09-21-ssgi-phase4-ao.md`）。

## 与骨架的对应

骨架 Phase 4 要求"SSAO 原型 → HBAO 方向切片 → 统一输出 `_AOTexture` → 更新 Composite"。本文对应：SSAO / HBAO 两条分支在 `AOFunction.hlsl` 内完成并统一输出可见度；合成落在本 Feature 内（方案 A：AO 自乘场景色），骨架末尾的统一 Composite 公式（`Direct + IndirectDiffuse×AO + IndirectSpecular×AO + Ambient×(1-AO)`）仍为后续设计，**不能**再把同一 AO 系数乘回本 Feature 已处理过的场景色。
