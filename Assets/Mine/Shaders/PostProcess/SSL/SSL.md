# SSL — Screen Space Light

> 纯后处理（RendererFeature + settings，无 Volume）。2026-11-15 收敛为单一 HG 体积光模式，
> 移除 RAY3D_Fog / RBR2D 与对应关键字。

---

## 概述

SSL 是全屏屏幕空间体积光：沿视线从相机步进到深度重建点，每一步：

1. 采样主光阴影图，阴影为 0 的步直接跳过；
2. 采样主光彩色 Cookie 得到该步透射色（无 Cookie 时为白色）；
3. 按视线与主光方向的 HG 相位加权，累积 RGB 密度。

最终 `density × _Intensity × mainLight.color` 加法合成到场景。

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

## 参数（Feature Settings → Shader Uniform）

| Settings 字段 | Shader Uniform | 范围 / 取值 | 说明 |
|---|---|---|---|
| quality | 展开为 `_MaxSteps` 与模糊链层数 | Low / Medium / High | 纯成本档位；**不改变亮度** |
| maxDistance | `_MaxDistance` | 0.1-100 | 步进距离上限（感受野） |
| intensity | `_Intensity` | 0-5 | 体积光强度 |
| sslScale | `_SSLScale` | 0-0.999 | HG 相位各向异性 g |
| jitterScale | `_JitterScale` | 0-1 | 起始抖动 |
| blurScale | `_BlurScale` | 0-5 | blur 链半径 |
| passEvent | — | — | 默认 BeforeRenderingPostProcessing |
| debug | — | Off / Light | Debug：Light 时输出中间结果 |

### 质量档位

| 档 | 步进数 | 模糊链 |
|---|---|---|
| Low | 16 | 无 |
| Medium（默认） | 32 | 1 级 × 1 |
| High | 64 | 2 级 × 2 |

## 质量一致性（2026-11-15）

**旧实现亮度随质量变化**：`stepSize = rayLen / maxSteps` 已经包含 1/maxSteps，末尾又执行
`density /= maxSteps`，导致完全照亮射线的密度 ∝ `rayLen / maxSteps`。Low(16) 是 Medium(32) 的
2 倍、High(64) 的一半，所以质量越低越鲜艳。

**修正**：去掉 `density /= maxSteps`，改为固定参考步数 `SSL_REFERENCE_STEPS = 32`：

```hlsl
return density * (phase / SSL_REFERENCE_STEPS);
```

32 与旧 Medium 预算一致，因此 Medium 的画面和面板强度保持；Low / High 现在只改变采样噪声与
模糊链成本，不再改变亮度。相位函数也从每步计算改为每像素计算一次。

## 阴影跳过

循环内先采阴影，`shadow <= 0` 时 `continue`，不采 Cookie、不累积：

```hlsl
float3 samplePos = currentPos;
currentPos += rayDir * stepSize;

float shadow = SampleShadow(samplePos);
if (shadow <= 0.0) continue;

density += stepSize * shadow * SampleLightColor(samplePos);
```

阴影边缘的软阴影值 > 0，仍按实际可见度贡献；只跳过完全无光的步。

## 彩色 Cookie（2026-11-15）

每步颜色来自**主光彩色 Cookie**：

```hlsl
float3 SampleLightColor(float3 positionWS)
{
    // _LIGHT_COOKIES → 读 _MainLightCookieTexture / _MainLightWorldToLight
    // 无 Cookie 返回白色
}
```

- 循环内用 `SAMPLE_TEXTURE2D_LOD(..., 0)`，避免隐式导数在变长循环中的未定义值。
- **Cookie 包围盒外必须回白**：主光 Cookie 使用纹理自身的 Clamp；如果纹理边缘是窗格黑边/铅条，
  UV 越界会重复成大片黑光，看起来像阴影（不是 shadow map）。测试场景把 `lightCookieSize`
  放大 1.25× 留出白色边，并在 `SampleLightColor` 里对 UV 越界直接返回白色。
- Raymarch Pass 使用 `#pragma multi_compile_fragment _ _LIGHT_COOKIES`；关键字由 URP
  `LightCookieManager` 在有主光 Cookie 时全局开启。
- 彩窗写入主光数据的方式：`Light.cookie`（RGB = 透射色，铅条/框为黑）+
  `UniversalAdditionalLightData.lightCookieSize / lightCookieOffset`；URP 每帧发布
  `_MainLightCookieTexture / _MainLightWorldToLight / _MainLightCookieTextureFormat`。
- 地面等表面着色器只有使用 `GetMainLight(shadowCoord, positionWS, shadowMask)` 三参数重载
  （或显式 `SampleMainLightCookie`）才会乘 Cookie；两参数重载不含 Cookie。
- 主光 Cookie 是沿光方向的正交投影，适合平行光 + 平面窗；多窗、独立光源或透视投影需要自定义
  光数据块，不在本 Feature 内。

## 彩窗光平面（2026-11-15）

主光 Cookie 是沿光方向的 2D 投影，默认沿整条光线生效，窗外（向阳面）也会被染色。场景组件
`ColorfulWindowLight` 发布一组全局平面数据：

- `_ColorfulWindowPlane`：`xyz` = 房间侧法线，`w = -dot(normal, point)`
- `_ColorfulWindowMask`：0 关闭 / 1 开启

`SampleLightColor` 在采样 Cookie 前先判平面：平面外侧直接返回白色（普通阳光），内侧才取 Cookie。
无组件时 `mask = 0`，行为与未加平面限制时一致。这是单平面窗近似；多窗、曲面玻璃或透视投影需要
深度感知的彩色阴影图，不能用单一平面表达。

## 与场景深度的关系

SSL 的射线终点来自 `SampleSceneDepth(uv)`，**不做逐点场景深度遮挡**。建筑/地形若要在 SSL 中遮挡
体积，必须位于相机深度纹理：不透明、`ZWrite On`、被 Renderer 的深度输入收集。透明或 `ZWrite Off`
的自定义材质不会遮挡，SSL 会按更远的表面继续步进。主光阴影图只决定光照可见性，不替代相机深度。
测试场景的墙体/地面都写深度，因此室外相机看不到室内的彩色光柱。

## 测试入口

`Assets/Mine/Shaders/Render/ColorfulWindow/ColorfulWindowTest.unity`，独立
`ColorfulWindowTestRenderer`（PC_RPAsset renderer list index 2）挂本 Feature；地面使用同目录
`ColorfulWindowGround.shader` 的三参数 `GetMainLight`。验证图在任务会话目录，说明见
[ColorfulWindow.md](../../Render/ColorfulWindow/ColorfulWindow.md)。

## 历史

- 2026-08-27 起 RBR2D（径向模糊）与 RAY3D_Fog（深度密度）曾作为可选模式存在，2026-11-15 移除；
  旧实现与 Volume 接入说明留在 Git 历史。
