---
name: hash-function-extraction
description: 按算法族提炼通用 HashFunction，迁移等价调用并保留项目种子语义
date: 2026-10-10
---

# 通用 Hash 库提炼

用户要求从可复用函数开始维护项目，并确认写入范围为 `Assets/Mine`：新建共享 Hash 库，迁移经比对可等价替换的调用。遵循本轮补充的边界：项目/功能族公共流程留原目录，只有脱离业务语义的通用算法进入 `Special/HLSL`。

新增 [HashFunction.hlsl](../../../../Assets/Mine/Special/HLSL/HashFunction.hlsl) 与 [接口合同](../../../../Assets/Mine/Special/HLSL/HashFunction.md)，属于 A 档纯函数库：零 CBUFFER、零全局资源、零 include。提供 `Hash_Sine21`、`Hash_Frac22`、`Hash_FracGrid22`、`Hash_Sine14`、`Hash_UInt2SeedStream01`；整数入口保持 `SHADER_TARGET >= 35` 保护。

用户随后补充：不能仅因常数/结构不同保留变体，需根据最终作用方式与作用范围判断，类似随机效果允许合并并改变序列。裁决依据已补入复用 skill，详见 [体系记录](../../meta-developer/memory/2026-10-10-behavioral-reuse-criteria.md)。

## 等价迁移

- SSSM / SSC / SSL 的 ray jitter 和 PCSS `Random1D` 共用 Sine21。
- Fin `Hash1/Hash2` 共用 Frac22，标量接口取首通道；SSO `Hash21` 保留功能层 `*2-1` 映射。
- Weather `Weather_Random` 保留签名与 `index+1`，转发 Sine14；网格 seed、周期、随机幅度与生命周期留 Weather。
- Blur `Blur_Hash01` 保留签名，转发 UInt2SeedStream01；像素/seed 转换、H/V、轮次和 tap 编号、采样核与低 target 回退不变。
- 五个现行 Blur 消费者全部显式先 include Hash：MaskBlur、SSL、DDOF、SSSM、Kuwahara。共享库不 include 兄弟库，调用顺序记录为 Core → Hash → Blur。

上述等价抽取保持原常数、float 类型、运算顺序、输入种子、输出映射及 Shader target。生成 Shader、隐藏备份和 Weather 历史验证快照未迁移。全程使用 Unity 写入门禁，保留任务开始前已有改动，未修改场景或 Renderer 配置。

## 按作用域统一变体

- SSL 径向去 banding 的另一组系数也统一到 Sine21；VanGogh `Hash2` 用 Sine21 并在项目层保留 `-0.5`，`Hash(float)` 以 `float2(x,0)` 适配相同原式。
- 新增 FracGrid22，采用原 SpecularGI 的三分量混合；SpecularGI 等价转发，RainDrop 的网格/列随机统一到此入口。RainDrop 的布局、随机列速度及 SSL / VanGogh 的随机图案会重排，原 key、周期、幅度、范围、效果编排和 target 保持。
- Frac22 继续服务 Fin / SSO。CPU 实测该式在整数格横向 lag50 相关约 0.76 / 0.86；反向将 FracGrid22 直接用于 SSO 归一化 UV，相邻相关约 0.60–0.64。保留两个入口是输入尺度下随机作用不同的证据，不能仅用近似均匀直方图跨域替换。
- RainDrop 40×36 网格的新旧 coverage .85 / .17 / .425 均相近；小样本列的具体速度均值与次序会变化，最终运动观感待画面检查。SSL 屏幕域及 VanGogh 格键的新旧均值、方差和相邻相关性支持随机作用合同合并，不宣称旧像素等价。

## 验证与边界

- 与阶段前工作内容核对，最终 19 个完整源文件除指定函数迁移、Grid22 新增和 include 外，其余代码 token 保持；13 个现行 Shader/Compute 入口的声明顺序通过，包含 RainDrops 与 PCSS.compute 的私有相对路径 include。
- 实际库经 glslang 原生 HLSL → SPIR-V 编译及验证：四个 float API 在 `SHADER_TARGET` 宏 20/35/50 下通过，整数入口在宏 35/50 下通过；宏 20 隐藏整数 API，调用负测按预期失败。此项只验证语法与宏分支，不证明 Unity 实际目标平台能力。
- 同主机 Clang CPU 桥接实源函数体，禁用 FMA；保持原式的接口对照旧式，有意换序列的接口对照所选新标准式。覆盖归一化 UV、Fur seed、雨滴格键/列、SpecularGI 像素/帧、VanGogh 索引、SSL 时间相位和 uint 边界/回绕，共 63,212 次标量逐位比较零差异，54,161 次范围检查通过。Sine21 / Grid22 两份临时常数故障副本均被检出，真实 Assets 未用于故障修改。
- 新库规范检查、HLSL 模板 API 核验和 `git diff --check` 通过。

Unity Editor 与 bridge 未运行，本轮未执行 Unity / Metal 编译、GPU 像素对照或性能测量。同主机 CPU 浮点相等不能推导不同 GPU 的正弦 Hash 位级一致。代码尚未提交。

后续添加 Hash 变体先关联此库并比较最终作用与实际输入域，不以常数或结构不同作为保留理由，也不以输出范围相同作为合并证据。复用流程见 [unity-feature-reuse](../../../skills/unity-feature-reuse/SKILL.md)，归属判据见 [公共函数归属表](../templates/shader/hlsl/README.md#公共函数归属)。
