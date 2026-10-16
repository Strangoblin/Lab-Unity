# 通用射线步进与屏幕深度采样追踪

Unity 6 / URP 17。RAY、DDA、HiZ 共用几何结果；不包含 BRDF、颜色、法线偏移、置信度、光照积分或时域策略。

公共库与 SSGI DDA 接线已完成。SSGI 的 Diffuse/Specular 默认 DDA，保留 RAY 对照、原 AO/着色/滤波，并补逐 GI 配置历史失效；接线、初始预算及真实相机验证见 [SSGI](../../../Shaders/PostProcess/SSGI/SSGI.md)。SSSM 真实效果尚未接线；生产 HiZ 及等质量 GPU 性能选择仍待后续。

## 分层与入口

| 文件 | 职责 |
|---|---|
| `../../HLSL/RayStepFunction.hlsl` | 纯几何：单位射线、世界距离取样、裁切、透视正确投影与 DDA 增量状态 |
| `../../HLSL/ScreenSpaceTraceFunction.hlsl` | 深度资源适配与 `ScreenTrace_TraceRay` / `ScreenTrace_TraceDDA` / `ScreenTrace_TraceHiZ` |
| `ScreenSpaceDepthPyramid.cs` | 按需构建、录制周期内缓存的 RenderGraph 服务 |
| `ScreenSpaceDepthPyramid.compute` | 正眼深转换与保守 min mip 归约 |
| `Validation~/` | 独立世界坐标采样预期、GPU 回读、旧 RAY 基线及两种 fragment 消费者 |

体积、云、SDF、视差可以复用基础层；各自积分、距离场或高度场算法仍由效果实现，不能用深度首命中代替。

## HLSL 契约

调用方先声明依赖，再按顺序 include：

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Mine/Special/HLSL/RayStepFunction.hlsl"
#include "Assets/Mine/Special/HLSL/ScreenSpaceTraceFunction.hlsl"
```

使用整数 Texture Load，fragment 建议 `#pragma target 4.5`。公共库无 CBUFFER、无兄弟 include。

默认资源为 `_ScreenTraceDepth` 与 `_ScreenTraceDepthPyramid`（`TEXTURE2D_X`）。调用方也可在 include 前定义 `SCREEN_TRACE_LOAD_DEPTH(pixel)` / `SCREEN_TRACE_LOAD_PYRAMID(pixel,mip)`；这允许 Compute、自定义深度资源与其他管线复用。宏必须返回单个浮点深度，不能用双线性采样。

| 数据 | 含义 |
|---|---|
| `RayStepRay.origin/direction` | 原始世界起点、单位世界方向；`RayStep_CreateRay` 验证并归一化方向 |
| `distanceRange` | 世界距离 `[tMin,tMax]`，`0 <= tMin < tMax`；命中距离永远相对原始起点 |
| `RayStep_UniformDistance` | `sampleIndex=0..sampleCount` 的等世界距离端点取样；调用者决定积分权重与取样策略 |
| `worldToView/viewToClip` | 与所绑定深度对应的刚性世界→视图与视图→裁切变换；视图前方为负 Z |
| `uvScaleBias` | `uv = ndc.xy * scale + bias`；常规 scale 为 `(0.5,±0.5)`、bias 为 `(0.5,0.5)`，Y 符号必须匹配实际 GPU 投影/深度，不要重复翻转 |
| `depthSize` | 实际有效深度像素尺寸，不是低分辨率 GI Trace RT 尺寸；不决定采样间隔 |
| `depthTextureSize` | 源纹理的实际分配尺寸 |
| `depthUvScaleBias` | 有效视口 UV→源纹理 UV 映射；支持裁剪、RTHandle 有效区和 Y 翻转；整数 Load 越界视为天空 |
| `clipPlanes/zBufferParams` | 正的 near/far；raw 透视深度用 `rcp(zBufferParams.z * raw + zBufferParams.w)` |
| `depthIsLinear/orthographic/reversedZ` | 深度编码；linear 输入是正眼深，raw 输入范围为 `[0,1]` |
| `pyramidMipCount` | 已绑定且有效的 mip 数；HiZ 必须匹配构建结果，DDA / RAY 不要求金字塔 |
| `maxSteps/refinementSteps/thickness` | RAY/DDA 样本数或 HiZ 检查上限、三后端二分次数（0..16）、**线性眼深**厚度（非世界射线距离） |

