# SSGI 框架实施计划

> 2026-09-21：本文档已随 SSGI 家族重组迁入 `Assets/Mine/Shaders/PostProcess/SSGI/`。
> 家族现由统一 `SSGIFeature` 调度 AO、DiffuseGI、SpecularGI，并保留三个独立 Feature 供对照；共享几何层为 `ScreenSpaceTrace.hlsl`。统一运行说明见 [SSGI.md](SSGI.md)。
> 各模块的使用方式、参数与近似边界见各自技术文档：
> [SpecularGI.md](SpecularGI/SpecularGI.md) / [DiffuseGI.md](DiffuseGI/DiffuseGI.md) / [AO.md](AO/AO.md)。

---

## 状态总览

| Phase | 内容 | 状态 |
|---|---|---|
| 1 | SSPR 假平面镜面反射 | **已退役**：控制困难，SpecularGI 不再计算该候选 |
| 2 | Stochastic SSR 粗糙表面反射 | **已并入** SpecularGI 的 GGX 采样级（不再独立成 Feature） |
| 3 | DiffuseGI 屏幕空间间接漫反射 | 已落地 |
| 4 | SSAO / HBAO 环境光遮蔽 | 已落地 |
| 5 | 时域 + 空域滤波系统 | 基础时域已落地：三路共享历史管理、运动重投影和深度拒绝；方差钳制未做 |
| 6 | 统一 Composite | 已落地：同一不透明输入、三路 TextureHandle、单次合成 |

> **Renderer 注册现状（2026-09-28）**：`PC_Renderer.asset` 已启用统一 SSGI Feature，旧 SpecularGI Feature 保留但关闭。AO、DiffuseGI、SpecularGI 均由统一 Feature 在同帧调度，参数和验收见 [SSGI.md](SSGI.md)。
---

## 总体架构

```text
不透明场景色 + 深度 + 法线 + 运动向量
  └─ SSGIFeature（AfterRenderingSkybox）
      ├─ AO → 可见度 ─────────────┐
      ├─ DiffuseGI → 间接漫反射 ───┼→ SSGITemporalFilter
      └─ SpecularGI → SSSR / Cubemap ┘
          ↓
      SSGIComposite.shader → cameraColor（一次）
          ↓
      _CameraOpaqueTexture 拷贝与透明物体
```

`ScreenSpaceTrace.hlsl` 是家族私有共享几何库；`SSGITemporalFilter.cs`、`SSGITemporal.shader` 与 `SSGITemporal.hlsl` 是家族私有共享时域层。三个算法的采样与空域 HLSL 仍各自维护。
旧独立 Feature 保留用于对照，不与统一 Feature 同时启用。

## 路线调整

Phase 1 的 SSPR/SSPM 假平面候选已撤出运行管线。它基于视图方向投影、轴对齐法线阈值和远景淡入，无法稳定控制反射对应关系。现有 SpecularGI 只保留 Phase 2 的镜面/GGX 射线：先尝试 SSR 几何命中，剩余权重直接取显式 Cubemap。DiffuseGI、AO、公共时域与统一合成保持原架构。

---

## Phase 2: Stochastic SSR — 粗糙表面镜面反射

> **已被取代**：不再作为独立 Feature 存在。GGX 重要性采样现在是 SpecularGI 的
> 射线生成方式（`SpecularGI_CreateRayDirection`），粗糙度由 Feature 全局参数给出。
> 原实现按「来源」硬拒绝时域历史、以及对 1 spp 结果做 3×3 颜色硬钳制，都是
> 闪烁的成因，已在 SpecularGI 中移除——详见 [SpecularGI.md](SpecularGI/SpecularGI.md)
> §已知限制，以及闪烁根因复盘
> `.agents/agents/unity-developer/memory/2026-09-21-speculargi-unified-fallback.md`。

### 算法原理

基于 Frostbite (SIGGRAPH 2015) 方法：每像素 1 条 GGX 重要性采样射线 + 空间重用以降噪。

**两阶段管线：**
```
Stage 1 — Ray March (1 SPP)         Stage 2 — Resolve (空间重用)
┌────────────────────────┐          ┌──────────────────────────┐
│ GGX 重要性采样          │          │ 搜索邻域命中结果           │
│ 屏幕空间步进            │    →     │ 几何加权平均              │
│ 写入颜色 + 来源编码      │          │ 5×5 深度/法线权重         │
└────────────────────────┘          └──────────────────────────┘
```

