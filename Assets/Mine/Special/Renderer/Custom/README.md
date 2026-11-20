# Custom — 通用自定义管线能力 / 模板

可配置的 RenderGraph 自定义管线基线：按层捕获 Depth / Normal / Color，输出到可命名的全局纹理或相机目标。
它不是某个效果的实现，也不挂载在任何 Renderer 上；专用管线可以直接配置它，或把它当作编写新 Feature 的参考。

## 组成

| 文件 | 作用 |
|---|---|
| `CustomFeature.cs` | `Mine.Rendering.CustomFeature`：Depth / Normal / Color 三个捕获 Pass，层掩码、pass tag、输出全局名、队列与事件均可配置 |
| `Shaders/Custom.shader` | `Custom/Custom`：把捕获到的深度 / 颜色 / 法线按对象 UV 采样的模板 Shader |

## 配置

| 设置 | 说明 |
|---|---|
| `copyDepth` / `copyColor` / `copyNormal` | 选择要录制的通道；三个都关时 Feature 不入队 |
| `depthTextureName` / `normalTextureName` | 发布的全局纹理名，默认 `_CustomDepthTexture` / `_CameraNormalTexture` |
| `depthLayerMask` / `colorLayerMask` / `normalLayerMask` | 每个通道独立的层集合 |
| `depthPassTags` / `normalPassTags` / `colorPassTags` | 每个通道匹配的 `LightMode`；数组内靠后的 tag 优先 |
| `transparentMode` | `false`：不透明队列，Depth/Normal 在 `BeforeRenderingPrePasses`、Color 在 `AfterRenderingTransparents`；`true`：全队列，Depth/Normal 在 `AfterRenderingTransparents`、Color 在 `BeforeRenderingTransparents` |

## 使用方式

1. **直接配置**：挂到相机的 `UniversalRendererData`。若要由 Feature 拥有相机目标，把该 Renderer 自身的 `opaqueLayerMask` / `transparentLayerMask` / `prepassLayerMask` 置 0，URP 就不再画自己的 opaque / transparent / prepass。
2. **作为模板**：复制 `CustomFeature.cs`，把默认 tag、输出名和事件顺序改成专用管线需要的形式。专用管线应拥有自己的模块目录与 README，不要反过来把专用逻辑塞回这里。

## 边界

- Color Pass 会清空相机颜色目标为透明黑，再画颜色层；Overlay 相机直接跳过，避免抹掉 Base 相机结果。
- Depth / Normal 是全局纹理的生产者：`SetGlobalTextureAfterPass` + `AllowPassCulling(false)`，空捕获也会产出清空后的纹理。
- Color Pass 若同时开启 `copyDepth`，会把相机深度附件绑成参照面；同一资源不能既当附件又当全局纹理，所以参考深度统一从 `_CameraDepthTexture` 读取。
- 本模块不定义任何效果的语义（互动穿透、体积光、彩色透射等）；这些属于各自的专用模块。
