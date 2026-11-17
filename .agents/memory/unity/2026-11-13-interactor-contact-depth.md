---
name: interactor-contact-depth
description: 贴地物体写不进交互场——_ContactDepth 虚拟下沉偏置的设计、压在哪个环节、压满值的算法
date: 2026-11-13
---

# 互动物体的虚拟下沉：`_ContactDepth`

## 问题

`InteractorObject.shader` 的捕获 Pass 输出 `max(0, fragEye − sceneEye)`——**必须真的插进参照面**。
真实互动里网格不会互相穿透：球搁在地上、脚踩在地上，背面与地面的深度差恒 ≤ 0，`max` 之后全是 0，
雪/沙/草/水一个字都写不进去。原来 Demo 是靠球心放在 y = 0.30（半径 0.70）硬沉 0.40 m 绕过去的。

## 方案对比（结论：逐像素深度偏置）

| 方案 | 结论 |
|---|---|
| **逐像素深度偏置**（`fragEye + b`） | 采用。等价于「物体按 b 米沉进地面」，取背面时压痕 = 物体在 h=b 处的**真实截面**，软边免费 |
| 法线外扩 | 否。下沉量带 `|cos(法线,视线)|`，平底给满、侧面只变胖；平滑法线是「膨胀」不是「下沉」；硬边顶点被拆开外扩会出裂缝 |
| 在 compute 里加常数 | 否。轮廓外是 0，整片被抬成同一个值 → 平底硬边的圆盘，截面形状和软边全丢 |
| 代理体（proxy mesh） | 逃生通道。形状可控但要做者维护 + 跟骨骼同步，适合薄片/布料 |

偏置加在 `CaptureFrag` 里，不在处理器里；四个子集都只吃 `_InteractionOriginTex` 一个量，改一处全生效。

## 压满值：两级 gain、两级 saturate

```
rt    = saturate(d · _Intensity)              // material，第一次削顶：d = 1/_Intensity
press = saturate(rt · _ContactGain)           // 三个地面子集 kContactGain 恒为 5
⇒ press ≈ saturate(d · _Intensity · 5)
```

**真正决定手感的是乘积**：`_Intensity = 0.4` 时压满深度 = 1 / (0.4 × 5) = **0.5 m**；
材质那一级的 5 m 削顶永远轮不到触发。`_Intensity` 同时是水面强度和地面饱和点，改 A 会坏 B。

所以 `_ContactDepth` 做成**归一化 0..1**（1 = 接触点恰好输出 1.0），而不是米：
压满深度由材质 `_Intensity` + 子集 `kContactGain` 两个住在不同文件里的常量决定，写死成米会跟着漂。

```hlsl
float fullPressMeters = 1.0 / max(_Intensity * CONTACT_GAIN, 1e-4);
float penetration = max(0.0, fragEye + _ContactDepth * fullPressMeters - sceneEye) * _Intensity;
```

`#define CONTACT_GAIN 5.0` 写在 shader 里，与三个 `*InteractionProcessor.cs` 的 `kContactGain` 是**必须手动同步**的耦合。

## 实测（探针半径 0.70，`_Intensity` 0.4 → fullPress 0.5 m）

球心抬到 y = 0.70（刚好贴地、零穿透）：

| `_ContactDepth` | 0 | 0.5 | 1.0 |
|---|---|---|---|
| `_originRT` max | 0.0000 | 0.0988 | 0.1988 |
| `press`（×5） | 0.000 | 0.494 | **0.994（压满）** |

球按老办法沉到 −0.40 m、`_ContactDepth = 0`：RT 0.1588 → press 0.794。
线性可加：每 0.5 的 `_ContactDepth` ≈ 0.25 m ≈ 0.10 RT。压满以上的偏置只会削平形状，建议 ≤ 0.5。

## 相关

- [多层地面](2026-11-13-layered-ground-alpha-layers.md)
- [交互多子集](2026-11-06-interaction-multisubset.md)
