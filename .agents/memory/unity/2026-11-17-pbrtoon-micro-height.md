# PBRToon 单通道高度微法线（2026-11-17）

## 判定

`_MicroTex` 只接单通道高度（R），在切空间与 `_NormalTex` 做 whiteout 叠加；不设强度 / 旋转参数。
假设：皮肤这类方向性弱的表面，用低分辨率高度图即可替代独立的大分辨率细节法线。

## 实现

- `PBRToon.shader`：`_MicroTex` + `[Toggle(ENABLE_MICRO)]_EnableMicro`；`_MicroTex_ST` / `_MicroTex_TexelSize` 进 `UnityPerMaterial`。
- `ComputeMicroNormalTS`：`SampleGrad` 3 tap（中心 + U + V 前向差分），`slope = (hR - h0, hU - h0)`，`normalize(float3(-slope, 1))`。
- `BlendNormal_Whiteout`：`normalize(float3(base.xy + detail.xy, base.z * detail.z))`。
- `#pragma shader_feature_local ENABLE_MICRO` 只在 Forward；DepthNormals 不声明，后处理仍拿基础法线。

## 已验证

- `ShaderUtil.CompilePass` Forward（pass 0）与 DepthNormals（pass 5），ENABLE_MICRO + ENABLE_CELTOON，`GetShaderMessages` 0。
- 隔离球体（程序化 256² 可平铺高度，Tiling 4×，512² 输出）：
  - 微法线开/关最大通道差：close 0.2575、mid 0.1948、far 0.1294；`SampleGrad` 的 mip 淡出成立。
  - 平均差 close 0.00226、mid 0.00256、far 0.00188；球体屏幕占比随距离变化，均值不能单独当 LOD 证据。
  - cel 关：微细节铺满受光面；cel 开：主要落在明暗交界，受光面被阶梯吃掉。
- 产物：`tmp/sessions/2026-11-17-pbrtoon-micro/output/`（7 张 PNG）；脚本在 `staging/`。

## 未验证 / 推断

- 未在真实角色皮肤、蒙皮形变、动画和移动端上验证；SRP Batcher / GPU Instancing 未单独复测。
- 前向差分有半纹素偏置；若出现方向性偏移，改中心差分（4 tap）。
- 高度强弱由贴图对比度与分辨率决定；换分辨率会改变观感，这是「无强度参数」的代价。
- cel 开启时是否把微法线只接高光 / 粗糙度（漫反射用低通法线）待用户决定。