### 实施步骤

1. 添加 `SpecularGI/Settings` 性能档（Trace 分辨率 + 步数）
2. 射线生成 `SpecularGI_CreateRayDirection(uv, normalWS, viewDirWS)`:
   ```hlsl
   // 1. GGX VNDF 重要性采样（Intel 快速方法，无需构建 TBN）
   float3 H = SampleGGX_VNDF(rand, roughness);
   float3 reflectDir = reflect(-viewDir, H);
   // 2. 屏幕空间几何首命中（复用 ScreenSpaceTrace.hlsl）
   // 3. 输出屏幕命中置信度；剩余权重回退到 Cubemap
   ```
3. 空间重建 `SpecularGI_SpatialResolve()`:
   ```hlsl
   // 1. 5×5 邻域，深度/法线权重
   // 2. 不做紧邻域颜色硬钳制（1 spp 下会保留噪声并钳黑历史）
   ```

### GGX VNDF 采样核心

```hlsl
// Intel VNDF: 无需 TBN 构建，~15% 更快
float3 SampleGGX_VNDF(float2 xi, float alpha) {
    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (alpha*alpha - 1.0) * xi.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    // 各向同性：半球均匀方位角
    return float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
}
```

### 依赖

- 几何首命中：`ScreenSpaceTrace.hlsl` 的 `SST_Trace`（家族共享，不再是旧 RayMarchFunction）

---

## Phase 3: Diffuse GI — 屏幕空间间接漫反射

> 2026-09-21：Shader 与 RenderGraph 管线已落地；使用方法、参数、近似边界见 [DiffuseGI.md](DiffuseGI/DiffuseGI.md)。

### 已实现管线

```text
DiffuseGIFeature（AfterRenderingSkybox，早于 URP 的 _CameraOpaqueTexture 拷贝）
    本帧不透明场景色 + 天空盒 + 深度 + 法线
      → DiffuseGI.shader / Trace
      → BlurHorizontal → BlurVertical
      → Resolve（几何引导上采样）
      → Composite（独立目标，更新 cameraColor）
```

- `DiffuseGIFunction.hlsl`：TBN + cosine-weighted 半球采样，静态像素随机旋转。
- `ScreenSpaceTrace.hlsl`：世界空间步进、近平面/clip.w/UV 安全检查、首次深度穿越和二分精炼；几何结果不携带镜面材质权重。
- 命中法线朝向验证、沿接收法线的起点偏移，以及最大距离/厚度约束。
- 半分辨率（Low 档为宽高各 1/4）追踪与深度/法线双边滤波，几何引导上采样到全分辨率。
- 独立 Debug：Trace / Indirect / Confidence，保留 Off 合成模式。

### 估计量约定

```text
p(ω) = cosθ / π
GI.rgb = Σ(Li_hit × edgeFade × artisticFalloff) / N
Final.rgb = Scene.rgb + intensity × receiverAlbedo × GI.rgb
```

`N` 是总射线数，miss 为零贡献，不能只除命中数。`GI.rgb` 是 `E/π` 的近似，不再重复乘 BRDF 的 `1/π` 或余弦。Alpha 仅记录命中置信度，滤波和合成不再次乘 alpha。

输入是相机方向的场景辐亮度近似，并非纯直接漫反射；当前 `receiverAlbedo` 使用统一颜色，尚无逐像素材质反照率/金属遮罩。合成是加法近似，会与场景已有烘焙/探针 GI 重叠。

距离衰减作为美术项仅在 gather 乘一次，默认 0 关闭。统一 Composite 不再向本输出叠乘同一衰减。

### 接口与复用边界

几何层由 `ScreenSpaceTrace.hlsl` 统一提供，DiffuseGI 不携带任何镜面专属权重（旧
`RayMarchFunction.hlsl` 内的 `HitProcess` 含 `_Smoothness`，已随 SSR 删除）。
三个算法仍各自配置参数；统一 Feature 调度它们并共享同一帧的输入纹理。

| 性能档 | 宽高缩放 | 射线/像素 | 步进/射线 |
|---|---|---|---|
| Low | 1/4 | 1 | 24 |
| Medium | 1/2 | 2 | 48 |
| High | 1/2 | 4 | 64 |

内部发布 `_GITraceTexture` / `_GITexture`，仅限本效果执行后的同相机同帧使用。Intensity=0 且 Debug=Off 时跳过整个 Feature，不可继续消费旧全局绑定。

