# DDOF — 动态景深（Dynamic Depth of Field）

## 概述

全屏后处理景深：以**屏幕中心像素的深度**为对焦参考，像素深度与参考差值超出 `focusRange` 即视为失焦，
按失焦量与距离遮罩缩放高斯模糊半径，最后用遮罩把原图与模糊图插值合成。
效果不依赖物理光圈参数，是「中心对焦 + 越远越糊」的简化模型。

---

## 架构

```
相机色 ─┬─────────────────────────────► 留档 tempRT ─┐
        │                                            ├─ lerp(main, blur, mask) ─► 屏幕
        └─ Pass 0 CoC ─────────────────► maskRT ─┐   │
                                                 │   │
                  Pass 1 BlurH ─► blur1RT ─ Pass 2 BlurV ─► blur2RT ─┘
                        ▲ CoC 驱动半径                     ▲ CoC 驱动半径
```

| 类型 | 职责 |
|------|------|
| `DDOFFeature.Settings` | 面板参数（Artistic / Debug）+ `PropertyToID` 预计算 |
| `DDOFFeature.DDOFPass` | `ScriptableRenderPass`，建 4 张图内 RT 并按 Pass 顺序编排 5 次 Blit |
| `DDOF.shader` | 4 个片元入口：CoC / BlurH / BlurV / Composite，无顶点自定义（用 Blit.hlsl 的 `Vert`） |

---

## 配置参数

### Feature 面板

| 字段 | 分组 | 范围 | 默认 | 说明 |
|---|---|---|---|---|
| ddofShader | Technical | — | — | 指向 `DDOF.shader` |
| focusRange | Artistic | 0 – 50 | 2.0 | 以屏幕中心深度为基准的合焦范围；超出部分开始失焦 |
| blurScale | Artistic | 0 – 5 | 2.5 | 模糊半径倍率 |
| debug | Debug | Off / CoC / Blur | Off | 中间结果直出 |

### Shader 参数

| Shader 名称 | 类型 | 来源 | 说明 |
|---|---|---|---|
| `_FocusRange` | float | Feature | 合焦范围 |
| `_BlurScale` | float | Feature | 模糊半径倍率 |
| `_DDOFTempMainTex` | Texture2D | Feature 全局纹理 | 未模糊的原图留档 |
| `_DDOFCoCTex` | Texture2D | Feature 全局纹理 | CoC 中间结果：R = 失焦量，G = 距离遮罩 |
| `_BlitTexture` | Texture2D | Blit.hlsl | 当前 Pass 的源纹理 |

---

## 渲染管线

| 顺序 | Pass 名称 | 目标 | 说明 |
|:----:|-----------|------|------|
| — | 拷贝 | `_DDOFTempMainRT` | 原图留档，供最终合成 |
| 0 | `DDOF_CoC` | `_DDOFMaskRT` | `R = saturate((abs(pixelDepth - centerDepth) - range) / range)`；`G = (1 - linear01Depth) + 1` |
| 1 | `DDOF_BlurH` | `_DDOFBlur1RT` | 半径 `= CoC.G * blurScale`，调用 `BlurFunction.hlsl` 的固定 5-tap 高斯 |
| 2 | `DDOF_BlurV` | `_DDOFBlur2RT` | 同上，垂直方向 |
| 3 | `DDOF_Main` | 相机色 | `lerp(main, blur, CoC.R)` |

| RT | 规格 | 生命周期 |
|---|---|---|
| `_DDOFMaskRT` / `_DDOFTempMainRT` / `_DDOFBlur1RT` / `_DDOFBlur2RT` | 相机 target 描述符（`depthBufferBits = 0`） | 帧内，RenderGraph 托管 |

- `RenderPassEvent.BeforeRenderingPostProcessing`，`ConfigureInput(Depth)`
- 合成写回 `activeColorTexture`，`AllowPassCulling(false)`

---

## 使用方式

**前置条件**：URP 17+；把 Feature 加到当前 Renderer 的 Feature 列表并指定 `DDOF.shader`。
该 Feature **当前未接入 `Assets/Settings/PC_Renderer.asset`**，需要手动加入才会生效。

**运行时行为**：每帧按上表执行 5 次全屏 Blit；参数在 `RecordRenderGraph` 中逐帧 `SetFloat`。

**Debug 模式**：

| 档 | 行为 |
|---|---|
| `Off` | 正常合成 |
| `CoC` | 在 Pass 0 后直出遮罩并提前结束（省掉两次模糊与合成） |
| `Blur` | 在双向模糊后直出模糊图并提前结束（省掉合成） |

Debug 分支复用本 Pass 已生成的 RT，不额外增加 Pass。

---

## 性能

| 指标 | 值 |
|---|---|
| 每帧全屏 Blit | 5 次（1 拷贝 + 4 Pass） |
| 图内 RT | 4 张全分辨率 |
| 每像素纹理读取 | CoC：3 次深度采样（中心 1 + 本像素 2）；BlurH/V：各 5 次；Composite：3 次（原图 / 遮罩 / 模糊图） |
| 循环边界 | 固定 5 tap，半径只缩放偏移，不进入循环次数 |

---

## 已知限制

- 对焦基准是**屏幕中心**深度，不是相机焦点距离；中心像素落在天空/近物上会整体失焦。
- 失焦量在 CoC Pass 里对每个像素计算，但半径由 CoC 的 G 通道（距离遮罩）驱动，因此「远景糊、近景相对清晰」，不做前后景区分。
- 模糊是单次 5-tap 双向：半径参数为「距离遮罩 × blurScale」（约 1–2 倍 blurScale），tap 偏移再按 ±2 倍该半径铺开；
  大半径下会有明显的五次采样阶梯感（要更平滑需换 `BlurFunction.hlsl` 的分层抖动核）。
- 未接入 Renderer，无双相机 / XR 验证记录。

---

## 扩展点

- 换成 `Blur_GaussianJittered` 三档预算（需要把质量档接进面板）。
- 用真实焦距/光圈参数替换 `focusRange`，或改由 Volume 提供。
- CoC 计算加入近景（前景模糊）分支。

---

## 字段命名速查

- Feature 面板字段：`camelCase`（`focusRange` / `blurScale`）
- `PropertyToID`：`PascalCase + ID`（`FocusRangeID`）
- Pass 索引：`const int XxxPassIndex`
- Shader 内：`_PascalCase` 材质参数；`_DDOFXxx` 中间全局纹理
