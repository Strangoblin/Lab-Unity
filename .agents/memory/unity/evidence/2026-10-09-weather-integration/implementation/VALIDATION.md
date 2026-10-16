# Weather 验证记录

实施与验证：2026-10-08～2026-10-09。环境：Unity 6000.3.14f1、URP 17.3.0、macOS Metal / Apple M2。全部 Weather 自有内容位于本目录；C# 脚本迁移保留 GUID，旧天气与共享噪声未迁移或改写。

## 已验证

| 验证 | 结果与范围 |
|---|---|
| 导入／编译 | C#与Shader编译成功，Weather Shader supported，两个命名Pass；迁移后再次编译成功 |
| Profile契约 | 13项通过：真实迁移值、全关、周期冻结、白Tint调色、非法值纯解析、雪雾与霜边端点 |
| Feature契约 | 29项通过：材质复用／更换／释放、一次诊断、12组Depth／Debug需求、值快照、相机与资源筛选；EditMode确认原生材质销毁 |
| 兼容基线GPU | 100项通过：18组A/B、48种模块组合、31个端点及3个控制；A/B固定两个纵横比、三个时间，373248个像素，RGBA均有限 |
| 当前真实管线 | 96项通过、103次StandardRequest；48种组合、Debug、透视／正交、比例、renderScale=1／0.75、预设切换、第二Base及真实Base＋Overlay跳过 |
| 全关 | 真实管线与禁用Feature同帧输出误差为0；正常路径入队前筛除 |
| RT几何 | 320×180目标、renderScale=0.75时，实际绑定240×135；不同纵横比的输出均有限 |
| 隔离 | 测试结束恢复Profile、Debug、Feature启用状态、renderScale及全局值；临时对象释放；运行日志没有Error／Exception |
| 旧资源 | 20个旧天气Shader／Material／README／meta与共享Noise2DRG基线哈希均未变化 |

GPU A/B容差为 `abs(new-ref) <= 0.001*(1+abs(ref))`。最大绝对误差0.000619531，最大归一化误差0.000451983。Rain参考Shader仅在内存中取消旧blur合成，Sand／Snow使用原Shader；Tone关闭与正霜边控制均检测到实际差异，避免空输出通过。

## 基线与当前调参

兼容基线对应模块检查点 `7fcc8e4`。2026-10-09工作区已有粒子视觉调整：Rain网格4.5×4.5，Sand网格3×3与尺寸0.2，Snow采用1.5×1.5分区与新的位移。保留这些改动，未将其纳入本次提交。

上表100项GPU结果对应调优前的兼容基线；当前调参版本通过的是96项真实管线检查。`ShaderChecks.cs`仍用于严格检查旧算法兼容性，调优后运行会报告粒子响应差异。当前Renderer选择Rain并关闭Weather，测试保留这一状态。

## 复现

验证脚本在 `Validation~/`，Unity忽略该目录，不会将其中多个 `Script` 类编入游戏程序集。项目已打开且unityctl连接时，在仓库根目录执行：

```sh
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/ProfileChecks.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/FeatureChecks.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/ShaderChecks.cs
unityctl play enter
unityctl script execute Assets/Mine/Shaders/PostProcess/Weather/Validation~/PipelineChecks.cs
unityctl play exit
```

FeatureChecks在EditMode可核验立即销毁；PipelineChecks要求PlayMode，使用临时相机／Profile和浮点读回，不保存资产。GPU方程与管线逐case结果保存在 `Validation~/shader-results.json`、`pipeline-results.json`；旧资源哈希见 `legacy-resource-hashes.json`。

## 验证边界

StandardRequest在该URP版本中关闭RenderGraph debug数据生成，因而其逐case的 `weatherPassCount=-1` 表示未测量，不能当作零Pass。未入队的用例不更新内部材质和Pass输入，记录里的keyword／declaredDepth仅是上一次Prepare的值，不代表本帧使用了这些资源。

GPU毫秒耗时、人工画面品质、硬件动态分辨率与XR设备没有完成测量。Snow正交只验证稳定输出，仍保留旧眼空间雾模型；透明前景遮挡受不透明深度限制。缺颜色／深度的录图保护路径已审查，未在真实管线中强制破坏内部资源。

旧目录保留，未执行归档删除。回退检查点仅包含Weather新增实现与定向Renderer接入，不包含其它工作区改动。
