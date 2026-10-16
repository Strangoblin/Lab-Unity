# Blur Function

跨效果 Gaussian 与双边滤波库，路径为 `Assets/Mine/Special/HLSL/BlurFunction.hlsl`。新增接口同时支持普通 2D 与 XR Array 纹理，所有参数由调用方传入；未新增材质 CBUFFER、纹理声明、帧时间或可变全局状态。

调用方应先 include URP `Core.hlsl`，再以 Assets 完整路径依次 include `HashFunction.hlsl` 和本库。Hash 实现见 [HashFunction.md](HashFunction.md)；`Blur_Hash01` 保留签名并转发通用整数映射，像素/seed 转换与层编号仍留在滤波侧。本机 URP 的 `TEXTURE2D_X_PARAM` / `TEXTURE2D_X_ARGS` 在普通模式展开为 Texture2D 与 SamplerState，在 XR 模式展开为 Texture2DArray 与 SamplerState。调用片元需先初始化 `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX`。

## 保留接口

以下函数的签名、采样权重、返回值与关键字语义均保持原样：

- `BlurHorizontal`
- `BlurVertical`
- `BilateralBlurHorizontal`
- `BilateralBlurVertical`

旧函数继续使用原 Texture2D 参数。旧 Gaussian 权重不作归一化，以免改变现有消费者；旧双边函数仍通过深度与可选法线引导。库中既有 `DeclareDepthTexture.hlsl` / `DeclareNormalsTexture.hlsl` 依赖保持不变。

## 新接口

```hlsl
float2 Blur_GetUVStep(float radius, float2 outputSize, int radiusMode);

float4 Blur_Gaussian5(
    float2 uv, float2 uvStep, float2 direction,
    TEXTURE2D_X_PARAM(mainTex, samplerState));

float4 Blur_GaussianJittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed, int sampleCount,
    TEXTURE2D_X_PARAM(mainTex, samplerState));

float4 Blur_Gaussian5Jittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed,
    TEXTURE2D_X_PARAM(mainTex, samplerState));

float4 Blur_Gaussian9Jittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed,
    TEXTURE2D_X_PARAM(mainTex, samplerState));

float4 Blur_Gaussian13Jittered(
    float2 uv, float2 uvStep, float2 direction, float2 pixelCoord,
    float jitterStrength, float seed,
    TEXTURE2D_X_PARAM(mainTex, samplerState));
```

| 参数 | 语义 |
| --- | --- |
| radius | 非负核步长；固定核最远距离为 `2 × radius`；抖动核原始支持域为 `[-2.5, 2.5]`，实际域由各核二阶矩校准 scale 统一换算 |
| outputSize | 当前输出画面的有效宽高，含长宽比；每维至少按 1 处理 |
| radiusMode | 0 = 像素，`uvStep = radius / outputSize`；1 = 屏幕高度比例，`uvStep = radius × (height / width, 1)`；其他值按 0 处理 |
| uv | 源纹理采样 UV |
| uvStep | 基本采样间距，分辨率与单位转换由调用方负责 |
| direction | 水平 `float2(1, 0)` 或垂直 `float2(0, 1)`；调用方也可传其他方向 |
| pixelCoord | 当前输出像素坐标，库内部 floor 并夹到非负，再以二维整数坐标生成固定空间 hash |
| jitterStrength | 距离抖动强度，内部夹到 0–1 |
| seed | 非负整数 pass 种子（接口为 float，内部 floor 后转 uint）；方向和迭代使用不同值，不使用帧时间 |
| sampleCount | 调用方传编译期常量 5 / 9 / 13；其他值按 5 处理。不得绑定材质参数或由 radius 推导 |
| mainTex / samplerState | 由 XR 参数宏显式传入的源纹理及采样器 |

像素单位的尺寸应对应需要表达半径的输出画面，不应默认为 `_ScreenParams`。在降采样 RT 上复用时，调用方应明确半径属于相机画面还是当前工作 RT，并换算至相同 UV 空间。屏幕高度比例模式把 X 方向乘 `height / width`，使两个方向的屏幕像素跨度一致。

`radius = 0` 定义为关闭滤波时，由 Feature 跳过 H/V Pass；各核保持编译期固定采样预算，半径不参与循环预算。

