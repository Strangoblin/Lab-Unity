# ScreenWarp validation

在已有 Unity Editor 的宿主环境中运行；使用真实 GPU，不启动第二个 Editor，不进入 Play。命令的末尾参数指定本次会话输出目录，省略时生成独立的 `tmp/sessions/` 目录。

```bash
unityctl script execute Assets/Mine/Shaders/PostProcess/ScreenWarp/Validation~/ShaderChecks.cs -- tmp/sessions/<本次任务>/output/shader
unityctl script execute Assets/Mine/Shaders/PostProcess/ScreenWarp/Validation~/PipelineChecks.cs -- tmp/sessions/<本次任务>/output/pipeline
```

## 检查契约

`ShaderChecks.cs`：432 组、2,082,816 像素。对四种映射使用独立的双精度期望（Panini 使用原 URP 未约简几何式）；覆盖宽屏/竖屏/方屏、中心/偏心、强度0/0.35/1、Zoom1/1.8、两种边界、UV/越界Debug、HDR与非恒定Alpha。216组使用两倍物理纹理、仅左下有效视口，外部填哨兵颜色；一个已知禁用效果的负控制必须被检出。单像素RGBA绝对误差阈值0.0015，NaN/Inf失败，空样本失败。

`PipelineChecks.cs`：30次真实URP SingleCameraRequest。创建并清理PreviewScene和方向色块/近远网格，只在当前RP两组renderer数组同时追加临时尾槽，保留原renderer实例。覆盖四模式、横竖屏、透视/正交、Off与零强度（含非默认Zoom）一致、UV/边界和主后处理开启路径；核对每次相机的投影/视图/姿态/FOV/正交尺寸/aspect/targetTexture不变，恢复原数组、Feature列表及开关、场景dirty与活动RT，渲染Error/Exception/Assert日志必须为零。输出JSON与完整PNG。

脚本不保存资产或场景。临时renderer Dispose会正常释放它拥有的资源，也可能触发URP共享缓存释放；不会Dispose原用户renderer。脚本不是Camera Stack、XR、硬件动态分辨率、与其他项目Feature组合或性能测评的覆盖证据。

## 2026-10-19 实际结果

- Unity 6000.3.14f1 / URP17.3.0 / macOS Metal。
- C#刷新编译通过；实际ScreenWarp Shader单Pass在Metal绘制通过。
- Shader：432组通过，0失败，最大RGBA绝对误差 `0.00003629923`，负控制检出。归档到本目录后复跑同结果。
- URP：30请求、362断言通过，0渲染错误；相机与原场景/RP状态恢复检查通过。归档后复跑通过。
- 当前PC_Renderer接入后，用真实Main Camera生成640×360开/关图，相机投影和姿态保持；已检查完整PNG方向及画面覆盖。默认边界为黑色，测试图的洋红边界是用于验证的显式参数。
- 没有采集GPU性能，不把单Pass/单采样直接换算为耗时收益；最终艺术效果待用户调参观察。

记录和最小报告归 `.agents/memory/unity/2026-10-19-screenwarp-v1.md`，运行输出在相应 `tmp/sessions/` 目录。
