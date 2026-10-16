# Sandy — 屏幕沙粒与干燥天气

Shader：`PostProcess/Sandy`，两个全屏 Pass。由 Sandstorm 更名，保留资源 GUID 和现有 Renderer 引用。

## Pass 0：单层沙粒

SandyParticle 使用固定 40×24 网格、周期随机 TRS 与一次 SandTex.r 采样。落点在周期内固定，只淡入淡出；Wind 只控制击打更新频率。

径向遮罩与纹理噪声相乘得到粒子强度，直接混入赭色。不再叠加第二层粒子或执行十次复合增强。合成时，中央强度为 8%，向四角平滑增强至 100%。粒子尺寸与最近版本的细沙设置一致。

## Pass 1：积尘与气氛

一次 SandTex.rg 采样：R 调制静态边缘积尘，G 调制暖色高度雾。边缘采用四次幂圆角遮罩。没有额外高频积尘层或背景模糊。

一次深度世界坐标重建，以视线中点的高度密度近似整段扬尘雾。支持透视与正交相机，路程上限 250 世界单位，世界 Y=0 为高度基准。最后用与 Snowy 相同的亮度混合结构完成暖调色，保留输入 Alpha。

## 参数

| 属性 | 默认值 | 说明 |
|---|---:|---|
| SandTex | white | 沙粒与气氛共享 RG 噪声；示例材质绑定 Noise2DRG |
| Randomness | 0.5 | 击打落点、角度、尺寸的随机幅度 |
| Warmth | 0.3 | 暖调色强度 |
| Dusty | 0.5 | 沙粒、积尘与高度雾强度 |
| Wind | 0.5 | 击打更新速度；为 0 时冻结周期 |

示例材质：`Mine_PostProcess_Sandy.mat`，保留 Randomness=0.8、Warmth=0.65、Dusty=0.65、Wind=0.5。

## 使用与限制

复用 DebugOutputFeature，绑定示例材质，Require Depth 开启，Additional Pass Index=1。-1 仅执行沙粒 Pass。

屏幕沙粒无世界空间视差或独立几何遮挡。高度雾依赖场景比例和不透明深度；中点高度近似在跨越大高度范围时会偏离积分结果。视觉强度需在目标场景确认。
