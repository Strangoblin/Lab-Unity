# SSGI 历史纹理生命周期（2026-11-03）

切换场景后，Scene View 的 SSGI 在 `graph.ImportTexture` 报 Invalid import，后续出现 RenderGraph / ZBinningJob 异常；GrassManager 不在异常堆栈。日志中切场景后出现资源卸载，随后历史导入失败，历史纹理被卸载是与证据一致的推断，不断言为唯一触发方式。

`SSGITemporalFilter` 自建 RenderTexture 交给 RTHandle 持有所有权并设置 HideAndDontSave；使用前检查两侧纹理存在且 IsCreated。深度历史失效重建整套相机历史；颜色失效只重建对应信号并清除有效位。修复前的 Editor 探针销毁底层纹理后确认旧代码保留非空但纹理失效的句柄；修复后非侵入探针确认健康复用、颜色通道隔离恢复、深度双缓冲恢复、RenderGraph 导入、尺寸变化和重复释放。真实场景切换复测尚待确认，未改用户 Renderer 的现有状态。
