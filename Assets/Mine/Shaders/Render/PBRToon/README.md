# PBRToon — Unity 6 / URP 17 材质结构

Shader 名称：`Render/PBRToon`。保留现有 Toon、SSS LUT、补光、各向异性、环境反射、边缘光和描边计算。

## 管线能力

- 六个 Pass：Forward（UniversalForwardOnly）、Outline、MaskBlur、ShadowCaster、DepthOnly、DepthNormals（DepthNormalsOnly）。
- 全部 Pass 启用经典 GPU Instancing 变体，顶点变换前初始化实例 ID；片元恢复实例/眼睛索引。
- Forward 的 renderinglayer 实例化选项与已有 Light Layers 光照逻辑配套。
- 统一 UnityPerMaterial；Unity SRP Batcher 兼容检查返回 0。
- 普通贴图使用 TEXTURE2D；颜色、法线、AO、Emission 各自支持 Inspector Tiling/Offset。
- Forward / Outline 支持场景雾；主光屏幕阴影、级联阴影、软阴影变体按 URP 17 接口处理。
- 基础几何共用 SurfacePositionWS；描边继续额外外扩，阴影/深度不包含这层艺术描边。
- DepthNormals 按 URP 输出世界法线或八面体编码，alpha 不再写入自定义线性深度。
- 当前材质保持 Opaque，没有新增 AlphaClip 或透明混合功能。

## 使用与边界

材质参数名和资源 GUID 保留。ST 默认 (1,1,0,0) 时原采样不变；已有非默认 ST 现在会真正生效。
材质的 Enable GPU Instancing 可按提交方式启用；普通 MeshRenderer 可能选择 SRP Batcher，不能仅凭复选框断言实例化。
显式实例化绘制需提供需要的光照探针等实例数据，不能把缺少数据导致的明暗变化归因于 Shader。
法线输出供标准 URP 消费；需要线性深度时应读取相机深度并按其约定重建。
XR 宏已接入，但未做 XR 设备验证；DOTS/BRG、Lightmap/Meta、MotionVectors、Rendering Layers MRT 输出不在本轮范围。
保留现有共享 HLSL 算法，未宣称这些依赖已经完成全部 XR/平台适配。

## 遮罩柔光（2026-10-08）

`MaskBlur` Pass 使用 `LightMode="MaskBlur"`，由独立的
`Assets/Mine/Shaders/PostProcess/MaskBlur/MaskBlurFeature.cs` 收集。主体顶点变换与 Forward 共用，
输出 `_MaskBlurWeight`（默认 1，0 为不参与柔光），不包含 Outline 外扩。
片元使用同帧相机深度剔除被遮挡表面，不依赖共享 Stencil 编号；
Max 混合保证被遮挡表面的零值不会覆盖可见表面的权重。

在 Renderer Data 上添加 MaskBlurFeature 并指定 `PostProcess/MaskBlur` Shader。
该功能在不透明物体和天空绘制后提取遮罩内场景颜色，执行 Gaussian 模糊并叠加柔光；
参数与全局纹理接口见 `Assets/Mine/Shaders/PostProcess/MaskBlur/README.md`。
PC_Renderer 已接入该 Feature。原来的 PBRToon 光照和描边算法保留。

## 验证（2026-09-17）

- 模板先更新并通过 GPU 检查，再迁移本 Shader。
- 五 Pass 及代表性关键字变体编译无错误；非均匀缩放/旋转的双实例与独立绘制对照通过。
- Forward / Outline 在受控无雾对照中与迁移前输出一致；实例化 Forward 平均 RGB 差约 0.0024（0–255）。
- ShadowCaster / DepthOnly / DepthNormals 的实例化深度对照差为 0。
- 当前 Main Camera 渲染通过，场景中两个使用本 Shader 的对象无新增 Shader 错误。
- 未完成所有灯光、XR、构建平台组合的视觉验收；阴影轮廓的自动检查使用零偏置的受控条件。

## 织物层（2026-10-30）

`ENABLE_FABRIC` 在 Forward 片元里追加一层 KM 双流薄层，替代原先独立的 `Render/Fabric` 材质：
单物体、单材质、不透明队列即可，不需要第二个物体或透明混合。

Frag 按语义分三段，织物层夹在中间，只看得到「反射率」和「辐亮度」：

