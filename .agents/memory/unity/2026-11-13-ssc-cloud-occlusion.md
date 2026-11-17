---
name: ssc-cloud-occlusion
description: 云遮光（SSC 覆盖率扣减 MaskRBR 遮罩）的跨 Feature 时序契约与可用性握手
date: 2026-11-13
---

# SSC 云覆盖率遮挡 MaskRBR

## 目标

云体要遮住阳光径向光束（"云遮光"），而不是各画各的。否决的两条路：

- **让 SSC 吃 RBR 的结果**：SSC 不再自算 HG、直接复用 RBR 的射线条，冲突确实消失，但 RBR 的结果已经被合成进场景色，云再也压不住它——做不到「云遮光」。
- **只改 passEvent**：SSC 整体挪到 RBR 之前（300/350）→ 300 时 skybox 还没画，SSC 合成的云会被 400 的 skybox 整片覆盖；挪到 skybox 之后又必然晚于 RBR，光束直接叠在云上（云会发光）。**两段对时序的要求相反，同一 Pass 满足不了。**

## 方案：SSC 拆两段，覆盖率先行导出

| 段 | 事件 | 字段 | 职责 |
|---|---|---|---|
| Ray March | `AfterRenderingOpaques`(300) | `cloudBufferEvent` | 只写 `_SSCCloudRT`，`SetGlobalTextureAfterPass` 导出 `_SSCCloudTexture`（A = 覆盖率，RGB = 预乘辐亮度） |
| MaskRBR | `AfterRenderingSkybox`(400) | `passEvent` | 遮罩 Pass 乘 `1 - coverage`，再走径向核 |
| Composite | `AfterRenderingTransparents`(500) | `compositeEvent` | `scene × (1 - alpha) + cloud` |

**一个 Feature 两段时间，`passEvent` 这种单值字段表达不了**（URP 的 `renderPassEvent` 本来就是逐 Pass 的排序键）。所以 SSC 的字段按语义拆成「产出时刻 / 写入时刻」，契约写成偏序 `cloudBufferEvent < 消费者 passEvent < compositeEvent`，而不是靠一个字段的含义自觉。两个方向被破坏的后果不同：产出晚于消费者 → 遮罩拿不到覆盖率、静默无遮光；合成早于消费者 → 云先画、射线后加，光束叠在云上。`MaskRBRPass.ValidateCloudOrder()` 在录制时校验这两条，每种配置只警告一次。

关键事实：ray march 的 `Frag_SSC` **不采样场景颜色**，只有 `Frag_Composite` 用 `_BlitTexture`；所以 ray march 前移到 300 不会改变快速路径（`backgroundRadiance <= 1.0` 的 sky 判断仍在 500 的 Composite 里）。两段共享同一个 `Material`（feature 级持有）与一个 `FrameState`（TextureHandle + textureSize + downsample），`AddRenderPasses` 每帧重设两段的 `renderPassEvent`。

## 跨 Feature 握手

`builder.UseGlobalTexture(id)` 在全局纹理没有生产者的图里**直接抛异常**，所以不能无条件声明。握手靠一个 CPU 标志：

```csharp
public static bool CloudOcclusionReady { get; private set; }
static SSCFeature() { RenderPipelineManager.beginCameraRendering += (_, _) => CloudOcclusionReady = false; }
```

- SSC 的 RayMarchPass 在 `RecordRenderGraph` 末尾置 `true`（RenderGraph 按 `renderPassEvent` 排序录制，300 < 400 天然成立）；
- MaskRBR 在遮罩 Pass 录制时读它，为 `true` 才 `UseGlobalTexture(SSCFeature.CloudTextureID, Read)`，并把 `_MaskRBROccludeClouds` 写 1，否则写 0（Shader 里分支跳过采样）。
- 结果：SSC 关闭 / Shader 缺失 / 事件被调到 RBR 之后，自动退化成无遮光的旧行为，不报错。

## 为什么必须在遮罩阶段扣，而不只靠 Composite

现状（SSC=500）本来就有 `(1-a)` 的像素级遮挡，但那是在射线已经加完之后硬切：RBR 的源遮罩仍把云后天空当光源，径向模糊会把云后的亮部糊到云边做成泄漏光环，云薄时也不会减弱光束。在**遮罩阶段**扣，衰减会跟着径向核一起传播，云边缘自然渐变。云区因此被两段各乘一次 `(1-coverage)`，**双重扣减是刻意的**（更强的云遮光），不是 bug。

## 验证

- `Assets/Mine/Shaders/PostProcess/SSC/Validation~/TierCheck.cs`：拆分后重跑，6239008 项断言通过、状态还原、三档云图正常；只改了一处反射字段（`sscPass._material` → feature 级 `_material`）。
- 新增 `Assets/Mine/Shaders/PostProcess/MaskRBR/Validation~/CloudOcclusionCheck.cs`：把 RBR 设成 Debug Mask（忽略场景 RGB），SSC 云色调成黑色，于是三张图只差两层乘子：A=SSC 关、B=SSC 开+遮罩扣、C=SSC 开+不扣。实测 519901 像素：A 全 1、C 有 237247 个被 Composite 压暗、B 再多 162492 个被遮罩压暗，`invertedPixels=0`，max 降幅 1.0 / 0.397。标志位在那三帧里也按预期 true/false 翻转。同一脚本还用 `Application.logMessageReceived` 抓警告做顺序守卫的负控：把 `cloudBufferEvent` 改成 400（= 消费者事件）触发「Cloud occlusion is off」，把 `compositeEvent` 改成 300 触发「composites at」，两条都按预期出现（10 项断言全过）。
- 输出在 `tmp/sessions/2026-11-13-ssc-cloud-occlusion/output/`。

## 陷阱

- **发布时刻与消费时刻要分开放**：SSC 在 `AddRenderPasses` 里发布 `CloudBufferEvent` / `CloudCompositeEvent`，消费者在 `RecordRenderGraph` 里读。因为所有 Feature 的 `AddRenderPasses` 都在任何 Pass 录制之前跑完，而在 `PC_Renderer.asset` 里 MaskRBR 排在 SSC 前面——消费者若在自己的 `AddRenderPasses` 里读，拿到的是上一相机/上一帧的 null。
- `SSC.shader` 的名字是 `PostProcess/SSC`，`Shader.Find("SSC/Cloud")` 返回 null，`Create()` 会静默造不出材质、两个 Pass 都是 null。
- 该 Shader 有一条既有 Metal 警告（`pow(f, e) will not work for negative f`，line 188），与本次改动无关。
