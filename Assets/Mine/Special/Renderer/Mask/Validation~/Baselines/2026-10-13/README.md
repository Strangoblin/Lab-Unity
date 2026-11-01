# 2026-10-13 迁移前图像输入

这 22 份基线来自公共 Mask 抽取前的 MaskBlur／MaskRBR／PBRToon 实现，由 PipelineEquivalence 的隔离 Game 相机实际渲染，全部来源／权重／遮挡／扩散控制通过。Git 起点 f9b3ad8，但工作区含未提交实现，精确输入版本以 `source-before-sha256.json` 为准；不是对 Git HEAD 的截图。

文件编码为 `.rgba-f32.bin.gz.b64`：先 Base64 解码，再 gzip 解压，得到 320×180×4 个 little-endian float32，RGBA、左下起始行序、线性颜色，没有色调映射。原捕获报告使用 `.bin.gz` 文件名，归档增加 Base64 文本层以适配受管文件通道；`input-sha256.json` 同时记录编码文件与解压数值的 SHA256，解码数值与原输入逐字节一致。

所有者为 ScreenMask 验证模块，依赖上级 `PipelineEquivalence.cs` 的固定场景、参数、Unity／URP／Metal 环境。它们是不可从当前新版反推的旧实现输入，不是可重建运行输出。需要新的基线时使用独立日期目录并登记行为版本；版本被正式替代后再按精确清单评估清理。