## 固定核与分层抖动

`Blur_Gaussian5` 在距离 `-2, -1, 0, 1, 2` 采样，使用原权重 `0.1216216, 0.2332432, 0.290918, 0.2332432, 0.1216216`，再除以权重和 `1.0006476`。RGBA 使用同一组归一化权重，恒定颜色及遮罩不会被核权重总和放大。

`Blur_Gaussian5Jittered` 将五个样本分别放入以 `-2, -1, 0, 1, 2` 为中心的一维采样层。每层都有自己的 hash 值，中心层也参与抖动：

```
base_i = i - 2
position_i = base_i + (hash(pixelXY, integerSeed, i) - 0.5) × strength
weight_i = oldGaussianWeight_i / 1.0006476
```

强度 1 时，未校准的五层分别覆盖 `[-2.5,-1.5)`、`[-1.5,-0.5)`、`[-0.5,0.5)`、`[0.5,1.5)`、`[1.5,2.5)`，每层均匀取一个样本，并保留固定层质量。支持域、层内采样分布与层权重一致，期望结果是 **分段常量 Gaussian 近似**；不是精确连续 Gaussian，也不再按随机位置重新计算密度及随机归一化分母。颜色和 alpha 使用相同的固定归一化权重，恒定 RGBA 场保持不变。

采样位置仍会使单个像素的核形状和质心变化。中心层不再把样本锁死在原像素，中心权重也不再被随机分母放大或缩小；这可移除先前固定中心贡献与随机层质量变化，但不保证所有源图像的颗粒都消失。

噪声改为固定空间 **uint hash**：混合像素 X、像素 Y、整数 pass seed、tapIndex，经整数 avalanche 混合后取高 24 位映射到 `[0,1)`。它不沿用两组 IGN 的共同线性相位，不使用帧时间、噪声纹理或蓝噪声。各采样层使用不同编号，H/V 和不同迭代仍由调用方给不同整数 seed；固定画面与种子保持固定分布。

层内抖动会增加期望扩散宽度，用一次 `rsqrt` 作统一距离校准：

```
M2 = (2 × 0.2332432 + 8 × 0.1216216) / 1.0006476
   ≈ 1.4385276
scale = rsqrt(1 + strength² / (12 × M2))
sampleUV_i = uv + uvStep × direction × scale × position_i
```

在层内随机数近似均匀的假设下，未校准核关于输出中心的期望二阶矩为 `M2 + strength²/12`，上述缩放将它还原为旧核的 `M2`。校准同时缩放采样层支持域；它保持期望尺度，不保证逐像素核形状或质心一致。双线性插值、纹理离散化和边界夹取仍会影响实际画面。

强度为 0（包括负值夹到 0）时直接调用 `Blur_Gaussian5`，严格退化为固定核。非零分支每方向仍只采样 5 次，额外成本为 5 个整数 hash 和 1 次 `rsqrt`，没有指数、log、inverse CDF、额外纹理或 RT。uint 代码仅在 `SHADER_TARGET >= 35` 编译；低 target 的调用自动返回固定五采样核，旧消费者可保持原目标级别。

这是固定五采样的分布改进，不增加采样预算。半径大、源图像高频或颜色对比强时仍可能有静态颗粒；有限采样不能保证任意半径都无伪影。

## 固定 9 / 13 采样预算

`Blur_Gaussian9Jittered` 与 `Blur_Gaussian13Jittered` 使用相同完整原始支持域 `[-2.5, 2.5]`，分别将它等分成 9 / 13 层；不改变原五采样函数。位置、层宽和固定质量定义为：

```
width = 5 / N
midpoint_i = (i + 0.5) × width - 2.5
position_i = midpoint_i + (hash(pixelXY, integerSeed, i) - 0.5) × strength × width
rawMass_i = exp(-midpoint_i² / (2 × 1.5²))
mass_i = rawMass_i / sum(rawMass)
```

这里采用 `sigma = 1.5` Gaussian 的 **中点层质量近似**，而不是每层精确积分。宽度相同，归一化后 width 因子约掉；质量预计算为代码中的常量数组，不在片元中求指数，也不随随机采样位置变化。每层内均匀采样并用该层固定质量，定义的是相应的分段常量 Gaussian 近似。强度 1 时所有层共同覆盖完整支持域；较低非零强度收缩各层内的采样跨度。

