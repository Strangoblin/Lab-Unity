---
name: blur-jittered-unification
description: BlurFunction 的 5 / 9 / 13 分层抖动核统一为固定点数核心，保留原接口与分布
date: 2026-10-10
---

# BlurFunction 抖动核结构统一

用户要求精炼 `Assets/Mine/Special/HLSL/BlurFunction.hlsl`，将最新 5 / 9 / 13 点抖动采样共用函数结构。

- 新增 `Blur_GaussianJittered(..., int sampleCount, TEXTURE2D_X_PARAM(...))`，一个分层位置 / hash / 加权采样循环。调用方须传编译期常量 5、9、13，其他值按 5；不能绑定材质运行时参数或由 Radius 推导。
- 局部 27 项权重表按原五点、九点、十三点顺序拼接，起始索引 0 / 5 / 14。循环固定展开上限 13，`i < tapCount` 包住实际纹理采样，供编译器按常量裁剪无效分支。
- 原 `Blur_Gaussian5Jittered / 9Jittered / 13Jittered` 签名保留，成为字面常量 5 / 9 / 13 的薄封装；MaskBlur 调用及质量配置无需修改。
- 保留五点的原始权重、末尾除以 1.0006476 和原 rsqrt 表达式；九 / 十三点保留各自权重、层宽、M2 与尺度运算次序。零抖动及 target<35 回退到固定五点核，RGBA 同权重，hash 及旧 Gaussian / Bilateral 函数未改。
- 同目录 `BlurFunction.md` 同步统一入口、编译期点数契约与实现说明。源文件从重构前 331 行减至 280 行，不将结构精简宣称为性能提升或采样算法升级。

## 验证与边界

规范检查、`git diff --check` 通过。重构前后旧接口、尺寸换算、hash、固定核所在前缀逐字节相同。CPU float32 对照三档权重、六种非零抖动强度的层内位置及 RGBA 累加，最大差为 0；尺度表达式与运算次序作源码核对。

Editor 与 bridge 当前未运行，尚未执行本轮 Unity / Metal 编译、实际采样指令预算和画面对照。固定 13 展开上限及字面常量接口有利于常量传播，但不能凭源码证明 Metal 已裁到 5 / 9 / 13 次纹理采样；后续需编译并核验实际结果。

本轮只修改共享 HLSL 与同目录说明，未改 MaskBlur Feature / Shader、场景、Renderer 资产。尚未提交。暗点的采样分布与两轮 H/V 结论仍见 [2026-10-09 记录](2026-10-09-maskblur-stratified-darkspots.md)。
