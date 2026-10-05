# 2026-09-20 — 时域累积抽离 TemporalFunction + PCSS 接入

## 背景

项目后续不开 TAA，时域由各效果自持。把 StochasticSSR 的私有 temporal pass 抽成跨效果共享库
`Assets/Mine/Special/HLSL/TemporalFunction.hlsl`（A 档纯函数，零 CBUFFER、零兄弟 include），
首个接入方选 PCSS——因为平行光下的阴影可见性只依赖世界位置，重投影无偏。

## 结论

- **PCSS 是时域的正确第一落点**：成本天然 O(r²)，32 个 Vogel 点在 100+ px 核上极度稀疏，
  带状走样是结构性的，加采样数按平方增长，时域摊销是唯一便宜解法。
- **独立性条件**：`PCSS_Main` 的采样角必须叠加 `_FrameIndex * 0.618`（黄金比）逐帧旋转。
  不做这一步，时域累积只是反复累加同一份偏差，**不收敛**。
- **alpha 兼作历史有效位**：`_PCSS_SoftShadow` 四通道已满（RGB=阴影值，A=penumbra mask）。
  非 penumbra 像素 alpha=0 即「此处无有效累积」，penumbra 恒为 1，alpha 全程不参与混合——
  这样下行双边模糊的 mask 语义与接入前逐字节一致。
- **SRV/UAV 互斥逼出的设计偏离**：同一 dispatch 内资源不能既是 SRV 又是 UAV，且从
  正在写的 RWTexture2D 读邻域会与邻居线程竞争（时域累积器里的非确定性 = 永不收敛的噪声）。
  故时域输出必须单独一张 RT，不能原位复用 `_PCSS_SoftShadow`。

## 踩坑 1 — `SetComputeVectorParam` 与 uniform 声明宽度必须匹配

**症状**：`Shader error in 'PCSS': invalid subscript 'zw' at kernel PCSS_BlurH ... (on metal)`，
四个 kernel 报同一条错误（= 整个文件编译失败，Unity 对每个 kernel 重复首错）。

**根因**：`PCSSFunction.hlsl` 声明 `float2 _ScreenSize;`，而 C# 侧用
`SetComputeVectorParam(..., new Vector4(w, h, 1f/w, 1f/h))` 绑定，kernel 里读 `_ScreenSize.zw`。
`float2` 没有 `zw`。

**做法**：`SetComputeVectorParam` 的接收方一律声明 `float4`。需要 texel size 就用 `.zw`，
并且把除法写成 `(id + 0.5) / _ScreenSize.xy`——`float2 / float4` 会提升为 float4 再隐式截断，
在 Metal 上产生隐式截断警告。

**同类风险**：`float3 _WorldSpaceCameraPos` / `float3 _LightDirection` 也走 `SetComputeVectorParam`，
同样应审视声明宽度。本次未改（未报错），留作观察项。

## 踩坑 2 — `Kernel at index (N) is invalid` 的真错在 Editor.log

**症状**：运行时报 `PCSS.compute: Kernel at index (0..3) is invalid`（每 kernel 每帧一条），
但 Unity Console 里没有 HLSL 编译错误。

**误判路径**：`ShaderUtil.GetComputeShaderMessages(cs)`（需反射，非 public）返回空 → 误以为 shader 是干净的。
该 API 反映的是**上次 import** 的结果，未触发真实平台编译时返回空，与
「GetShaderMessages 返回上次编译缓存」是同一类陷阱。

**正解**：真错误只在 `~/Library/Logs/Unity/Editor.log` 里：

```bash
grep -n "Shader error in 'PCSS'" ~/Library/Logs/Unity/Editor.log | tail -20
```

`FindKernel("Name")` 返回有效索引**不能**证明 shader 可用——它只证明 kernel 名字在源码里存在，
与平台编译是否成功无关。运行期 `Kernel at index ... is invalid` 才是「编译真的失败了」的证据。

## 顺带修正的存量缺陷

- `PCSSFeature` 从未挂在任何 renderer 上 → PCSS 从未真正编译运行过，上述错误一直潜伏。
- `PCSS.md` 三处失准：文件名写成 `PCSS_Function.hlsl`（实为 `PCSSFunction.hlsl`）、
  列了不存在的 `PCSSDebugPlane.shader`、列了 `Settings` 里没有的 `pcssTemplateShader` 字段。
- `PCSSFeature.cs` 里 `showShadowMap` 是**调试视图**：直接 Blit 阴影图到屏幕，不参与光照合成。
  接入真实阴影需改为合成路径或劫持 `_ScreenSpaceShadowmapTexture`。

## 验证证据

- C# 编译通过；四个 kernel `FindKernel` 均解析（0/1/2/3）。
- Play mode 实跑：`sun=Directional Light`、`SRP=PC_RPAsset`，日志零 error/warning。
- 修复前的同一路径报 `Kernel at index invalid` ×12，**证明 dispatch 确实到达 GPU**；
  修复后静默 = dispatch 正常执行。这是本次「真的跑了」的证据链。

## 遗留

- **参数名已在本文件写成之后变更**：`temporalBlend` → 美术参数 `temporal`；
  `clampMode`/`clampRadius`/`clampSigma`/`temporalDepthScale`/`temporalNormalPower`
  五者已删除并内化为 `PCSSFunction.hlsl` 的 `TEMPORAL_*` 常量；`lightSize` 与
  `softness` 合并为 `softness`。见
  [2026-09-20-parameter-layering.md](2026-09-20-parameter-layering.md)。
- 视觉验证（重投影恒等测试 → 随机化独立性 → 收敛 → 伪影 → 模糊语义回归）由人工在 Game View 判读。
- `PC_Renderer.asset` 中 `PCSSFeature` 已挂载并绑定 `PCSS.compute`，`m_Active: 0`
  （与 SSR/SSPR/StochasticSSR 同惯例）。
- `DebugOutputFeature` 当前 `m_Active: 1, debug: 1`，在 `AfterRenderingPostProcessing`
  驱动 Snowy 的 SnowAtmosphere pass——它晚于 PCSS 的 `AfterRenderingTransparents`，
  观察 PCSS 时需先关掉，否则画面被 Snowy 覆盖。
