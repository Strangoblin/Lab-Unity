---
name: surface-lines
description: Analytic surface-attached line material for URP 17.
date: 2026-10-27
---

# SurfaceLines 表面附着程序化线条（2026-10-27）

## 目标与结论

在 `Assets/Mine/Shaders/Render/SurfaceLines/` 新增 `Render/SurfaceLines`，作为
`SurfaceDither` 的纯线条兄弟：跟随表面、无 3D 纹理、无分形层级。线条是否附着表面
取决于坐标空间；分形只解决多尺度自相似，不是“无缝”的前提。

## 正式产物

- `SurfaceLines.shader`：Forward / DepthOnly / DepthNormals / ShadowCaster 四 Pass。
- `SurfaceLinesFunction.hlsl`：2D 主轴投影、旋转线场、`frac` 距离、导数抗锯齿、
  最小屏幕线宽、主线 + 可选交叉线。
- `SurfaceLines.mat`：默认 UV0、45°、`spacing=0.10`、`width=0.18`、
  `minPixelWidth=1`、交叉线关闭、暖白线/近黑底。
- `SurfaceLines.md`：参数、算法、与 SurfaceDither/SSO 的边界、限制。

## 关键契约

- `_CoordinateMode`：`UV0` / `Object Space` / `World Space`；对象/世界模式按法线主轴
  投影，轴切换处有硬接缝。世界模式在物体移动时线条会从表面滑过。
- 线条是解析函数，不读纹理；`_LineSpacing` 在 UV 模式是 UV 单位，在对象/世界模式是
  场景单位。`_LineWidth` / `_LineSoftness` 都是 spacing 的比例。
- 屏幕导数只用于抗锯齿和 `_MinPixelWidth`；**不保证线距在屏幕上恒定**。需要屏幕恒定
  线距时应加层级/频率选择。
- 着色是风格化的：`shade = lerp(_MinShade, 1, saturate(luminance(lighting)))`，
  防止场景强光把深色底冲成亮灰、丢失线条对比；不是完整 PBR 光照。

## 验证

- Unity 6000.3.14f1 / macOS Metal：Shader supported=True；4 个 Pass 全部零错误零消息；
  `_ALPHATEST_ON`、`_RECEIVE_SHADOWS` 变体也零错误。
- Game View 冒烟：临时把 `SurfaceDither_Sphere` 移到 `(16.2,5.3,-2.2)` 并挂
  `SurfaceLines.mat`，球体左侧区域出现线/底两种颜色与明显边缘；随后恢复球体原位置
  `(17.44,1.29,-8.67)` 和 `SurfaceDither.mat`。
- 场景 `RenderScene` 仍为 dirty/未保存；未替用户保存其他并行会话的改动。

## 未验证 / 后续

- UV 接缝、对象/世界轴切换接缝、斜视压缩、屏幕恒定线距、蒙皮角色、附加光、
  Lightmap、透明、XR 与真实相机距离/角度对照。
- 需要真 3D 平面族线场时，可加 `dot(position, planeNormal)` 模式消除投影接缝，
  但线距会随表面斜率变化。
- 需要 SSO 那种全屏三平面阴影线/轮廓时，仍应使用 `PostProcess/SSO`；SurfaceLines
  是逐材质反照率线条，不做深度/法线轮廓或阴影区门控。

## 来源

- 本地兄弟实现：`Assets/Mine/Shaders/Render/SurfaceDither/`（点阵/分形/3D 纹理）。
- 局部参照：`Assets/Mine/Shaders/PostProcess/SSO/`（世界空间三平面条纹 + 屏幕轮廓）。
