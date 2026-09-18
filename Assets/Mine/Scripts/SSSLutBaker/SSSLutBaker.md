# SSS LUT 烘焙与测试

编辑器菜单：`Tools/SSS Lut Baker...`；材质 Shader：`Custom/SSSLutTest`。

## 使用

1. 设置分辨率、采样数和 `Scatter Radius RGB`，点击 Bake 预览，Save 保存 `.asset`。
2. 将 LUT 赋给测试材质的 `SSS LUT`。默认输出为本目录 `SSS_LUT.asset`。
3. `SSS Strength`（0–1）控制叠加贡献；0 为 Half-Lambert 基础响应。
4. `Scattering Distance`（0–1，世界单位）控制散射距离；内部自动估计几何曲率，以 `spread = distance * curvature` 查询 LUT，0 关闭散射贡献。

测试材质仅暴露强度和散射距离两个调节参数，保留 LUT 资源槽。曲率由归一化几何法线与世界位置的屏幕导数自动估计，不再暴露手动曲率或开关。
共享库 `Assets/Mine/Special/HLSL/SSSFunction.hlsl` 内声明 `_SSSLut` 与采样器；
`_SSSLut_TexelSize` 留在调用 Shader 的 `UnityPerMaterial` 中，传给 `SSS_SampleLut(NdotL, positionWS, normalWS, scatteringDistance, texelSize)`，避免材质常量移出 CBUFFER。
函数内部计算曲率和散射范围；输入归一化几何法线，在片元阶段调用。函数返回原始查表结果（未赋 LUT 或范围为零返回零贡献），强度与 Half-Lambert 组合由材质处理；`SSS_Response` 已移除。

## LUT 契约

| 项目 | 定义 |
|---|---|
| U | `NdotL * 0.5 + 0.5`，保留负值半球 |
| V | `scatteringDistance * curvature / 2`，范围截断到 0..1 |
| RGB | 逐通道归一化的扩散后 Lambert 余弦响应，范围 0..1 |
| Alpha | 固定 1 |
| 存储 | RGBAHalf、Linear、Clamp、Bilinear、无 mipmap、无压缩 |
| 采样 | 烘焙两端网格；运行时 `uv = uv*(1-texelSize)+0.5*texelSize` |

响应已包含余弦；直接光漫射为 `albedo/PI * response * (1-F) * light`，不能再乘 NdotL。
测试材质沿用艺术叠加：`response = halfLambert + saturate(lut * strength)`；零范围、未赋 LUT 或零强度返回 Half-Lambert。

## 烘焙模型

参考预积分皮肤着色的环形近似，对曲面上的 Lambert 光照做归一化扩散。
令 `q=scatteringDistance*curvature`，环上偏移角为 a，归一化弦长为 `d=2*sin(a/2)/q`。
每个通道采用可调高斯 `w=exp(-0.5*(d/profileRGB)^2)`，积分 `max(cos(theta+a),0)` 并除以权重积分。
默认相对半径 `(1, 0.35, 0.2)` 使红色扩散更宽；这是可调艺术轮廓，不是测量所得的多层皮肤模型。
积分范围随 q 调整，避免低曲率时窄高斯被固定角度采样漏掉；q=0 直接返回 Lambert。

## 边界

- 测试 Shader 仅处理主光源与简单球谐环境光，不实现附加灯、屏幕空间扩散或背面厚度透射。
- 当前测试材质未将读取的阴影衰减乘入直接光，也不模拟光跨阴影边界传播。
- 自动曲率依赖网格法线和平滑程度；使用几何法线，不使用法线贴图。
- 散射距离使用世界单位；相同距离下，高曲率区域使用更宽的 LUT 响应，spread 超过 2 时截断。
- 窗口 Load 只恢复纹理，不恢复烘焙参数；相同路径保存保留 GUID 和材质引用。
- GPU baker 使用 float 积分避免半精度累计误差，材质响应使用 real。

## 来源

- [Penner 与 Borshukov：Pre-Integrated Skin Shading，GPU Pro 2](https://www.oreilly.com/library/view/gpu-pro-2/9781439865606/chapter-17.html)
- 工程结构参考相邻目录 `../FGDLutBaker/` 的烘焙服务与窗口。