### 尚未实施

- Cache-Aware 半球采样与相关性能基准：未实现，原提纲的“62% 加速”不代表本实现。
- 时域累积与重投影已由 Phase 5 公共层接入；方差钳制、动态光照失效判定与运动画质验收仍待完成。
- 屏幕外/遮挡背面信息：屏幕外与天空已验证不贡献 GI，遮挡背面仍无信息。
- 透明物体：**不在追踪输入内**（事件早于透明绘制）。旧版本用截屏得到的“隐藏 Water/RainDrops 后 GI>0.10 从 39315 px 降到 3952、均值 0.419 → 0.103”是透明几何覆盖调试视图造成的取景伪影；追踪是否命中透明几何需回读 `_GITexture` 全局纹理判定（开放项）。
- 逐像素反照率与金属遮罩：仍使用统一 `receiverAlbedo`。
- HiZ 加速、Compute 迁移以及平台/性能全面验收。

### 验收记录

- C# 已通过本机 Unity 6000.3.14f1 / URP 17.3 程序集的 Roslyn 编译。
- 新增代码经过项目 write_gated 与规范检查。
- 2026-09-21 经离屏 RT 对照测量：Intensity=0 与禁用位精确一致；天空盒不参与 gather（差值 0.00000000）；正交投影可用；三档性能经 uniform 回读确认接线；`Time.timeScale = 0` 时位精确；修复后 intensity 0 → 1 有 28370 px 变亮（平均 +0.03144，最大 +0.57568）。数值表见 [`DiffuseGI.md`](DiffuseGI/DiffuseGI.md) §验收。
- 事件排序（本轮修复）：旧值 `BeforeRenderingTransparents` 使 GI 合成晚于 `m_CopyColorPass`，透明物体（Water/RainDrops）折射到未叠加 GI 的 `_CameraOpaqueTexture`，把水面的 GI 盖回原状；已改为 `AfterRenderingSkybox`（同事件下自定义 Feature 先于 URP 内置 Pass）。旧的“调试 RT 污染 = in-place 读写别名”“gather 命中透明几何”两条结论已撤回，理由见 [`DiffuseGI.md`](DiffuseGI/DiffuseGI.md) §事件排序。残留的水面轻微变暗（197 px / −0.027）已重新定性为开放项。
- 薄墙漏光、深度边缘串色、相机运动稳定性未测；视觉质量需在 Game View 人工确认，静态检查不等于画面验证。

---

## Phase 4: SSAO + HBAO — 环境光遮蔽

> 2026-09-21：Shader 与 RenderGraph 管线已落地；使用方法、参数、近似边界与验收读数见 [AO.md](AO/AO.md)。

### 已实现管线

```text
AOFeature（AfterRenderingSkybox，早于 URP 的 _CameraOpaqueTexture 拷贝）
    本帧不透明场景色 + 天空盒 + 深度 + 法线
      → AO.shader / Trace（SSAO 半球核 或 HBAO 水平线积分）
      → BlurHorizontal → BlurVertical（眼深 + 法线双边）
      → Resolve（全分辨率，深度/法线引导的固定四点升采样）
      → 统一 Composite（与 DiffuseGI、SpecularGI 同帧合成，更新 cameraColor）
```

- `AOFunction.hlsl`：两条遮蔽分支共用「引导读取 → 旋转 → 追踪 → 可见度」骨架，`AO_Trace` 是统一入口。
- `ScreenSpaceTrace.hlsl`：复用 Phase 3 引入的几何层（深度采样、眼深/世界重建、投影），不引入第二套几何库。
- 低分辨率追踪（Medium / High 为宽高各 1/2，Low 为 1/4）+ 5 点双边滤波 + 四点几何引导升采样；`depthSigma` 是滤波的几何深度阈值。
- 独立 Debug：AO / Depth / Normal；Off 为合成模式。
- 档位与 SSAO 样本数 / HBAO 方向数 / 步进预算同源展开（`AOFeature.GetTier`）：

| 性能档 | 宽高除数 | SSAO 样本数 = HBAO 方向数 | 步进/方向 |
|---|---|---|---|
| Low | 4 | 4 | 6 |
| Medium | 2 | 6 | 8 |
| High | 2 | 8 | 12 |

### 估计量约定

