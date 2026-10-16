# DiffuseGI 静态噪声诊断证据

- 所有者：unity-developer 的 `2026-10-13-diffuse-weak-temporal-diagnosis.md`。
- 用途：历史 Specular 修复对照后，保存弱时缓0.5的受控线性 GI 帧差、冻结/RAY控制与原状态恢复，支持选择下一轮空间重建候选，不代表修复完成。
- 依赖：当日 Unity 6000.3.14f1 / URP 17.3 / M2 Metal、当前SSGI程序集/Shader、SampleScene几何及报告中的SceneView pose/源设置。没有真实时间推进、运动画质或其它平台结论。
- 保留条件：该问题诊断基准；未来不再需要时，随dated memory按精确清单人工确认清理。新测试输出仍进入tmp/sessions，不覆盖本目录。

## 文件

| 文件 | 用途 |
|---|---|
| jitter.json / probe-cli.json | 8组实际回读与成功CLI；CLI内tmp路径为原运行落点，对应报告并排归档 |
| Probe.cs | 本版唯一临时诊断驱动；当前CameraChecks脚手架加Jitter入口，复制到新任务再配置输出位置以复现；非生产权威，不保存退役逐格/slab源 |
| live-config.json / live-final.json | 实际源Feature/Active/设置，原配置保持 |
| protected-before.sha1 | 四个受保护资产与Shader源码未变 |

诊断入口为 `unityctl --json script execute <新任务中的Probe.cs> --method Jitter -t 240`。驱动要求当前Edit Mode、唯一Active SSGI、Medium Diffuse，使用当前SceneView（无则MainCamera）的姿态；只创建临时Game/Base相机/Renderer。先把驱动 `_output` 配置到新任务唯一输出目录，不把归档报告或固定旧tmp地址当运行依赖。帧差来自自有持久history RT，不受最终显示覆盖影响；冻结采样仅用于归因。
