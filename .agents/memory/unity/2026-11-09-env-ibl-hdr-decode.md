# BRDF_Env 环境 IBL：补 HDR 解码与正确 mip 映射（2026-11-09）

## 结论

`Assets/Mine/Special/HLSL/ENVFunction.hlsl` 的 `BRDF_Env` 有两个与 URP 标准实现不一致的地方，已修：

1. **缺 HDR 解码**。`unity_SpecCube0` 存的是编码值（RGBM / RGB9E5 / 平台相关），解码参数在
   `unity_SpecCube0_HDR`。原来直接 `SampleLevel(...).rgb`，高光段偏暗偏灰。
2. **mip 映射错**。原 `mip = roughness * 5.0`，标准是
   `PerceptualRoughnessToMipmapLevel(roughness)`：先做 `r * (1.7 - 0.7r)` 感知重映射，
   再乘 `UNITY_SPECCUBE_LOD_STEPS`（= 6）。`r = 0.5` 时 2.5 vs 4.05，中间粗糙度反射明显偏锐。

修改：`BRDF_Env` 增加 `real4 specHDR` 参数；四个调用点（`PBRToon` / `WaterFunction` /
`RainEffect` / `SnowEffect`）传 `unity_SpecCube0_HDR`。`RainEffect` 里水珠的
`beadReflection` 也是同一张贴图的裸采样，一并包了 `DecodeHDREnvironment`。

## Unity 环境链路（本次核实）

环境贴图**不是每帧重渲染**，只在环境更新事件点做一次：

- 场景加载 / 进入 Play、烘焙光照、`DynamicGI.UpdateEnvironment()`、替换 `RenderSettings.skybox` 引用。
- 程序化天空与静态天空盒走同一条路：引擎只负责把 skybox 材质画进 cubemap，不判断内容是否随时间变化。
- 两个产物刷新时机不同：SH 每帧从 `RenderSettings.ambientProbe` 读取（URP
  `SetupPerCameraShaderConstants`），改 probe 下一帧即生效；默认反射 cubemap
  （`defaultReflectionResolution`，默认 128，经 `ReflectionProbe.defaultTexture` 兜底绑定）
  必须重新生成。
- 动态天空（昼夜 / 天气变色）要么节流调 `DynamicGI.UpdateEnvironment()`（6 面 + mip + SH 卷积），
  要么实时反射探针管高光 + 自算 SH 管漫反射。

## 复用判据

- 任何直接采样 `unity_SpecCube0` / `unity_SpecCube1` 的地方都必须配
  `DecodeHDREnvironment(sample, unity_SpecCube0_HDR)`；自定义 cubemap 要用它自己的解码参数。
- 粗糙度到 mip 用 `PerceptualRoughnessToMipmapLevel`，不要写死倍数。
- 共享库函数若接收外部 cubemap，HDR 参数必须一起传——不能假设调用方传的是默认反射。

## 验证

Unity 6000.3.14f1 / URP：`ImportAsset(ForceUpdate)` 后 `ShaderUtil.GetShaderMessages`，
`PBRToon` / `Water` / `RainEffect` / `SnowEffect` 均 `supported=True`、`messages=0`。
报告：`tmp/sessions/2026-11-09-env-ibl-fix/output/shader-check.txt`。
未做画面 A/B；HDR 解码后的亮度变化需在真实场景目视确认。
