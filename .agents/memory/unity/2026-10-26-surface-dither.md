---
name: surface-dither
description: Surface-stable fractal Bayer dithering material for URP 17.
date: 2026-10-26
---

# SurfaceDither 表面稳定分形抖动（2026-10-26）

## 目标与结论

在 `Assets/Mine/Shaders/Render/SurfaceDither/` 落地一个 URP 17 材质：点固定在物体表面，
同时按屏幕导数动态选择 3D Bayer 图案的分形层级，使点在屏幕上的大小/间距近似恒定。
实现参考 Rune Skovbo Johansen 的 Surface-Stable Fractal Dithering，按项目 URP 规范重写，
未直接搬运原 Unity Built-in 代码。

## 正式产物

- `SurfaceDither.shader`：`Render/SurfaceDither`，Forward / DepthOnly / DepthNormals / ShadowCaster 四 Pass。
- `SurfaceDitherFunction.hlsl`：Jacobian 奇异值、log2 层级、3D 图案采样、亮度 ramp 与对比度。
- `Editor/SurfaceDitherTextureGenerator.cs`：8×8 Bayer、128×128×64 R8 Texture3D、128×1 R8 ramp、1×1 白贴图与示例材质。
- `Editor/SurfaceDitherValidator.cs`：Shader 消息、四 Pass + 五变体编译、隔离平板近/远渲染统计。
- `SurfaceDither.md`：参数、算法、限制、验证与扩展点。
- `Validation~/`：`results.txt` 与最终一轮 near/far/brightness/object-space PNG。

## 关键契约

- 3D 纹理横向分辨率固定为 `16 × dotsPerSide`，Z 层数为 `dotsPerSide²`；着色器由 `_DitherTex_TexelSize.z` 反推，生成器改动必须同步。
- 表面坐标支持 `UV0` 与 `Object Space`（按对象空间法线主轴投影）；后者适合硬表面，曲面轴切换处有接缝。
- 空 `_MainTex` 由 `_MainTex_TexelSize` 判空回退为白色；material 默认绑定 1×1 白贴图。
- 纹理资产必须线性（`m_ColorSpace=0`）；生成器用 `SerializedObject` 修正已存在资产的色彩空间。
- 亮度 ramp 用 `brightnessCurve` 反查阈值；`_DotSizeVariability=0` 为 Bayer 点数风格，1 为半调点大小风格。

## 验证

- Unity 6000.3.14f1 / macOS Metal 批处理导入与逐 Pass 编译通过，`ShaderUtil` 消息 0。
- 四 Pass + `_INVERSE_DOTS`、`_QUANTIZE_LAYERS`、`_RADIAL_COMPENSATION`、`_DEBUG_FRACTAL`、`_ALPHATEST_ON` 全部编译通过。
- 512×512 隔离平板：UV0 近/远边缘密度 0.3497 / 0.3483，比值 0.996；Object Space 0.3498。
- 诊断输入亮度 0.5482，与测试环境光 0.55 对齐。
- 预览场景主光未被 URP 选中；主光/阴影仅验证编译，真实场景画面未独立验证。

## 未验证 / 后续

- 真实场景主光/阴影、UV 接缝、强各向异性、蒙皮、附加光、Lightmap、透明与 XR。
- PNG 是显示证据，不是线性 HDR 数值证据。
- 可扩展 RGB 三通道、CMYK 半调旋转、第二套 UV 自适应与 4×4/16×16 图案预设。

## 来源

- Rune Skovbo Johansen, *Surface-Stable Fractal Dithering* 视频与开源实现：
  <https://www.youtube.com/watch?v=HPqGaIMVuLs>、<https://github.com/runevision/Dither3D>（MPL-2.0）。
- 本地记录：`Assets/Mine/Shaders/Render/SurfaceDither/SurfaceDither.md`、`Validation~/results.txt`。