构造 `ScreenTraceContext` / `ScreenTraceConfig` 后：

```hlsl
RayStepRay ray;
ScreenTraceHit hit;
if (RayStep_CreateRay(originWS, directionWS, float2(tMin, tMax), ray))
{
    bool found = ScreenTrace_TraceDDA(ray, context, config, hit);
}
```

返回 `uv`（有效视口空间）、`distance`、`eyeDepth`、`status`、`steps`、`depthQueries`。`eyeDepth` 是命中射线位置的正眼深，不是该像素的场景表面眼深，两者可相差 `[0,thickness]`。命中字段仅在返回 true / HIT 时有效。末端 UV 可恰落屏幕边界；消费者颜色采样自行 clamp。法线偏移、命中朝向、屏幕边缘淡出、遮挡强度等留在消费者。

| 状态 | 数值 | 含义 |
|---|---|---|
| `SCREEN_TRACE_HIT` | 0 | 有采样穿越并通过厚度检查，不保证真实几何首命中 |
| `SCREEN_TRACE_MISS` | 1 | 按本后端规则完成查询而无命中，或起点在已知表面上/后方被拒绝 |
| `SCREEN_TRACE_LEFT_VIEW` | 2 | 有效片段结束于视口/near/far，或无可投影片段 |
| `SCREEN_TRACE_BUDGET` | 3 | HiZ 因回退/细查耗尽预算而未完成片段；RAY/DDA 固定采样不返回此状态 |
| `SCREEN_TRACE_INVALID` | 4 | 输入无效或浮点区间不能继续前进 |
| `SCREEN_TRACE_UNAVAILABLE` | 5 | HiZ 的 mip 配置缺失或不合法；调用方可改用 DDA |

## 后端差异

- **RAY**：沿整个距离范围均匀取 `maxSteps` 次，前→后穿越后二分。`refinementSteps=6` 对应当前 SSGI 基线。耗尽均匀样本返回 MISS，而非 BUDGET；可能跳过薄几何。起点不在有效视口/near 内时直接 LEFT_VIEW，保留旧路径语义。
- **DDA**：先裁 near/far/屏幕，将完整可见投影线段均分为 `maxSteps` 个样本。预计算屏幕位置、`1/w`、`eyeDepth/w` 增量，循环累加；眼深为 `(eyeDepth/w)/(1/w)`。前→后穿越后在投影参数上二分并检查厚度。无需金字塔，不再逐格或解析 slab 求交。
- **HiZ**：从 mip 0 出发，复用 DDA 增量，以 `2^mip` 放大步幅；采样在最近深度之前则升级，有穿透嫌疑则回退本步、降级，mip 0 复用 DDA 精炼。粗层穿透仅是降级提示，不锁定一段必须完整扫查的候选区；细层未穿透后仍可重新升级，因此粗层曾观察到穿透也不保证命中。它是旧 SSR 风格采样跳跃，不是保守空单元证明，可能跳过薄前景。
- DDA / HiZ 拒绝已知表面上/后方起点，比较预留 `1e-6 * max(1,sceneDepth)` 的眼深编码精度，避免 raw 转换自命中；效果仍需法线偏移。允许先裁切视口外或 near 前方起点。零屏幕投影仍沿深度方向取样，不用 RAY 重跑兜底。
- 基础步幅覆盖完整裁切片段，不恢复历史 `_StepSize=0.2` 缩短覆盖；最后一个样本重定位精确端点，避免累加漂移。距离仍相对原始世界起点。
- DDA / HiZ 以 `1e-4` 世界距离容差区分结束于裁切边界的 LEFT_VIEW 与 MISS；边界缩短量小于该值时可归为 MISS。起点眼深余量只用于 DDA / HiZ，RAY 保留原精确比较。
- DDA/HiZ 最多 `maxSteps + refinementSteps + 3` 次深度查询（含起点、二分和候选确认），不恢复旧 `StepCount × BinCount` 嵌套扫描。HiZ 的迭代上限包含回退/细查。
- 同样样本数不表示同样位置或画质：RAY 世界距离等距，DDA 投影参数等距，均可能跨像素或重复采样；二分在深度阶跃处也不保证真实首命中。零厚度配有限精炼可能 miss，不能按解析交点验收。
- 使用静态函数选择或 Pass 局部关键字，不要在每次迭代动态选择后端。**有 out 参数副作用的查询不可写在三目表达式里**；用 if/else，避免 HLSL 同时计算两侧并覆盖结果。