| 核 | 每方向非零抖动采样数 | 层宽 | 固定中点核 M2 |
| --- | --- | --- | --- |
| `Blur_Gaussian9Jittered` | 9 | 5/9 | 1.4258538730 |
| `Blur_Gaussian13Jittered` | 13 | 5/13 | 1.4254231168 |

每个核使用不同的固定质量表，但将期望二阶矩统一匹配到旧五采样核的 `1.43852761`：

```
M2_N = sum(mass_i × midpoint_i²)
jitterMoment = strength² × width² / 12
scale = rsqrt((M2_N + jitterMoment) / 1.43852761)
sampleUV_i = uv + uvStep × direction × scale × position_i
```

校准只需要一次 `rsqrt`。原始支持域统一，实际采样域会随该确定性 scale 略微收缩或扩张；质量随域一同换算，保持近似随机采样模型中的期望扩散尺度。它不会保证每像素质心、插值后的纹理方差或边界夹取行为完全相同。

5 / 9 / 13 现在共用 `Blur_GaussianJittered` 的位置生成、hash 和加权采样循环。局部 27 项权重表依次存放原五点权重、九点质量和十三点质量，起始索引为 0 / 5 / 14；五点仍在累加末尾除以 `1.0006476`，保留原尺度公式的计算次序。循环上限固定为 13 并标记 `[unroll]`，只有 `i < tapCount` 的分支读取纹理，radius 不参与预算。三个原接口只传入字面常量点数，供编译器内联并裁掉其他档的配置及空采样分支；实际 Metal 展开结果仍须编译后核验，源码重构本身不证明 GPU 指令数量。RGBA 共用固定权重，没有额外纹理、RT 或 inverse CDF。

`jitterStrength <= 0` 或 `SHADER_TARGET < 35` 时统一核心及三个固定点数入口都回退到原 `Blur_Gaussian5`，因此回退方向只采样 5 次。Feature 可在强度 0 时直接禁用抖动变体，并按原 Quality 的轮数执行固定核，保持严格的固定核对照；库本身不控制 Feature 档位或迭代数。

增加样本数量能缩小分层区间、减少稀疏采样误差，但不保证任意半径或高频源图像都没有静态颗粒。9 / 13 是不同成本的滤波预算，仍需用真实画面比较收益与代价。

## 统一结构与兼容性

`Blur_Gaussian5Jittered / 9Jittered / 13Jittered` 保留签名，内部都转到同一核心；已有 MaskBlur 调用无需修改。新调用可直接传常量 `sampleCount`，同一 Shader 内能调用不同固定点数，不依赖共享库的全局质量宏。点数选择只在循环前决定层宽、权重表起始位置与尺度补偿，未引入逐点权重分布算法。

本次合并保留各核的权重、hash 编号、层内采样位置、float4 累加次序、零强度固定核回退和低 target 回退。五点与九 / 十三点的尺度公式保留各自计算方式，避免把结构合并变成画面参数调整。旧 Gaussian / Bilateral 四个接口的代码未改动。

2026-10-10：静态结构及 float32 公式等价检查通过。Unity Editor 未运行，本轮尚未重新编译或验证实际 Metal 的展开结果与画面。

## 调用示例

```hlsl
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

float2 uvStep = Blur_GetUVStep(radius, outputSize, radiusMode);
float4 fixedColor = Blur_Gaussian5(
    input.texcoord, uvStep, float2(1, 0),
    TEXTURE2D_X_ARGS(_BlitTexture, sampler_LinearClamp));

float4 jitteredColor = Blur_GaussianJittered(
    input.texcoord, uvStep, float2(0, 1), input.positionCS.xy,
    jitterStrength, verticalSeed, 9,
    TEXTURE2D_X_ARGS(_BlitTexture, sampler_LinearClamp));
```

H/V 应分别使用不同固定种子，多轮滤波再按迭代编号区分。颜色和遮罩 alpha 一起滤波；预乘遮罩颜色的柔光合成直接使用模糊 RGB，不再乘一次 alpha。