```text
V（可见度，1 = 无遮挡，0 = 全遮蔽）由 Trace 输出；天空与无效引导返回 1
occlusionApplied = saturate((1 - V) × Intensity)
Final.rgb        = Scene.rgb × (1 - occlusionApplied)
```

Trace 输出的是**可见度**而不是遮蔽量：滤波与合成全程按可见度处理，`Intensity` 只在合成乘一次。滤波后发布 `_AOTexture`，仅限同相机同帧消费。

### 与既有 SSAO 的关系（方案 A：AO 自乘场景色）

Forward 后处理拿不到逐像素的"环境光 / 间接光"分量，所以合成是**整色相乘**，AO 也会压暗直接光照。这正是 URP 自带 SSAO 的 `AfterOpaque` 路径同样的近似（`ShaderLibrary/SSAO.hlsl` + `SSAO_AfterOpaque` 变体）。"只乘到间接光"需要逐像素材质光照数据或 Deferred，留作后续。

### 与 Bavoil 2008 的差异（有意简化，不得当作等价实现引用）

仰角相对**切线平面**测量，不是原文"视线 → 水平线"的完整切线框架；**不做 16 层 de-interleaved 纹理**（方向与步进在同一像素内完成）；无按方向的地平线权重与 30° 固定偏移；步长按眼深折算屏幕像素并夹在 `[1, 64]`。

### 尚未实施

- 方差钳制、HBAO 的 de-interleaved 分组、HiZ / Compute 迁移；时域累积已由公共层接入。
- Forward 场景色仍无法分离直接光，整色 AO 仅是兼容近似；统一 Feature 的 `SceneAO` 可调节此项。
- 未测：SSAO 与 HBAO 的目视质量对比、大半径下的屏幕边缘行为、运动稳定性、正交投影下的 `radius` 手感、档位的运行期 uniform 回读。

### 验收记录

- C# 通过本机 Unity 6000.3.14f1 / URP 17.3 程序集的 Roslyn 编译；`ShaderUtil.GetShaderMessages("PostProcess/AO")` 0 条。
- 2026-09-21 当时已持久化进 `Assets/Settings/PC_Renderer.asset`（`AOFeature`，`m_Active=1`，事件 `AfterRenderingSkybox`）。**当前工作区由统一 SSGI Feature 调度 AO**，见文首「Renderer 注册现状」。
- 2026-09-21 离屏相机对照采集（642×522、关闭后处理）：可见度 HBAO 均值 0.983 / SSAO 0.984，天空行 100% 可见度 = 1.00，可见度 < 0.9 占 6.5% / 6.6%；`intensity 0.0001 → 1` 只变暗不变亮；逐像素 `lin(composite)/lin(base) == V` 中位误差 0.0000。数值表见 [`AO.md`](AO/AO.md) §验收。
- 两条工具陷阱（跨帧回读 `_AOTexture` 必得全 0；Debug 视图会被后处理色彩分级改写）记在 [`AO.md`](AO/AO.md) §已知陷阱。

---

## Phase 5: 时域 + 空域滤波系统 —— 基础时域已落地

三个模块先完成各自的空域滤波和全分辨率重建，再把结果交给 `SSGITemporalFilter`。统一 Feature 对同一相机只创建一对 RHalf 眼深历史；AO、DiffuseGI、SpecularGI 各有独立的颜色双缓冲和混合权重。AO 历史为 RHalf，两路 GI 为 ARGBHalf。公共 Shader 按运动向量读取前帧颜色，重建当前像素在前帧视图空间的眼深，与前帧深度比较后降低或拒绝历史权重。

相机停帧、分辨率变化、矩阵突变、运动向量缺失以及模块停用后的双缓冲侧不匹配都会失效历史。AO 与 DiffuseGI 在有效时域输入下推进逐帧采样旋转；独立 Feature 可绑定同一 `SSGITemporal.shader` 使用该实现。SpecularGI 原来的时域代码已迁出，保留 Trace/Spatial 私有算法和历史权重 Debug。

### 尚未实施

- 邻域颜色/方差钳制、动态光照变化检测以及基于来源变化的拒绝；当前只有几何深度置信度。
- 统一的跨模块空域滤波器；三路现有空域滤波并不具有相同的语义和工作分辨率。
- XR、动态分辨率、快速镜头运动与遮挡边界的画质/性能验收。

## Phase 6: 统一 Composite —— 已落地

