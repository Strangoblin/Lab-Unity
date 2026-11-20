# ColorfulWindow 的 Metal 穿墙光（2026-11-19）

Unity 6000.3.14f1 / URP 17.3 / Apple M2 Metal。`ColorfulWindowTest.unity` 的墙体投影开启。关闭 `SSLFeature` 后，室内墙体遮挡与窗洞内的地面彩色光斑正常。

`Assets/Mine/Shaders/PostProcess/SSL/SSL.shader` 的 `ReconstructWorldPos` 读取 `_CameraDepthTexture`，重建视线步进的终点。`SSLFeature.cs` 用 `ConfigureInput(Depth)` 请求深度，但 `RecordRenderGraph` 的 Unsafe Pass 只声明场景色和内部中间纹理。未声明的深度读取使 RenderGraph 无法保证深度纹理在 SSL 执行时仍有效。读取到不匹配的深度时，步进可以进入墙后，再把散射光叠到墙面。

## Metal 对照

同一个 `SingleCameraRequest` 路径、室外相机、768×432、同一 SSL Shader、Medium 档。以室外相机的 SSL Debug 输出为判据（保留默认后处理），临时复制原 SSL Pass，仅控制其 RenderGraph 读取声明。

- 室外无声明、只声明主光阴影图、只声明彩色透射图、声明两者的四张输出完全一致，窗下墙面有彩色条纹。对应 `repeat-out-{0,1,4,5}.png`。
- 只读 `resourceData.cameraDepthTexture`，或与另外两项任意组合，四张输出完全一致，墙面彩色条纹消失。对应 `repeat-out-{2,3,6,7}.png`。
- 缺失依赖的表现随前序渲染变化。连续同相机请求也可能恰好读到有效结果。替换生成着色器单独量步进终点并关闭后处理后，两组都读到约 4.55 m，该探针没有重现错误的深度值。
- 初始室内整面泛白在停用 SSL 时消失。关闭模糊或仅启用级联选择未消除这一现象。原始 SSL Raymarch 缺少 `_MAIN_LIGHT_SHADOWS_CASCADE` 变体，在四级阴影设置下固定用第 0 级，属于独立的阴影采样问题。

对照产物与脚本在 `tmp/sessions/2026-11-19-colorfulwindow-metal-PgS6tq/`。诊断脚本采用内存中的 Pass 和 Shader，全程恢复临时设置。

## 修复落地

`SSLFeature.cs` 的 Unsafe Pass 已声明 `cameraDepthTexture` 为读取项。深度句柄无效时跳过 SSL。Unity 重新编译通过，Apple M2 / Metal 上用实际序列化的 Renderer 和项目编译产物复测。室外首个请求与室内切到室外的 SSL Debug 图均与深度声明对照 `repeat-out-2.png` 逐字节一致。室内合成保留彩色光柱和地面光斑，室外墙面无复现出的彩色条纹。见 `applied-*.png`。

原版在同一台 Metal 机器上也会随渲染顺序出好图。未声明的读取只要恰好仍能取到有效深度，就不会暴露。此前 Windows / D3D12 有正常画面，本次验证未复测 Windows，也未抓取其具体资源生命周期。
