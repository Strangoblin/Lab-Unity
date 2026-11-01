# ScreenWarp validation

在已有 Unity Editor 的宿主环境中运行；使用真实 GPU，不启动第二个 Editor，不进入 Play。命令的末尾参数指定本次会话输出目录，省略时生成独立的 `tmp/sessions/` 目录。

```bash
unityctl script execute Assets/Mine/Shaders/PostProcess/ScreenWarp/Validation~/ShaderChecks.cs -- tmp/sessions/<本次任务>/output/shader
unityctl script execute Assets/Mine/Shaders/PostProcess/ScreenWarp/Validation~/PipelineChecks.cs -- tmp/sessions/<本次任务>/output/pipeline
```

## 检查契约

`ShaderChecks.cs`：752 组、3,741,696 像素。保留原 432 组映射检查，对四种映射使用独立的双精度期望（Panini 使用原 URP 未约简几何式）；覆盖宽屏/竖屏/方屏、中心/偏心、强度 0/0.35/1、Zoom 1/1.8、两种边界、UV/越界 Debug、HDR 与非恒定 Alpha。新增 320 组遮盖检查：横竖屏、四模式、强度 0/1、贴图 Alpha 0/0.35/1、不透明度 0/0.4/1；独立的输出 UV 渐变贴图包含 HDR，校验 straight RGBA source-over、场景/边界 Alpha 与 Debug 绕过遮盖。376 组使用两倍物理纹理、仅左下有效源视口，外部填哨兵颜色，遮盖仍覆盖完整输出 UV。两个负控制必须检出：禁用变形、禁用遮盖。单像素 RGBA 绝对误差阈值 0.0015，NaN/Inf 失败，空样本失败。

`PipelineChecks.cs`：52 次真实 URP SingleCameraRequest。创建并清理 PreviewScene 和方向色块/近远网格，只在当前 RP 两组 renderer 数组同时追加临时尾槽，保留原 renderer 实例。原 30 请求覆盖四模式、横竖屏、透视/正交、无遮盖时 Off 与零强度（含非默认 Zoom）一致、UV/边界和主后处理开启路径。新增 22 请求用可重建 RGBA 方向图/透明中心边框，核对空纹理与 opacity=0 保持底图、零变形仍能显示遮盖、遮盖固定屏幕位置、透明中心保留变形场景、调试视图绕过遮盖；每个区域对照同时要求可见正控。核对相机投影/视图/姿态/FOV/正交尺寸/aspect/targetTexture 不变，恢复原数组、Feature 列表及开关、场景 dirty 与活动 RT，渲染 Error/Exception/Assert 日志必须为零。输出 JSON 与完整 PNG。

脚本不保存资产或场景。临时 renderer Dispose 会正常释放它拥有的资源，也可能触发 URP 共享缓存释放；不会 Dispose 原用户 renderer。遮盖测试纹理由脚本生成和销毁，不导入资产。脚本不是 Camera Stack、XR、硬件动态分辨率、与其他项目 Feature 组合或性能测评的覆盖证据。

## 2026-10-19 遮盖扩展结果

- Unity 6000.3.14f1 / URP 17.3.0 / macOS Metal；C# 刷新编译通过。
- Shader：752 组通过（其中遮盖 320 组），0 失败，最大 RGBA 绝对误差 `0.00003629923`，两个负控制检出。
- URP：52 请求（基础 30 + 遮盖 22）、632 断言（遮盖 270）通过，0 渲染错误；相机与原场景/RP 状态恢复检查通过。
- 已查看横竖屏完整 PNG，方向正确、遮盖在屏幕边缘，中心显示变形后场景。方向色块仅为验证输入，未替用户创建正式素材。
- 使用同一 Pass/RT；开启遮盖多一次贴图采样。未采集 GPU 性能，实际艺术效果由用户素材与调参决定。

记录和最小报告归 `.agents/memory/unity/2026-10-19-screenwarp-overlay.md`，运行输出在相应 `tmp/sessions/` 目录。

## v1 历史基线

首版为 432 组 / 2,082,816 像素 Shader 检查、30 请求 / 362 断言 URP 检查；已接入 PC_Renderer，Main Camera 开/关 PNG 中相机参数保持。历史记录和报告保留在 `.agents/memory/unity/2026-10-19-screenwarp-v1.md`，不以本次结果覆盖。
