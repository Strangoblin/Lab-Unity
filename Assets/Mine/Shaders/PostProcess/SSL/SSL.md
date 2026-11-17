# SSL — Screen Space Light

> 纯后处理（RendererFeature + settings，无 Volume）。单一 HG 体积光模式。
> 2026-11-15：主光彩色 Cookie + 窗平面方案退役，改为光空间彩色透射图 + 深度比较。

---

## 概述

SSL 是全屏屏幕空间体积光：沿视线从相机步进到深度重建点，每一步：

1. 采样主光 shadow map；阴影为 0 的步直接跳过（墙/窗洞可见性）；
2. 调用 `ColoredShadow_Transmission(positionWS)`：
   - 世界点投到主光光空间，与玻璃深度比较；
   - 玻璃前 / 无玻璃 → 白色；
   - 玻璃后 → 玻璃透射色；
3. 按 HG 相位加权，累积 RGB 密度。

最终 `density × _Intensity × mainLight.color` 加法合成到场景。

彩色透射图由 [ColoredShadowFeature](../../../Special/Renderer/TransparentDepth/ColoredShadow/README.md)
产生，与 opaque shadow map 共存：shadow map 决定“哪里能见光”，透射图决定“光是什么颜色、是否在玻璃后”。

## 管线结构

```
Pass 0  SSL_Raymarch   HG 体积光步进 → blurPing[0]（全分辨率）
Pass 1  SSL_BlurHorizontal   多级 blur 链（BlurFunction.hlsl）
Pass 2  SSL_BlurVertical     多级 blur 链
       （quality 决定 blurLevels；>0 时 Ping/Pong 降采样 + 上采样）
       cmd.SetGlobalTexture("_SSLTex", blurPing[0])   ← 供其他 Shader 复用
Pass 3  SSL_Mix        合成：main + ssl（加法）
Debug   debug=Light 时直接用 _SSLTex 顶掉最终输出（默认 Off）
```

- RenderPass: `BeforeRenderingPostProcessing`，`ConfigureInput(Color | Depth)`
- 临时 RT: ARGBHalf，RenderGraph 自动管理

## 参数

| Settings 字段 | Shader Uniform | 说明 |
|---|---|---|
| quality | `_MaxSteps` + 模糊链 | 纯成本档位；**不改变亮度** |
| maxDistance | `_MaxDistance` | 步进距离上限 |
| intensity | `_Intensity` | 体积光强度 |
| sslScale | `_SSLScale` | HG 相位各向异性 g |
| jitterScale | `_JitterScale` | 起始抖动 |
| blurScale | `_BlurScale` | blur 链半径 |
| passEvent | — | 默认 BeforeRenderingPostProcessing |
| debug | — | Off / Light |

### 质量档位

| 档 | 步进数 | 模糊链 |
|---|---|---|
| Low | 16 | 无 |
| Medium（默认） | 32 | 1 级 × 1 |
| High | 64 | 2 级 × 2 |

## 质量一致性（2026-11-15）

旧实现 `stepSize = rayLen / maxSteps` 后又 `density /= maxSteps`，完全照亮射线密度 ∝ `1/maxSteps`，
Low 是 Medium 的 2 倍。修正为固定参考步数：

```hlsl
static const float SSL_REFERENCE_STEPS = 32.0;
return density * (phase / SSL_REFERENCE_STEPS);
```

三档 SSL Debug 图实测均值 10.14 / 10.63 / 10.63；Low 的约 4.6% 差异来自 16 步采样与无模糊链。

## 阴影跳过

循环内先采阴影，`shadow <= 0` 时 `continue`，不采样透射图、不累积：

```hlsl
float shadow = SampleShadow(samplePos);
if (shadow <= 0.0) continue;
density += stepSize * shadow * ColoredShadow_Transmission(samplePos);
```

相位每像素计算一次，循环外乘到累积密度。

## 彩色透射图（2026-11-15）

`ColoredShadowFunction.hlsl` 的接收逻辑：

- `_ColoredShadowDepth.z = 0` → 返回白色（Feature 未启用/没有透射对象）。
- UV 越界 → 白色。
- 比较点深度与玻璃深度；玻璃后取 `.rgb`，玻璃前回白。
- 循环内用 `SAMPLE_TEXTURE2D_LOD(..., 0)`，避免隐式导数在变长循环中的未定义值。

**不再使用主光 Cookie / `_LIGHT_COOKIES` / `_ColorfulWindowPlane`**。旧实现与文档思路保留在 Git 历史；
Cookie 纹理与 `ColorfulWindowLight.cs` 仍在功能目录中作参考资产，但测试场景和 Shader 不再读取。

## 与场景深度的关系

SSL 的射线终点来自 `SampleSceneDepth(uv)`，**不做逐点场景深度遮挡**。建筑/地形若要在 SSL 中遮挡体积，
必须位于相机深度纹理：不透明、`ZWrite On`、被 Renderer 的深度输入收集。透明或 `ZWrite Off` 的自定义
材质不会遮挡。彩色透射图只负责颜色与玻璃前后，不替代相机深度。

## 测试入口

`Assets/Mine/Shaders/Render/ColorfulWindow/ColorfulWindowTest.unity`：

- `ColoredShadowFeature`（BeforeRenderingOpaques）+ `SSLFeature`（BeforeRenderingPostProcessing）；
- 彩窗 Quad 在 Layer 30，玻璃材质 `Render/ColoredGlass`；
- 主光 `cookie = null`；
- 地面/墙体使用 `ColorfulWindowGround.shader`，直接调用 `ColoredShadow_Transmission`；
- 静态/动态验证见 [ColorfulWindow.md](../../Render/ColorfulWindow/ColorfulWindow.md)。

## 历史

- 2026-08-27 RBR2D 径向模糊与 RAY3D_Fog 深度密度模式移除；SSL 收敛为单一 HG Light。
- 2026-11-15 主光彩色 Cookie + 单窗平面 mask 移除，改为 ColoredShadowFeature 的光空间透射图 + 深度比较。
  旧 Cookie 的 Clamp 黑边会把包围盒外乘成黑光（伪阴影），该问题随方案退役。
