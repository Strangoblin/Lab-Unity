# SSC 固定自适应高度，保留原 DensityH（2026-10-23）

用户要求自适应高度固定0.5，随后暂停平方衰减改动，询问原thickness=0.5是否为半球形。当前只保留固定高度改动，不实施新的平方/四次方剖面。

- `SSC.shader` 将局部云顶的lerp强度写为常量0.5，删除 `_AdaptiveHeight` 的 Properties/CBUFFER，简化对应分支；`SSCFeature.cs` 删除 adaptiveHeight 字段、PropertyToID 与 SetFloat，面板不再暴露该项。此前通过DensityLOD的out参数复用low驱动局部高度的方式保留，不增加纹理采样。
- `DensityH` 与本轮开始前原函数逐字一致：线性tent权重 `r` 经 `pow(r,max(1e-5,1-thickness))` 输出。thickness=0.5时为sqrt(r)。令d为距峰位的归一化距离，则r=1-d，当前曲线sqrt(1-d)，严格半圆/半球截面的轮廓应为sqrt(1-d²)；两者不能等同。d=0.5时分别约0.707和0.866。
- 本轮曾生成的平方衰减实现已撤回。保留用户已调整的 Circle `boundaryFade=0.5`，不改其他噪声/阈值/颜色/积分或场景配置。
- 规范检查、git diff --check及Metal两Pass×四关键字共8组合通过，Shader消息0，确认材质不再有_AdaptiveHeight属性。未为保留不变的DensityH另做GPU曲线测试；当前改动仍待人工视觉观察。
- 本记录覆盖 [此前可调自适应高度](2026-10-23-ssc-adaptive-height.md) 的参数暴露约定；整体密度重构仍维持撤回裁决。
