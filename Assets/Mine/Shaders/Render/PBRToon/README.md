# PBRToon — Unity 6 / URP 17 材质结构

Shader 名称：`Render/PBRToon`。保留现有 Toon、SSS LUT、补光、各向异性、环境反射、边缘光和描边计算。

## 管线能力

- 五个 Pass：Forward（UniversalForwardOnly）、Outline、ShadowCaster、DepthOnly、DepthNormals（DepthNormalsOnly）。
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

## 验证（2026-09-17）

- 模板先更新并通过 GPU 检查，再迁移本 Shader。
- 五 Pass 及代表性关键字变体编译无错误；非均匀缩放/旋转的双实例与独立绘制对照通过。
- Forward / Outline 在受控无雾对照中与迁移前输出一致；实例化 Forward 平均 RGB 差约 0.0024（0–255）。
- ShadowCaster / DepthOnly / DepthNormals 的实例化深度对照差为 0。
- 当前 Main Camera 渲染通过，场景中两个使用本 Shader 的对象无新增 Shader 错误。
- 未完成所有灯光、XR、构建平台组合的视觉验收；阴影轮廓的自动检查使用零偏置的受控条件。

## 模板来源

通用结构来自共享 standard/shader 模板契约；本文件作为材质算法与迁移案例，不作为完整 URP Lit 替代模板。
