---
name: diffuse-multiscale-reconstruction
description: Diffuse两级步幅1→2复用共享空间核心；66组与四个负控制通过，固定当前视角弱时缓0.5静态帧差RMS降22.6%，采样/时域/Specular不改，运动与画质待确认。
date: 2026-10-14
---

# DiffuseGI 两级空间重建

用户反馈上一轮向 Specular 对齐有效但不明显，确认优先推进多尺度重建；采样序列/方差引导另议。本阶段跨日续接，仍沿用 `tmp/sessions/2026-10-13-diffuse-multiscale-r1/`。生产模式，已批准 SSGI 工作区，Assets 写入全部经草稿 apply_patch → gate_cli，未 reset/批准扩展/修改门禁策略。Git 基准及收尾 HEAD 均 f9b3ad8；未提交/推送或回退既有脏改动。

## 实现边界

- `DiffuseGIFeature` 编排 Trace → SpatialResolve.Fine（步幅1）→ SpatialResolve.Coarse（步幅2，读Fine）→ Resolve → 原时域/合成。三档均为两个固定25tap阶段，沿用原 RGBA16F 低分辨率；多一张临时 RT、一次 Raster Pass，四个 Shader Pass 的索引保持0/1/2/3。
- 原 `DiffuseGI_SpatialResolve` 与 `SSGISpatial_Resolve` 均不改；两级都按原相机几何引导，不做 mip 金字塔，非完整方差引导 À-Trous。参数经 PassData 快照绑定，不新增公开旋钮。采样签名w=2，防止沿用单遍历史（High旧步幅也为1，不能只靠半径区别）。
- 不改半球覆盖、采样序列/射线数、RAY/DDA步进与预算、估计量/黑miss、depthSigma严格上采样、历史权重、BRDF/环境或合成；Specular及共享核心不动。相较上一版成本是空间25→50tap，未测 GPU 时延，不宣称提速。
- 冻结 `Validation~/BaselineDiffuseGIFeature.cs` 是预编辑单遍编排/步幅的必要输入，不是整个旧Shader快照；两支刻意共享当前 gather/filter helper，以隔离编排变化。

## 验证

- Unity C# 编译成功；检查仍有其它原存量警告，不声称全项目零消息。最终 **66组 / 1,152,803断言**通过：原真实相机追踪、消费者、AO隔离、逐相机/通道历史，加两级独立CPU/GPU空间参考与观测不变量。
- 两级CPU参考按1→2组合；GPU分两次捕获、上传第一阶段float回读后再滤第二阶段，全RGBA最大差 `9.536743e-7`。覆盖HDR常量、脉冲支持扩大、奇偶混合、两层深度/法线断层、无效guide/天空、边界、零alpha仍贡献RGB。该探针是RGBAFloat回读路径，不是生产RGBA16F误差上界，也不直接逐边检查生产RenderGraph。
- Specular冻结滤波三档RGB最大差0；统一/独立入口信号一致。旧单遍签名被拒绝，无多余历史重分配。
- 距离+0.5、错误后端历史被故意接受、空间R+0.25、遗漏Fine四个GPU/历史负控制均被识别；最后一项checkerboard最大差1.70879745。比较报告的正控及错误RMS、Shader时间、mask三种离线负控制也通过。完整入口见 [Validation README](../../../../Assets/Mine/Shaders/PostProcess/SSGI/Validation~/README.md)。

## 同条件静态 A/B

`run.sh --compare` 临时导入冻结Feature，使用当前SceneView复制姿态 `(22.3678036, 5.60394955, 7.93991661)`、640×360、Medium/DDA，临时Diffuse-only Renderer，时缓0.5、白接收反照率/无距离衰减。两支预热32、采集射线帧32–47，实际6条射线/48步、Shader时间固定、historyValid全1、逐像素表面mask一致（196,302px）。回读生产持久history色/深度，不用跨帧RenderGraph全局或透明覆盖后的显示截图。

| 指标 | 冻结单遍 | 两级 |
|---|---|---|
| 帧差亮度RMS | 0.003400778069968139 | 0.002633428176819793 |
| 表面平均亮度 | 0.008771840464822895 | 0.008797195330431143 |

RMS下降22.56395%，平均亮度+0.28905%。加严格mask检查后复跑得到相同数字。两个640×360完整PNG已打开：它们显示GI空间信号，不是最终Game View截图；新核扩大平滑范围。此数字不与昨日不同镜头比较，不代表全场景方差/GPU性能/运动稳定性或画质已被用户确认。下一步由用户观察弱时缓静止闪烁、移动拖影、真实照明细节与边缘串色；方差图/亮度权重/采样序列未实施。

## 状态与产物

原Scene/Renderer、视角与参数没有程序化保存或回写。跨日暂停时PC_Renderer磁盘SHA1发生外部保存变化，保留续接当前值、不还原昨日快照；其余14项保护源码/Scene哈希一致。每次Renderer克隆检查当前Feature引用/Active/设置与dirty，结束保持Edit Mode。Runner自建Shader/C# fixtures及meta已经门禁清理。

OpenCode flash第一次调用未产出草稿，重试产出空间验收片段与NOTES；主会话强化Coarse边缘不变量和负控制诊断前缀并通过GPU验收。worker未写Assets/未运行Unity。验证装置修正：旧1.5步幅断言同步；A/B将Unity向量序列化为分量数组，避开normalized循环属性；桥接断开时串联宿主启动与整轮验证；一个并行runner拒绝覆盖已存在fixtures，没有覆盖。以上不算生产回归失败或成功证据。

权威功能与验证源码在SSGI目录；精选最终报告、四个控制、比较报告/PNG、保护哈希、编译/状态证据见 [证据目录](evidence/2026-10-14-diffuse-multiscale/README.md)。临时计划/worker备注/日志在原会话目录；已应用草稿与report-control复制输入可重建，不作为第二套权威源码。
