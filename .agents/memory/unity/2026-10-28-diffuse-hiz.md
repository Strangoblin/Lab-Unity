# DiffuseGI HiZ 实验，2026-10-28

用户确认上一轮优化并授权先为 Diffuse 加入 HiZ；Specular、AO、Renderer 和场景的持久配置未改，保留既有脏改动。默认仍 DDA。Diffuse 的 Experimental HiZ 仅在 DDA 下生效，需指定 `Assets/Mine/Special/Rendering/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute`；选择 Ray 时不启用。

复用已有 `ScreenSpaceDepthPyramid.TryGet` 和 `ScreenTrace_TraceHiZ`。统一与独立入口在 Trace 前按需构建同相机同帧正眼深金字塔，Trace 显式声明依赖，历史签名记录实际接线是否启用。缺 compute、部分视口、动态尺寸、非显式网格或服务不支持的 XR/MSAA 回退 DDA，警告一次；Shader 另外核对实际深度网格及 UV 映射，不匹配时逐射线走 DDA。当前 URP 17 在相机全局中将 `_RTHandleScale` 设为 1，不能误用 `RTHandles.rtHandleProperties.rtHandleScale`（真实测试中后者为 0.24×0.20）。当时的 HiZ 是旧 SSR 风格 mip 步幅，不是保守遍历，可能漏命中、耗尽预算；此处未更改通用库。该步幅已于 2026-10-30 改为像素步幅（见 [SSGI 共享 Trace 记录](2026-10-30-ssgi-shared-trace.md)）。

Unity 6000.3.14f1 / M2 Metal：编译通过；SSGI 原回归 94 组 / 1,153,554 断言与 5 个负控制通过。新增真实相机 HiZ 172,490 断言：三档、统一/独立、缺资源和部分视口 DDA 回退、Ray 隔离、只失效 Diffuse 历史；将实际粗层读取注入零值后输出改变，证明生产路径确实读取金字塔。Shader 内网格不匹配的分支及动态分辨率、XR、其他 API 未做实机覆盖。

固定 Main Camera、隔离 Diffuse 克隆、时缓 0.5、DDA 与 HiZ 各 8 次预热 / 12 次同步回读 / 三轮交错，包含金字塔构建。[原始 24 块报告](evidence/2026-10-28-diffuse-hiz/timing.json) 在当前场景中 1080p High 三轮块中位数 DDA 20.408 / 20.693 / 19.558 ms，HiZ 15.361 / 12.269 / 12.784 ms，分别约低 25%、41%、35%；654×393 两档和 1080p Medium 的轮间方向或幅度波动较大，未分辨稳定差异。测量是整次 SubmitRenderRequest + 单像素 ReadPixels 墙钟，不是 GPU 时间或 Game View 帧率，不能保证等画质。

同一复制视角的 640×360、无时缓、Medium/High 固定单帧对照 PNG 和 [数值](evidence/2026-10-28-diffuse-hiz/visual.json) 已保存；它们包含后续透明物体绘制，不能代替纯间接光误差图或运动画面验收。待用户检查薄物体、漏光、噪声及移动画质。临时 fixture 已清理；未提交。
