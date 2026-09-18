# shader-validation — 2D / 3D 渲染检查

编译后按 Shader 的使用方式选择分支；只执行与本次改动有关的检查，不把完整矩阵变成每次必跑的门禁。

| 类型 | 验证载体 | 入口 |
|---|---|---|
| 2D 全屏 / 后处理 | 复用 `Assets/Mine/Shaders/PostProcess/Debug/` 调试 Shader 和固定 DebugOutputFeature，在真实相机管线观察 | [屏幕调试方法](../../../agents/unity-developer/references/shader/postprocess/feature-script-structure.md#4-屏幕调试方法rendererfeature-屏幕调试) |
| 3D 网格材质 / 顶点变形 | Mesh + Material + Camera + Light；可先用隔离预览场景，再按依赖补真实场景验证 | 下述流程 |

这里的 2D 指全屏处理，不指所有 Sprite/UI。依赖场景深度、法线、中间 RT 或自定义 Feature 的材质，仍需真实管线集成验证。

## 共用：编译与证据

- `asset refresh` 只说明资源刷新/C# 编译完成，不代表全部 Shader Pass/变体已经编译。
- 导入目标 Shader 后查询 `ShaderUtil.GetShaderMessages`；需要时对相关材质调用 `ShaderUtil.CompilePass(material, pass, true)` 同步编译改动涉及的 Pass/关键字变体。
- 临时关键字在材质副本上设置；记录本次实际覆盖的 Pass/变体，不宣称穷举编译。
- 区分旧日志和本次错误。Editor 不可用时报告静态检查结果，并明确未做 GPU 验证。

## 3D：设计可检查的载体

1. 先列出 Shader 输入契约：顶点通道、UV、法线/切线、拓扑、材质参数和依赖的场景资源。
2. 选能暴露目标问题的最小网格与材质；现有资产合适则复用，需要特殊拓扑/UV 时再生成。
3. 检查 Unity 导入后的 Mesh，而不只检查源文件：顶点/三角形、索引、退化面、绕序、UV、法线、材质与 prefab 引用。
4. 顶点变形需核对 Mesh/Renderer bounds 覆盖支持的参数范围，避免离开原包围盒后错误剔除。

例如逐面 billboard 可检查每个 quad 的四个 UV 角点与独立顶点；384 面等 Plant 专有数值不作为通用断言。

## 3D：隔离渲染与对照

- 临时 Roslyn 验证脚本依照 [脚本决策](script-decision.md) 放在 `tmp/`；不将特定材质的阈值直接提升为通用脚本。
- `EditorSceneManager.NewPreviewScene()` 中放置网格、材质、相机和光；显式绑定 `camera.scene`，避免混入用户场景。
- URP 使用 `RenderPipeline.SubmitRenderRequest` + `UniversalRenderPipeline.SingleCameraRequest` 输出 RenderTexture；不能用 CPU 数学复算代替实际 GPU 输出。
- 无帧间行为的静态材质可在 Edit Mode 验证；时间动画、生命周期、运行时资源初始化需进入 Play Mode。
- 验证前记录修改的状态；在 `finally` 恢复活动 RT、释放临时纹理/材质并关闭预览场景，不保存或清理用户场景物体。

| 改动涉及 | 按需选择的对照 |
|---|---|
| 相机方向 / billboard | 正面、斜视；透视与正交（若声明支持） |
| 坐标空间变换 | 相机和物体共同平移；物体旋转、均匀/非均匀缩放 |
| 参数驱动变形 | 零值/默认/支持的边界值及剔除边界 |
| Alpha / 深度 / 法线 | Forward 与 DepthOnly/DepthNormals 的位置和遮罩一致性 |
| 阴影 | 光源类型、投射/接收及轮廓；编译通过不等于阴影正确 |
| Scene 纹理 / Feature | 回到真实 Renderer 配置和场景检查依赖与交互 |

## 判定与报告边界

- 先定义预期不变量再比较；共同平移对照只适用于不依赖世界固定纹理/风场等条件的效果。
- 非空像素/特定颜色计数只能作冒烟检查；绿色像素阈值不能验证任意材质、billboard 朝向或视觉质量。
- 像素回读可用于 3D 隔离实验的数值对照，不替代既有 2D 真实管线检查；阈值需考虑抗锯齿和浮点误差。
- 截图/预览图是按需辅助，不要求留档，不作为通过门禁；视觉质量由用户在 Editor/Game View 判断。
- 报告分别列出：数据通过、已编译范围、实际渲染覆盖、未验证项/近似。预览通过不等于完整项目集成通过。
