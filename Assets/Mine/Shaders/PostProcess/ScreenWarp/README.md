# ScreenWarp

独立的屏幕颜色重映射后处理，可选叠加 RGBA 屏幕遮盖。读取已完成主后处理的场景颜色，对每个输出像素反查一次源图；不修改 Camera 的 Transform、FOV、投影矩阵，也不读写场景深度、法线或运动矢量。

## 使用

在目标 Universal Renderer 的 Renderer Features 中添加 `ScreenWarpFeature`。确认 Shader 引用为 `PostProcess/ScreenWarp`（`Create` 会尝试自动找到它；构建时应保存此显式引用）。调整 Mode 与 Strength；`Strength = 0` 只关闭变形，只有无有效遮盖且 Debug 为 Off 时才完全跳过（不录制 Pass、不创建输出纹理）。

遮盖独立于变形映射：为 `Overlay Texture` 指定一张普通 RGBA 贴图（建议与输出比例一致，中心透明、边缘不透明的 straight-alpha PNG；导入时保留 Alpha，启用 Alpha Is Transparency 可减少透明边缘色晕），再按需调节 `Overlay Opacity`。`Overlay Opacity = 0` 或贴图为空时遮盖关闭；旧配置未指定贴图时行为与之前完全一致。遮盖以输出逻辑 UV 0–1 铺满屏幕，不跟随 warp 或 RTHandle 的源 scaleBias；`Border` 仍作为遮盖下方越界区域的 fallback。Debug 的 SourceUV/OutOfBounds 不显示 overlay，保持专注变形诊断。

默认用于独立 Game/Base 相机。Scene View 需启用 Show In Scene View。若要扭曲同阶段其他效果的结果，把 ScreenWarp 放在这些 Feature 之后。

## 参数

| 参数 | 默认 | 含义 |
|---|---|---|
| Shader | 自动查找 | 保存到 Renderer 的 Shader 引用 |
| Show In Scene View | false | 允许 Scene View 预览；其后绘制的 Gizmo 不变形 |
| Border | Color | Clamp 延展最边缘像素；Color 用 Border Color 填充图像之外 |
| Border Color | 黑色、不透明 | 越界区域 RGBA；支持 HDR |
| Mode | Barrel | Barrel 桶形、Pincushion 枕形、Fisheye 鱼眼式、Panini |
| Strength | 0.35 | 0–1；连续插值采样坐标，包含 Zoom，不叠加原图形成重影 |
| Center | (0.5, 0.5) | 逻辑 UV 中的变形中心，每轴限定 0–1 |
| Zoom | 1 | 1–2；放大、裁切已有图像，减少露边；不会增加相机视野 |
| Overlay Texture | null | 可选 RGBA 遮盖贴图；为空时遮盖关闭 |
| Overlay Opacity | 1 | 0–1；贴图为空时解析为 0，遮盖关闭 |
| Debug | Off | SourceUV：RG=源UV，B=有效范围；OutOfBounds：绿=有效、红=越界 |

强度在 Feature 入口钳制；NaN/Infinity 参数回退到安全值。变形零强度时采样映射为恒等，即使 Center/Zoom 为非默认值；有效遮盖仍可独立显示。四种模式均使用同一强度，而不是相机焦距；切换模式不会改变相机参数。

## 映射契约

映射是 `输出逻辑 UV -> 源逻辑 UV`。先复用 TRS 的等比坐标桥接，再以 Center 到最远屏幕角的距离为单位半径，因此横屏、竖屏和偏心时均覆盖在单位圆内。Zoom >= 1 保持这个有界定义域。

- Barrel：逆向采样半径乘 `1 + 0.6 r²`。
- Pincushion：逆向采样半径除 `1 + 0.6 r²`，支持域内不翻折。
- Fisheye：等距径向模型的反查 `tan(1.2 r) / 1.2`；最大角小于 90°，中心连续。它只重映射已有画面，不提供 180°/全景采集。
- Panini：单位距离 d=1 的反查，向量除 `1 - x²/4`；保持垂直线笔直。此处采用固定归一化屏幕平面，不读取或校准 Camera FOV。

完整映射（包括 Zoom）再与原 UV 按 Strength 插值。正常无贴图时仍只进行一次双线性颜色采样，保留 RGBA/HDR；启用遮盖时在同一 Pass 内增加一次 2D 纹理采样，以 straight RGBA source-over 与覆盖率 Alpha 合成，`Overlay Opacity = 0` 或贴图 Alpha 为 0 处严格保持原底图。先处理越界填色，再叠加素材，不使用 discard。采样钳制到有效源视口的半像素边界，避免读到 RTHandle 的未使用区域。

## 调度与资源

`AfterRenderingPostProcessing`，一个 Raster RenderGraph Pass、一张新颜色目标，之后把它交回 `UniversalResourceData.cameraColor`。不原地读写相机色，不使用第二次拷贝。遮盖在同一 Pass 内完成，不新增 RT 或 Pass。`requiresIntermediateTexture=true`；`ConfigureInput(None)`，不会为此效果额外请求 opaque copy/depth/normals。

URP 17.3 实际顺序是主后处理/TAA -> ScreenWarp -> FinalPost（FXAA/部分缩放与锐化）/FinalBlit。未开启 URP 主后处理时也可执行。普通 Screen Space Overlay UI 在其后合成；Camera/World Space UI、场景描边等若已写入场景颜色，会一起变形。

参数在录图时按相机快照，在执行回调中绑定，避免延迟执行串用材质参数。场景颜色输入/输出由 RenderGraph 管理；用户指定的遮盖贴图只借用，不创建、销毁或修改。Feature 只拥有运行材质，在重建、替换 Shader、Dispose 时释放。源尺寸从实际颜色描述与 RTHandle 有效视口取得，不用 Camera 的 FOV/投影推算变形。

## 首版边界

- 跳过 Overlay、非最终 Base Camera、Preview、Reflection、立体相机；Camera Stack 与 XR 未接入。
- 画面改变后，现有 GPU Picker/鼠标坐标不会自动反向映射；点选交互需后续接入同一映射契约。
- 已完成的 SSGI/景深/阴影等颜色结果整体变形；深度、法线、运动矢量保持原布局。后续若再接依赖深度的效果，必须处理颜色与几何缓冲的坐标关系。
- 大幅扭曲会放大采样、裁切画面或露边。Zoom 只能裁切已有内容。
- 不能恢复视锥之外或被前景遮挡的颜色，不能生成真实运动视差。深度引导的 Dolly Zoom 近似需要另一个输入契约与遮挡/空洞处理，不混入本无深度 Pass。

## 实源关联

- RenderGraph 调度、源视口处理基线：`../Weather/WeatherFeature.cs`，改为单颜色 Pass，不复用其粒子/雾/深度逻辑。
- 等比坐标：`../../../Special/HLSL/TRS.hlsl` 的 `TRS2D_UVToIsotropic` / `TRS2D_IsotropicToUV`，只调用、不修改共享库。
- Panini：已安装 URP 17.3 的 `Shaders/PostProcessing/PaniniProjection.shader` 中 `Panini_UnitDistance` 的解析简式。没有把 HDRMap 或材质三平面投影当作相机投影。
- 映射策略保持在 `ScreenWarpFunction.hlsl`；没有为尚未实现的深度重投影或世界弯曲建立处理器框架。

验证入口及实际证据见 `Validation~/README.md`；图像质量仍需在目标场景人工观察。
