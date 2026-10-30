# HiZ 像素步幅修复，2026-10-30

用户先问 HiZ 射线漏打的原因，随后直接下令「直接改像素步幅」。

## 根因

`RayStep_CreateDDA` 把 DDA 单位定义为 `progressSpan / maxSteps`，即**屏幕上 `L/maxSteps` 像素**（`L` 为裁剪后弦长）。旧 HiZ 用 `stride = 2^mip` 乘这个单位，实际每步前进 `2^mip × L/maxSteps` 像素，却只查询 `2^mip` 像素的粗层单元 —— 覆盖率恒为 `maxSteps/L`，与 mip 无关。`maxSteps=48`、`L≈864` 时探针落在路径的 2%/6%/15%/31%/65%/100%，漏打集中在 20–90% 中段。

## 改法（`ScreenSpaceTraceFunction.hlsl` 的 `ScreenTrace_TraceHiZ`）

- `unitPixels = length(increment.pixel)`；`rayPixels = unitPixels * maxSteps`；`stride = stepPixels / unitPixels`。
- `stepPixels = min(2^mip, limit - cursor, rayPixels - cursor)`，cursor 以**像素**计。
- 粗层命中：`parentEnd = cursor + stepPixels`、回退 state、`mip--`、cursor 不动 —— 逐级细分而不是跳过粗胞元后半段；cursor 到 `parentEnd` 才重新升级。
- 预算 `2 × maxSteps`；耗尽返回 BUDGET。
- `unitPixels`/`rayPixels` 为零或非有限（射线正对深度轴、屏幕投影退化）时**回退 DDA**。零投影射线在旧实现里仍走 mip 路径，是旧的回退负控场景，现已改为断言 HiZ 与 DDA 完全一致。

## 验证

CPU oracle 必须同步改成「弦像素」模型：GPU 的 `state.pixel` 是裁剪端点线性插值，不是精确透视投影；`Sample()` 的精确投影只留给 DDA 分支。参考模型加 `parentEnd` 细分与 `2 × Samples` 预算。

- 公共库：39 组 / 88,826 断言通过；DDA 距离注入 0.5 负控被识别。
- SSGI：92 组 / 1,153,512 断言通过；5 个负控全部被识别。
- 断言上限从 `Samples + 9` 改为 `2*Samples + 9`；`_TestMaxSteps=1` 时 HiZ 步数上界为 2，并断言「BUDGET ⇒ 步数=预算」。

**这组验证只证明 GPU 与新参考模型一致、预算有界，不证明画面漏打减少**。离线简化深度场（临时 Python 模拟，未入库）里 0.2 m 立柱场景 miss 从 17.4% 降到 6.3%、DDA 为 3.9–4.7%；这是模拟数字，不能当验收证据。真实画质仍需在 Game View 人工确认。

## 已知残留

- mip 0 的细层假阳性会让 `ScreenTrace_RefineDDA` 失败并**直接结束该射线**（与 DDA 同语义），因此仍可能漏掉细层之后的几何。
- 仍不是保守空单元证明，薄几何可漏。
- `2 × maxSteps` 是上界不是均值；Low 档（24）逐像素上升可能接近 DDA 的 24 次查询，不保证更快。

相关：[SSGI 统一 Trace](2026-10-30-ssgi-shared-trace.md)、[DiffuseGI HiZ 实验](2026-10-28-diffuse-hiz.md)。