| 段 | 内容 | 交给织物层的部分 |
|---|---|---|
| ① 本体层 | `Diffuse + Specular + Ambient + rim`（已含 cel 阶梯 / SSS / AO / 补光） | `bodyRadiance`；另给 `bodyReflectance = baseColor·(1−metallic)` |
| ② 织物层 | `Fabric_ShadeLayer(...)`，实现在 `FabricFunction.hlsl` | 一次调用返回薄层之上的总辐射 |
| ③ 表面层 | `fresnel · _Brightness` 全局调色 | 合成之后统一乘 |

契约（重要）：**双流公式里的 Rg 只能是反射率**。风格化后的最终色含 rim、emission、`_Brightness`
和 cel 阶梯，既不是反射率，也会让亮部的 `1−R·Rg` 失控。因此：

| 成分 | 处理 |
|---|---|
| 本体反射率 `baseColor·(1−metallic)` | 只进 `1−R·Rg` 分母，不参与着色 |
| 本体辐亮度 `Diffuse + Specular + Ambient + rim` | 整体乘 `eff = T²/(1−R·Rg)`，即透过薄层 |
| 自发光 `emission` | 产生在薄层之下、不受外部光照，只走单程 `T` |
| 全局调色 `fresnel · _Brightness` | 在合成之后乘；织物路径下 emission 也会被乘（非织物路径保持原样） |
| 织物自身反射 | `fabricColor·R·(radiance1 + SampleSH)` |
| 绒面 | `pow(1−NdotV, 3)` 的掠射项，只吃 `radiance1`，不吃环境光 |

织物自身反射用 `radiance1`（含 cel 阶梯、SSS、AO、补光）而不是裸的 `NdotL`，
否则同一块 cel 面上织物会出现连续的明暗过渡，和本体风格割裂。

- 参数：`_FabricColor`、`_FabricCoverage`、`_FabricSheen`，加 LUT `_FabricLut`。
- 库函数（零 CBUFFER）：`Fabric_EvaluateLayer` 采样 R/T、`Fabric_CompositeLayer` 纯合成、
  `Fabric_ShadeLayer` 一次做完；LUT 以 `TEXTURE2D_PARAM` 传入，要求 SM4.5+ 独立采样器。
- 常量：`FABRIC_FIBER_WIDTH`（0.55）、`FABRIC_MAX_OPTICAL`（0.06）、`FABRIC_MAX_KS`（16）、
  `FABRIC_LUT_MAX_SD`（8）、`FABRIC_SHEEN_COLOR`、`FABRIC_TINT_GAIN/BIAS`、`FABRIC_SHEEN_POWER/BASE`。
  `FABRIC_MAX_KS` / `FABRIC_LUT_MAX_SD` 必须与 `Tools/Fabric Lut Baker...` 的 Max K/S、Max S*d 一致，
  否则 LUT 采样域对不上。
- 计算：覆盖度 × 基础光程经 SGGX 投影面积放大成视线方向光学厚度；
  LUT 返回 R（黑底反射率）/ T（透射率）；透射乘 `saturate(fabricColor*3+0.35)` 近似光谱吸收
  （标量 K/S 本身无色）；边缘取 `pow(1−NdotV, 3)` 的 Fresnel 绒面光。
- 近似与边界：`eff` 同时套在镜面与边缘光上——这是**有意为之**，不是近似：从能量流看，本体层的光就是流进织物层、被它压住的。
  织物与皮肤之间只做一阶反弹，没有真正的多次散射；织物层不参与
  ShadowCaster / Depth / DepthNormals，也不支持逐像素附加光源（项目 RPAsset 为 PerVertex）；
  纤维方向用任意正交基，纤维宽度低于 0.4 会在球面出现竖向加厚带，要更细必须让纤维跟 UV 走。

LUT 资产 `Assets/Mine/Shaders/Render/Fabric/FabricLut.asset`（128²，RGBAHalf，
R/T/R∞ 三通道）；`Assets/Mine/Shaders/Render/Fabric/Fabric.mat` 是本 Shader + `ENABLE_FABRIC` 的参考材质。
演示场景已按用户要求移除，验证图留在 `Validation~/2026-10-30-pbrtoon-fabric/`。
三段式重构后用同一套验证场景复渲，与重构前成图逐像素一致（最大通道差 0）。

## 模板来源

通用结构来自共享 standard/shader 模板契约；本文件作为材质算法与迁移案例，不作为完整 URP Lit 替代模板。
