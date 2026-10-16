# Hash Function

路径：`Assets/Mine/Special/HLSL/HashFunction.hlsl`。通用、无状态的数值 Hash；无 CBUFFER、纹理、URP include 或业务全局。根据随机用途与实际输入域选择入口，调用方负责种子编排和输出范围映射。

## 接口契约

数字后缀表示输入维度→输出维度。所有接口使用 float；浮点输入及其中间运算须有限，并处于应用可表达的种子范围。

| 接口 | 输入→输出 | 算法与范围 |
|---|---|---|
| `Hash_Sine21(float2 key)` | 2→1 | dot 系数 `(12.9898,78.233)`，sin 后乘 `43758.5453` 再 frac；`[0,1)` |
| `Hash_Frac22(float2 key)` | 2→2 | 二分量 frac/dot 混合；乘子 `(123.34,456.21)`、偏移 `45.32`；`[0,1)` |
| `Hash_FracGrid22(float2 key)` | 2→2 | 三分量 frac/dot 混合；乘子 `(0.1031,0.1030,0.0973)`、偏移 `33.33`；`[0,1)` |
| `Hash_Sine14(float key)` | 1→4 | 乘子 `(127.1,311.7,74.7,269.5)`，sin 后乘 `43758.5453` 再 frac；`[0,1)` |
| `Hash_UInt2SeedStream01(uint2 key,uint seed,uint stream)` | 整数键/种子/序列→1 | 32 位 uint 混合与回绕，stream 从 0 编号且保留 `stream+1u`；取高 24 位乘 `2^-24`，`[0,1)` |

整数入口仅在 `SHADER_TARGET >= 35` 定义；调用 Shader 沿用原 target，不为浮点 Hash 升级平台能力。整数序列不得换成 signed 或先转 float 混合。

现有入口的常数、float 精度和运算顺序属于算法合同。sin/frac 对浮点误差敏感，不承诺不同 GPU/驱动位级一致。库不自动 clamp、取整或改写种子。

## 输入域与选型依据

`Hash_Frac22` 沿用 Fin / SSO 的算法，用于其现有连续输入；整数格抽样中，横向 lag50 相关约 0.76 / 0.86，不能直接作为像素格白噪声替代。

`Hash_FracGrid22` 沿用 SpecularGI 算法，供单位间隔的格子/像素键及其帧偏移使用。直接输入细密归一化 UV 会产生明显相邻相关性：SSO 1920×1080 UV 域 CPU 抽样约 0.60–0.64，故不能用它直接替换 SSO 的连续域入口。

两个二维入口的保留依据是实际输入域下的随机作用差异。新消费者应检查自己的键尺度、通道及空间/时间相关性；输出区间相同、直方图近似均匀或常数不同，都不能单独决定复用。

## 接入与项目边界

以 Assets 完整路径 include 本库，并在调用或依赖它的私有库之前声明。使用 BlurFunction 时，调用方顺序为 `Core.hlsl → HashFunction.hlsl → BlurFunction.hlsl`；共享库遵守零兄弟 include。

项目或功能族公共流程仍留原目录：屏幕/像素坐标选择、帧号、天气周期与网格 seed、H/V 与轮次编号、生命周期、随机幅度和调色都不属于本库。

## 来源与合并结果

| 来源 | 当前关系与行为 |
|---|---|
| PCSS `Random1D`、SSSM/SSC/SSL ray jitter | 等价转发/调用 Sine21；原坐标、帧输入与 jitter 类型保留 |
| SSL radial `hash21` | 合并到 Sine21；去 banding 的范围、时间种子与作用尺度保留，随机序列改变 |
| Fin `Hash1/Hash2`、SSO `Hash21` | 等价转发 Frac22；Fin 标量取首通道，SSO 保留 `*2-1` |
| Weather `Weather_Random` | 等价转发 Sine14，保留 `index+1`；网格 seed 与周期规则未入库 |
| Blur `Blur_Hash01` | 等价转发整数入口；pixel/seed 转换、层编号与低 target 回退留滤波侧 |
| SpecularGI `SpecularGI_Hash22` | 等价转发 FracGrid22，像素与帧种子表达式保留 |
| RainDrop `Hash2D` | 合并到 FracGrid22；coverage、位置、寿命与列速度保留原范围和编排，具体布局及随机列速度重排 |
| VanGogh `Hash/Hash2` | Sine21 加项目层 `-0.5`，保留 `[-0.5,0.5)`；标量以 `float2(x,0)` 等价适配，二维随机图案重排 |

生成 Shader、隐藏备份与 Weather `Validation~/Legacy` 历史快照未迁移。不同随机效果的合并判断流程见 [unity-feature-reuse](../../../../.agents/skills/unity-feature-reuse/SKILL.md)。

## 验证边界

最终 19 个完整源文件除指定函数迁移、Grid22 新增和 include 外，其余代码 token 保持；13 个现行 Shader/Compute 入口的声明顺序通过，包含私有库的相对路径 include。

实际库经 glslang HLSL → SPIR-V 编译并验证：四个 float API 在 `SHADER_TARGET` 宏 20/35/50 下通过，整数入口在宏 35/50 下通过；宏 20 不暴露整数 API，调用负测按预期失败。

同主机 Clang CPU 桥接保留实源函数体并禁用 FMA：保持原式的接口与旧式比较，获准换序列的接口与所选新标准式比较，共 63,212 次标量逐位比较零差异，54,161 次范围核验通过。两份临时故障副本分别改动 Sine21 / Grid22 常数，均被检出；真实 Assets 未用于故障修改。

代表输入域的 CPU 统计支持 RainDrop、SSL radial、VanGogh 二维的随机作用合同合并：RainDrop 40×36 格的新旧均值、方差、通道相关与 coverage 接近；SSL 屏幕域与 VanGogh 格键的相邻相关均接近零。少量雨滴列的速度均值与次序会变化，不能把有限样本统计或分布相近宣称为旧画面等价。

Unity Editor 当前未运行；本轮未执行 Unity/Metal 编译、GPU 像素对照或性能测量。HLSL 的目标宏检查仅验证预处理分支与语法，同主机 CPU 对照不能推导跨 GPU 确定性或最终画面一致。
