# Standard Shader Templates

Function-independent Shader documentation and code templates.

| Template | Purpose |
|---|---|
| [standard-shader.shader](standard-shader.shader) | Unity 6 / URP 17 标准光照骨架；四 Pass，可编译 |
| [shader-doc-template.md](shader-doc-template.md) | General Shader technical documentation |

Structure conventions: `references/standard/shader/shader-structure.md`.
Feature-bound skeletons (fullscreen postprocess, compute) live under `templates/shader/`.

## 网格材质能力契约（Unity 6000.3 / URP 17.3）

| 能力 | standard-shader | Render 单 Pass 模板 |
|---|---|---|
| UniversalPipeline 标签、统一 UnityPerMaterial、普通 TEXTURE2D、UV ST | 默认 | 默认 |
| 顶点实例 ID 初始化 + multi_compile_instancing | 全部 Pass | 唯一 Pass |
| Stereo 顶点输出与片元眼睛索引初始化 | 默认 | 默认 |
| ForwardOnly / ShadowCaster / DepthOnly / DepthNormalsOnly | 四 Pass | 仅 ForwardOnly |
| 共享顶点位置/法线入口、统一 AlphaClip | 默认 | 按需扩展 |
| 主光阴影、SH 环境光、雾 | 默认 | 按需扩展 |
| 八面体法线编码、方向/点光阴影变体 | 默认 | 不适用 |

- SRP Batcher 与经典 GPU Instancing 是不同提交路径；宏齐全表示兼容，不保证 MeshRenderer 实际合批。通过显式实例化绘制/Frame Debugger 验证，不能仅检查材质复选框。
- 实例 ID 必须在对象变换前初始化；片元使用实例相关数据（如 SH）时传递并恢复 ID。Render 单 Pass 默认片元只采样材质纹理，因此不传 ID；扩展后按需要补齐。
- 普通材质贴图用 TEXTURE2D；TEXTURE2D_X 用于确实需要 XR 纹理数组适配的屏幕资源。
- 标准模板默认不透明；启用 AlphaClip 时，同时按用途配置材质 RenderType/Queue。透明混合另行设计深度与投影策略。
- 顶点变形集中在 SurfacePositionWS，匹配法线集中在 SurfaceNormalWS；相机相关变形须明确阴影策略，不能盲目共用阴影视图矩阵。
- DepthNormals 输出按 URP 编码约定，不把 alpha 当作通用线性深度；颜色、深度、法线、阴影的遮罩需一致。
- 附加光、Lightmap/Meta、MotionVectors、LOD Crossfade、Rendering Layers 输出、DOTS/BRG/间接绘制按需求增加完整数据链与变体，不预先堆入基础模板。
- Stereo 宏仅提供基础结构；XR 设备、MSAA、平台组合需独立验证，不能由普通桌面编译推断支持完整。
- 自定义无 GBuffer 的材质选择 UniversalForwardOnly；需要 Deferred GBuffer 时另建匹配 Pass，不把标准模板当完整 URP Lit 替代品。

## 更新与验收

以项目实际安装的 URP Lit.shader / ShaderLibrary 为版本依据；PBRToon 仅作为材质算法案例。
模板先编译并检查相关变体，再验证多个不同变换实例与普通绘制输出一致；标准模板另外检查各 Pass 的 AlphaClip 和深度/法线输出。
按 [2D / 3D 验证流程](../../../../../skills/auto-manager/capabilities/shader-validation.md) 记录覆盖范围；不得以默认单物体编译代替实例化或 XR 验证。
