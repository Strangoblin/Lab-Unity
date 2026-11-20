# 通用渲染 Debug 工具

本目录仅存放通用画面诊断工具：DebugOutputFeature 与通用颜色可视化 Shader。功能专用验证 Shader 不作为此目录的常驻资源；正式效果自己的 Debug 分支和 Validation 留在各功能目录。

## 资源

| 文件 | 用途 |
|---|---|
| DebugOutputFeature.cs | 用实际 RenderGraph 临时 RT 执行材质 Pass 0，并输出到相机颜色目标 |
| DebugOutput.shader | `Debug/DebugOutput`：颜色通道、UV、曝光及显示映射 |

## Feature 输入

- **Debug Material**：优先使用指定材质，保留贴图、参数与关键字；材质由调用方持有，Feature 不销毁它。
- **Debug Shader**：无外部材质时创建内部材质，使用 Shader 默认参数；切换输入或释放 Feature 时销毁。
- **Additional Pass Index**：Pass 0 后执行的合法材质 Pass；第二遍输入是第一遍结果，默认 -1 或非法索引为普通复制。
- **Require Depth**：请求场景深度并声明两个阶段读取；还需核对目标相机实际深度有效。
- **Debug**：开启后输出；Feature Active 同时开启，两个输入都为空时不执行。

## 使用与验证

在实际 Renderer Data 添加 DebugOutputFeature，指定材质或 Shader。Shader 使用 URP Blit.hlsl 的 Vert、Varyings、_BlitTexture；专用输入由调用方绑定。

Feature 固定在 AfterRenderingPostProcessing。原效果的阶段、输入或中间资源验证使用该效果自己的 Debug 分支；本工具不改变正式效果的运行路径。

全屏调试、Game View 观察、完整固定尺寸 PNG 与状态恢复统一遵循 [unity-render-debug](../../../../.agents/skills/unity-render-debug/SKILL.md)。静态验证可在 Edit Mode 执行，测试使用临时材质并恢复改过的设置，不保存场景或资产。
