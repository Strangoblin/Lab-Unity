# Snowy — 屏幕雪斑与雪天气氛原型

Shader：`PostProcess/Snowy`。所有效果逻辑保留在 Snowy.shader，面片变换复用共享 TRS.hlsl。

## 文件与依赖

- Snowy.shader：两个全屏 Pass。
- Mine_PostProcess_Snowy.mat：示例材质，SnowTex 绑定 Assets/Textures/Noise2DRG.asset。
- README.md：当前实现与测试入口。

## Pass 0：屏幕雪斑

每周期随机位置、角度、尺寸；周期内位置不变。时间为 `_Time.y * _Wind * 2`，Randomness 同时影响落点范围、角度与尺寸。

TRS 将屏幕 UV 映射到局部等比空间，SnowTex.r 扰动局部 UV，再计算径向 SDF；Frost 控制雪斑范围与透明度，cycle 驱动淡出。当前版本不再使用前半周期半径生长或深度分层。

合成走闭式而非循环：`lerp(c, 1, p)` 复合 n 次等于 `c·qⁿ + (1 - qⁿ)`（q = 1 - p），n = 10 由 `SnowyComposite10` 展开为乘法链。十次仍作用于同一雪斑——`SnowyParticle` 没有索引参数，本来就不是十个独立落点——所以闭式与原循环逐像素等价，但少掉九次 `_SnowTex` 采样与九次逆 TRS。要真做出独立落点，得给 `SnowyParticle` 加索引参数，那是另一个效果。

## Pass 1：雪天气氛

1. 屏幕：SnowTex 在 7 倍与 5 倍 UV 下采样，生成静态不规则霜边。
2. 场景：第三次采样沿 X 方向滚动，调制随场景深度增加的雾。
3. 调色：降低饱和度并偏蓝灰。

两个 Pass 共享唯一噪声纹理 SnowTex，使用 Repeat 采样。气氛当前对 float4 进行混合，因此会改变 Alpha；保持当前原型实现。

## 参数

| 参数 | 默认值 | 作用 |
|---|---|---|
| Snow Tex | white | 共享噪声，读取 R 通道 |
| Randomness | 0.5 | 粒子位置、角度与尺寸变化幅度 |
| Coldness | 0.3 | 冷调色混合强度 |
| Frost | 0.5 | 雪斑、霜边与场景雾强度 |
| Wind | 0.5 | 粒子周期、雪幕滚动与雾距离变化 |

## 测试入口

在 PC_Renderer 的 DebugOutputFeature 中绑定示例材质，开启 Active、Debug，设置 Additional Pass Index=1、Require Depth=true，并保存 Renderer 配置。-1 只执行粒子 Pass。

当前深度解算使用透视相机路径，不包含真实粒子深度、遮挡或世界空间视差。参数端点由后续统一入口钳制处理，遵循项目参数范围约定；此次未追加逐点保护。