## RenderGraph 金字塔服务

`Mine.ScreenSpaceTrace.ScreenSpaceDepthPyramid.TryGet(graph,frameData,request,out result,out reason)`：

```csharp
var request = new ScreenSpaceDepthPyramid.Request
{
    Source = depthHandle,
    Shader = pyramidCompute,
    SourceRevision = depthStageRevision,
    Width = activeDepthWidth,
    Height = activeDepthHeight,
    SourceUvScaleBias = depthUvScaleBias,
    ZBufferParams = zBufferParams,
    NearPlane = camera.nearClipPlane,
    FarPlane = camera.farClipPlane,
    LinearDepth = false,
    Orthographic = camera.orthographic,
    ReversedZ = SystemInfo.usesReversedZBuffer
};
bool available = ScreenSpaceDepthPyramid.TryGet(graph, frameData, request, out var pyramid, out var reason);
```

调用方在深度可用的阶段请求，仅 HiZ 消费者请求；RAY / DDA 不调用这个服务，也不创建层级。

- 输出单张精确有效尺寸的 `R32_SFloat` mip 纹理；天空为有限 far 哨兵，父级取转换后正眼深的 min。旧 SSR raw reversed-Z max 同样取最近表面，但不能混用编码。非 2 次幂仍用 floor/ceil 覆盖归约；资源保守归约不代表采样追踪也是保守的。
- Copy 一次应用源 UV 映射；之后所有层级位于有效视口空间。深度与金字塔必须来自相同内容阶段、转换和网格。实际原尺寸被映射成每单元一个源像素；若主动降分辨率会漏源深度，不能当无损 HiZ。
- HiZ 的硬前提是金字塔 mip 0 尺寸等于 `context.depthSize`；HLSL 无单独的金字塔尺寸字段，也不查询纹理尺寸，调用方必须核对 `Result.Width/Height/MipCount` 后再构造 context，不能绑定不同基础尺寸的金字塔。
- `Result` 含 Texture / Width / Height / MipCount；消费者在自己的 PassData 中保存句柄，并用 `builder.UseTexture(...,Read)` 建立依赖，显式绑定所需资源。
- 相同 graph/source handle/shader/revision/尺寸/UV/转换参数在同一 frameData 内复用。**SourceRevision 是调用方指定的阶段/内容版本，不会自动检测纹理更新**；源被重新写入后必须换版本，不能复用旧层级。
- `frameData` 必须是管线本次相机录制的 ContextContainer，或调用方每次录制前 Reset/Dispose 的专属容器。缓存只存在于该容器 ContextItem，Reset 清空；**禁止跨帧、跨相机、跨录制保存 Result/TextureHandle**。服务不持有静态纹理缓存，也不替调用方猜测相机生命周期。
- 每级使用独立 Compute Pass，Copy Write、Reduce 整纹理 ReadWrite 串行依赖。当前 API 的整纹理依赖有意保守，不能改成只写而切断读前级依赖。自动 mip 关闭，未消费的构建可由 RenderGraph 剔除。
- 初版服务只接受已 resolve 的单 slice `Tex2D`；**MSAA / XR 数组明确拒绝**。HLSL 默认宏可扩展 stereo 资源，但不能因此声称该金字塔服务已经支持 XR。
- 不支持 Compute 或 R32 load/store 时返回 false / reason；消费者应选择 DDA。不对 HiZ 正常 miss / BUDGET 自动再跑一遍 RAY。
- 已在 macOS Metal 验证同纹理 SRV 全 mip + UAV 指定 mip 的读写及 RenderGraph 输出；其他图形后端需重跑验证，不宣称跨平台已验收。

