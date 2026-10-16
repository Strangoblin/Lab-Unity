---
name: ssgi-shared-spatial-reconstruction
description: Diffuse向Specular宽核对齐，共享5x5家族重建核心，减少一张RT/一遍Pass；避免2texel奇偶子网格，Diffuse最终步幅1.5/1.5/1，49组及三个负控制通过，画质待确认。
date: 2026-10-13
---

# SSGI 共享空间重建

用户批准沿 [弱时缓诊断](2026-10-13-diffuse-weak-temporal-diagnosis.md) 的方向向 Specular 靠近。Production，沿用已授权SSGI工作区，所有Assets写入走门禁。Git基准f9b3ad8，已有脏改动保留；未提交/推送，未保存场景/Renderer。

## 实现

- 新家族私有 `SSGISpatialFilter.hlsl` 提炼 Specular 5x5 循环：固定25tap、重尾空间权重、眼深尺度0.5、法线幂32。不是新的Special通用库。
- Diffuse由H/V两遍改为单遍SpatialResolve：Trace(0)→SpatialResolve(1)→Resolve(2)→Composite(3)，少一张低分辨率RT和一次Pass。实际采样从10tap变25tap，不据此宣称更快。
- Diffuse Low/Medium步幅最终1.5，High1；初版照用Specular的2产生奇偶子网格/格状残留，改用非整数步幅及线性输入采样，并补fractional-parity参考控制。Specular仍为2/2/1。
- Diffuse保留法线有效性/归一化、加权confidence；Specular保留原始法线权重、中心fallback及中心alpha。Diffuse严格四点上采样仍使用原depthSigma（默认0.15），该字段现仅管上采样，未以隐藏最小值/倍率改写它。
- 半球方向、全射线平均/黑miss、步进及射线预算、共享时域、BRDF/环境/合成未改。文档见功能SSGI/DiffuseGI/SpecularGI说明。

## 验证与结论

Unity6000.3.14f1 / URP17.3 / M2 Metal：最终 **49组 / 1,152,535断言**，距离、错误历史、空间r+0.25三个负控制都识别。新增独立CPU/GPU常量、脉冲、深度/法线断层、无效法线、天空、边界、半径/归一化、fallback及非整数奇偶输入参考；fractional-parity RGBA最大差1.78813934e-7。三档统一/独立信号差异0；Specular提炼前三档最终RGB回归差异0。

旧BaselineDiffuse消费者原本include活的Diffuse helper，冻结的是RAY几何而非整套滤波，故同步四Pass wrapper并通过同帧声明读依赖的TraceExportPass比较raw Trace。没有放宽旧容差或恢复死Blur函数。Specular另以Git HEAD的干净提炼前滤波另存 `Validation~/BaselineSpecularFilter.hlsl`，避免两边引用同一新helper自证等价。主runner临时fixture/metas已清理。

初版步幅2在原固定SceneView、同尺寸/报告pose/表面像素数/内部frame32..47/shader time下，时缓0.5帧差RMS：DDA0.002636304→0.002232678（约15.3%），平均亮度仅变-0.052%；但该候选有格状残留，**不是最终版本百分比**。最终步幅1.5采集时用户已改镜头、AO强度和原时缓为0.5，视角与时间均变，不能把新RMS0.001957318与旧镜头直接相除。每轮克隆校验保留当时原设置，不回写用户调整。

两次初版整次渲染同步计时有明显跨块/跨轮漂移，配置也不同于旧历史；它们不证明最终版本GPU收益或成本回归。不做更多盲目调参。固定640x360隔离空间图已打开，最终候选仍需用户在真实静止/运动镜头、时缓0.5下确认颗粒、格状残留、边缘漏光/拖影；未宣称噪声完全消除。

OpenCode opencode-go/deepseek-v4.1-flash一次独立验证草稿完成并由主agent复核：纠正inverseSigma0.5→2、增强非恒色法线/天空/无效输入，补fallback与非整数参考。草稿已合入功能Validation~；未让worker写Assets或运行Unity。验证前两次分别发现旧基线wrapper调用死Blur、TraceExport复用时误销毁外部Material，均只修验证装置，未作为成功证据。

八个保护文件（场景/Renderer/通用步进/SSGIFeature/共享时域/Specular采样）SHA1保持；原Renderer/Feature引用及Active/设置/dirty由runner校验，保留用户当前时缓0.5及视角。Editor/bridge留运行、Edit Mode。E5 review提案未改policy，隔离recipe/knowledge/deletion套件通过，真实state/audit指纹未变。

必要报告、控制与精选图归 [证据目录](evidence/2026-10-13-ssgi-shared-spatial)，功能验证源码是权威入口。任务 `tmp/sessions/2026-10-13-diffuse-spatial-r1/` 保留计划、诊断驱动和日志；已应用staging及worker重复源码可清理，不处理其它会话输入。
