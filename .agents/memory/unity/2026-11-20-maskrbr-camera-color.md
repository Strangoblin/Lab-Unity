# MaskRBR 读取透明混合后的相机颜色

MaskRBR 最早在 `AfterRenderingTransparents` 录制，旧配置的较早事件在入队时提升。实际 `PC_Renderer` 的序列化值为 `AfterRenderingSkybox`，运行值已核对为 `AfterRenderingTransparents`。彩窗无需单独的材质 Pass。

共享 `ScreenMaskRenderer` 可按调用方指定的覆盖 Pass 追加透明 RendererList。两种队列各自遵守 Renderer 层掩码和相机剔除，采样相机深度剔除被遮挡轮廓。RGB 取覆盖区域内的相机混合色，透明度和背景颜色随相机颜色进入径向采样。覆盖用 R8，彩色源和径向结果用 RGBA16F。主方向光决定焦点及角窗，合成保留相机 Alpha。启用 SSC 遮光时，可用 300 / 500 / 550 排列云缓冲、MaskRBR、云合成。 [功能说明](../../../../Assets/Mine/Shaders/PostProcess/MaskRBR/README.md)

Unity 6000.3.14f1 / Metal 验证通过。Shader 47/47，隔离 URP 管线 38/38，SSC 遮光 10/10。实际彩窗用例覆盖选中双面透明、遮挡与层排除、混合后 RGB、轮廓外彩色光束和 Alpha 保留。输出在 `tmp/sessions/2026-11-20-maskrbr-camera-color/output/`。Git 基准 `bf44149`，保留任务开始前已有改动。

## 天空与物体来源隔离

早期实现以远平面深度识别天空，再读取透明混合后的 RGB。`ZWrite Off` 的未选透明物体能经由天空分支给光束染色。现在在 `BeforeRenderingTransparents` 保存天空和不透明画面，`AfterRenderingTransparents` 用 RGBA8 区分选中不透明（R）、选中透明（G）、覆盖天空的透明轮廓（B）。`Sky` 取远平面且 B 为零的像素，`Objects` 只取 R/G 的并集。仅 G 的像素读取透明混合色，其他来源使用早期快照。

Metal 验证通过 Shader 47/47、URP 45/45、SSC 遮光 10/10。未选玻璃覆盖天空和已选不透明物体的用例中，只改变玻璃颜色会改变最终相机画面，三个 Source 的光束 RGB 均逐像素一致。输出在 `tmp/sessions/2026-11-20-maskrbr-source-isolation/output/`。