## 复用来源与验证

RAY 沿用原 SSGI 穿越与 6 次二分；`Validation~/BaselineScreenSpaceTrace.hlsl` 是冻结 RAY 基线，不是论文版源码。DDA / HiZ 主线采用删除前 SSR（`963c0fc^` 的 `RaySampleFunction` / `RayMarchFunction`；`c8f14c6` 同框架）的预计算齐次增量、回退精炼、mip 步幅升级；保留安全裁切、几何/颜色解耦与有界查询，不照搬鱼眼风险或嵌套扫描。原朝向、置信度与影子策略不进公共库。

### 退役方案：仅文档参考

2026-10-12 实现过完整二维深度单元遍历、单元内透视正确 slab 求交，以及按 mip 边界证明空区的 HiZ。它提供更强的薄几何覆盖合同，但全深度网格遍历与较高预算在 SSGI Diffuse 路径上成本明显。2026-10-13 按用户决定退役源码与逐像素几何 oracle，不留并行实现或源备份。后续确有薄几何/保守首命中需求时，再分阶段引入能力。

功能参考：[McGuire/Mara 2014](https://jcgt.org/published/0003/04/04/)、[AMD SSSR](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/stochastic-screen-space-reflections/)。退役实现和当前框架都不是论文/SDK 的逐行移植；文献不是当前合同或性能保证。

在项目根、Unity Editor 与 unityctl bridge 已连接、门禁已授权 `Assets/Mine/Special` 的条件下：

```bash
bash Assets/Mine/Special/Rendering/ScreenSpaceTrace/Validation~/run.sh
```

runner 经写入门禁临时导入两个自己管理的 Compute / Shader fixture，执行后经删除门禁清理源码与 metas，不覆盖已有 RuntimeFixture；可复用源码保存在 Unity 忽略的 `Validation~/`。新回读报告写到 `tmp/sessions/<date>-screen-trace-checks-<id>/output/results.json`，不写进功能目录。

验证覆盖：

- 独立 CPU 在世界坐标中裁整条视锥片段并投影采样，按固定投影样本/二分与 mip 回退对照 DDA / HiZ；不再逐像素求交，不宣称完整几何首命中。
- 6 种尺寸（13×9、64×32、65×37、1×17、17×1、1×1），linear / raw forward-Z / raw reversed-Z / 两种正交 raw；每组固定 107 条射线。
- 深度阶跃、常深度、全天空、NaN/Infinity/负深度、水平/垂直/负方向/朝向相机、零投影、near 裁切、tMin、0 厚度、离屏、无效方向与查询上限；HiZ 回退耗尽预算及缺失金字塔。
- 7×5 有效视口从 13×9 源深度裁剪，以及 Y 翻转；刚性世界/视图变换后命中距离仍相对原始世界起点。
- 冻结的旧 `SST_Trace` 在 raw 深度合成上下文的命中与距离回归；本 runner 只验证旧追踪流程。真实 SSGI 相机接线另由 SSGI `Validation~/run.sh` 验证，stereo/非单位动态有效区及最终画质仍未验收。
- reflection-hit / shadow-occlusion 两个 fragment 消费者的三后端 GPU 输出；并非改动或验收现有 SSGI / SSSM 效果。
- 真实相机回调中的 RenderGraph 构建与 mip 导出，重复录制、同阶段共享、revision / 尺寸失效、资源缺失及 MSAA/XR 拒绝。
- 负控制故意给 GPU DDA 命中距离增加 0.5，确认独立距离断言确实报错，避免静默空转。

此验证证明库的几何/资源合同，不证明 SSGI 画质改善或 GPU 提速。后续 SSGI 相机接线、透视/正交及 Y 方向、历史失效与空间信号对照已在其独立 runner 完成；总 GPU 成本、连续运动稳定性、XR 与非单位动态有效区仍需验证。
