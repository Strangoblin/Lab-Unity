# Weather 验证记录

实施与验证：2026-10-08～2026-10-09。环境：Unity 6000.3.14f1、URP 17.3.0、macOS Metal / Apple M2。全部 Weather 自有内容位于本目录；C# 脚本迁移保留 GUID，旧天气可导入目录已按用户授权移除，历史快照保存在本目录 `Validation~/Legacy/`；共享噪声未迁移或改写。

## 已验证

| 验证 | 结果与范围 |
|---|---|
| 导入／编译 | C#与Shader编译成功，Weather Shader supported，两个命名Pass；迁移后再次编译成功 |
| Profile契约 | 原有13项与新增23项通过：迁移值、模块端点、TileCount／Scale默认与边界、非有限输入纯解析、快照隔离和连续Bind覆盖 |
| Feature契约 | 29项通过：材质复用／更换／释放、一次诊断、12组Depth／Debug需求、值快照、相机与资源筛选；EditMode确认原生材质销毁 |
| 兼容基线GPU | 100项通过：18组A/B、48种模块组合、31个端点及3个控制；A/B固定两个纵横比、三个时间，373248个像素，RGBA均有限 |
| 正常帧预算 | 8项通过，实测Weather Pass数0／1／1／2；四种单模块Debug各1，状态完整恢复 |
| 当前真实管线 | 105项通过、112次StandardRequest；原有96项加9项粒子矩形网格／尺度边界，涵盖三天气、模块组合、Debug、相机几何、renderScale与Stack跳过 |
| 全关 | 真实管线与禁用Feature同帧输出误差为0；正常路径入队前筛除 |
| RT几何 | 320×180目标、renderScale=0.75时，实际绑定240×135；不同纵横比的输出均有限 |
| 隔离 | 测试结束恢复Profile、Debug、Feature启用状态、renderScale及全局值；临时对象释放；运行日志没有Error／Exception |
| 旧资源 | 20个历史快照／共享Noise2DRG基线哈希均未变化；旧目录不存在且不可加载，Weather只有1个导入Shader |

GPU A/B容差为 `abs(new-ref) <= 0.001*(1+abs(ref))`。最大绝对误差0.000619531，最大归一化误差0.000451983。Rain参考Shader仅在内存中取消旧blur合成，Sand／Snow使用原Shader；Tone关闭与正霜边控制均检测到实际差异，避免空输出通过。

## 基线与当前调参

兼容基线对应模块检查点 `7fcc8e4`。2026-10-09工作区已有粒子视觉调整：Rain网格4.5×4.5，Sand网格3×3与尺寸0.2，Snow采用1.5×1.5分区与新的位移。本次将网格和尺度提取为Profile参数，保留上述默认值。

上表100项GPU结果对应调优前的兼容基线；统一随机前的调参版本通过96项真实管线、8项正常帧预算及82项GPU模块／端点控制；18项严格旧粒子响应对照记录了上述调参引入的预期差异。`ShaderChecks.cs`仍用于严格检查旧算法兼容性，调优后运行会报告粒子响应差异。当前Renderer选择Rain并关闭Weather，测试保留这一状态。

## 共享粒子变换与统一随机（2026-10-09）

三天气通过 `Weather_CreateParticleContext` 共用分块、相位、周期随机、位移、旋转、尺度与等比TRS逆变换；种子统一包含网格坐标和周期索引，位移使用random.xy，旋转与随机尺度均使用random.x。Sand不再使用PI旋转系数与random.z尺度，Snow不再使用仅周期索引的种子；这些是用户要求的表现调整，严格旧粒子A/B不作为此次验收标准。生命周期、形状、折射／叠色由各天气负责。

`TransformChecks.cs` 的23项检查通过，验证三预设与ConfigurePreset默认值、矩形TileCount、Scale、快照隔离、范围钳制／非有限回退、临时OnValidate及Rain→Sand→Snow→Rain连续绑定。原有13项Profile检查继续通过。TileCount范围0.01–256，Scale范围0.001–4；非有限值按当前Kind回退，Resolve不回写来源。

真实管线105项通过，新增9项覆盖每种天气的(7,2)/Scale=0.4、(0.01,256)/Scale=0.001、(256,0.01)/Scale=4，并包含Randomness=0与1。新增用例检查浮点输出有限、实际执行时参数绑定与关键词正确；没有作画面品质或新旧像素一致性的断言。全关输出误差仍为0。C#编译成功，执行三天气变体后Shader诊断为空，运行日志无Error／Exception。退出PlayMode后Rain／关闭／Debug Off状态恢复，SampleScene与PC_Renderer磁盘哈希未变化。

此次逐case记录见 `Validation~/pipeline-transform-results.json`；原来的96项记录仍保留在 `pipeline-results.json`，作为整合阶段历史记录。

## 复现

验证脚本在 `Validation~/`，Unity忽略该目录，不会将其中多个 `Script` 类编入游戏程序集。项目已打开且unityctl连接时，在仓库根目录执行：

```sh
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/ProfileChecks.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/TransformChecks.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/FeatureChecks.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/ShaderChecks.cs
unityctl play enter
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/PipelineChecks.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/GraphBudgetChecks.cs
unityctl script eval 'return UnityEditor.SessionState.GetString("Weather.GraphBudgetChecks", "not armed");'
unityctl play exit
```

FeatureChecks在EditMode可核验立即销毁；PipelineChecks要求PlayMode，使用临时相机／Profile和浮点读回，不保存资产。GPU方程与管线逐case结果保存在 `Validation~/shader-results.json`、`pipeline-results.json`。调优版GPU记录见 `shader-tuned-results.json`，正常帧预算见 `graph-results.json`；旧资源哈希见 `legacy-resource-hashes.json`。

## 验证边界

StandardRequest在该URP版本中关闭RenderGraph debug数据生成，因而其逐case的 `weatherPassCount=-1` 表示未测量，不能当作零Pass。未入队的用例不更新内部材质和Pass输入，记录里的keyword／declaredDepth仅是上一次Prepare的值，不代表本帧使用了这些资源。

GPU毫秒耗时、人工画面品质、硬件动态分辨率与XR设备没有完成测量。Snow正交只验证稳定输出，仍保留旧眼空间雾模型；透明前景遮挡受不透明深度限制。缺颜色／深度的录图保护路径已审查，未在真实管线中强制破坏内部资源。

模块检查点：`7fcc8e4`；管线与集中目录检查点：`4d60584`。旧目录已清理，当前dirty／untracked旧内容原样保全为忽略目录内的测试快照；可用Git回退，不影响共享噪声。工作区其它功能改动未纳入检查点。