三个模块读取同一份不透明场景色，分别输出 AO 可见度、间接漫反射和镜面反射辐亮度。统一 Feature 直接传递 RenderGraph `TextureHandle`，只由 `SSGIComposite.shader` 写回相机颜色。具体公式、Forward 路径的整色 AO 近似、参数及运行验证见 [SSGI.md](SSGI.md)。

AO 在合成中对原场景色最多作用一次，并对新增 DiffuseGI 作用一次；SpecularGI 沿用 Fresnel `lerp`，不把反射当成直接加法。三路时域历史由同一公共管理器维护。
---

## 文件结构

```
Assets/Mine/Shaders/PostProcess/SSGI/
├── ScreenSpaceTrace.hlsl          ← 家族共享几何层（三个模块的 shader 各自 include）
├── SSGIFeature.cs                 ← 三路 RenderGraph 调度与参数入口
├── SSGIComposite.shader          ← 单次合成
├── SSGITemporalFilter.cs        ← 相机/通道历史双缓冲与 RenderGraph 编排
├── SSGITemporal.shader          ← 共享时域与深度历史 Pass
├── SSGITemporal.hlsl            ← 运动重投影与深度拒绝
├── SSGI.md                        ← 统一运行说明
├── SpecularGI/                    ← 镜面 / GGX 分层回退链
│   ├── SpecularGIFeature.cs
│   ├── SpecularGI.shader
│   ├── SpecularGISampling.hlsl    ← GGX VNDF 采样
│   ├── SpecularGITrace.hlsl       ← 屏幕几何命中
│   ├── SpecularGIFilter.hlsl      ← 空间重建
│   └── SpecularGI.md
├── DiffuseGI/                     ← 间接漫反射
│   ├── DiffuseGIFeature.cs
│   ├── DiffuseGI.shader
│   ├── DiffuseGIFunction.hlsl
│   └── DiffuseGI.md
├── AO/                            ← SSAO / HBAO
│   ├── AOFeature.cs
│   ├── AO.shader
│   ├── AOFunction.hlsl
│   └── AO.md
├── SSGI_Content_Skeleton.md       ← 本文件
└── Screen_Render_Architecture_Skeleton.md  ← 屏幕空间解耦分层骨架
```

已退役（2026-09-21 删除）：`PostProcess/SSR/`（`SSR.shader` / `SSRFeature.cs` /
`RayMarchFunction.hlsl` / `RaySampleFunction.hlsl` 与三份 `.backup_v*`）、
`PostProcess/SSPR/`、`PostProcess/StochasticSSR/`。

---

## 步进策略选择矩阵

| 采样算法 | 推荐步进 | 备选步进 | 选择条件 |
|---------|---------|---------|---------|
| SpecularGI（镜面） | `SST_Trace` 世界空间固定步长 | DDA / HiZ（待接入同一接口） | 当前后端；w 危险由 `SST_Project` 直接拒绝 |
| SpecularGI（粗糙） | 同上，锥角随 roughness 增大 | March3D | roughness < 0.6 |
| Diffuse GI | `SST_Trace`（世界空间首命中） | HiZ（待接入） | 独立几何结果，不携带镜面材质权重 |
| SSAO | 半球核深度比较（`SST_SampleDepth`） | — | 半径小（1-2 世界单位）、视图无关 |
| HBAO | 切线平面水平线积分（4-8 方向独立步进） | — | 方向感知遮蔽 |

---

## 参考来源

- **Stochastic SSR**: [SIGGRAPH 2015 Frostbite](http://advances.realtimerendering.com/s2015/Stochastic%20Screen-Space%20Reflections.pptx) | [Intel VNDF Sampling](https://community.intel.com/t5/Blogs/Tech-Innovation/Artificial-Intelligence-AI/VNDF-importance-sampling-for-an-isotropic-Smith-GGX-distribution/post/1599836)
- **Diffuse GI**: [Cache-Aware Hemisphere Sampling](https://patentimages.storage.googleapis.com/57/9b/d6/f5eb79099d2046/US20180040155A1.pdf)
- **HBAO**: [NVIDIA SIGGRAPH 2008](http://developer.download.nvidia.com/presentations/2008/SIGGRAPH/HBAO_SIG08b.pdf) | [De-interleaved Texturing](https://github.com/study-game-engines/nvidia-ssao-demo)
- **HiZ DDA**: [Morgan McGuire - Efficient GPU Screen-Space Ray Tracing](https://research.nvidia.com/publication/efficient-gpu-screen-space-ray-tracing)
