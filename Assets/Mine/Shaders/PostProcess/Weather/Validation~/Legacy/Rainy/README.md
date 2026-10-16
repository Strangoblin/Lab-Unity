# Rainy — 屏幕雨滴与雨天气氛

Shader：`PostProcess/Rainy`，两个全屏 Pass。保留 RainTex、Randomness、Coldness、Foggy、Wind 五个属性。

## Pass 0：雨滴折射

沿用确认过的 4×4 网格、周期随机 TRS、纹理扰动 SDF 和中心乘权。每滴在局部高度场中心与四邻采样，通过差分坡度产生 UV 折射，不叠加第二层雨滴。

高度差分所需的 RainyDepth、RainyParticle 与闭式十次合成保留；此部分与本次精简前一致。

## Pass 1：水雾与气氛

一次 RainTex.rg 采样：R 调制静态镜头水汽，G 调制高度雾密度。边缘以四次幂圆角轮廓柔和增强，中心无水汽覆盖。

RainyBlur 保留五点十字核：中心 0.4、四邻各 0.15，半径 2–5 像素。中心遮罩为零时跳过模糊，蓝灰罩与场景雾合并为一次颜色混合。

高度雾只重建一次世界坐标，以视线中点的高度密度近似整段视线。支持透视与正交相机，路程最多 250 世界单位，密度从世界 Y=0 向上指数衰减。省去近裁剪面重建和三点密度积分，远景与天空连续参与。沿用 Snowy 冷调色公式和输入 Alpha。

## 参数

| 属性 | 默认值 | 说明 |
|---|---:|---|
| RainTex | white | 雨滴与气氛共享 RG 噪声；材质绑定 Noise2DRG |
| Randomness | 0.5 | 粒子位置、角度、尺寸的随机幅度 |
| Coldness | 0.3 | 冷调色强度 |
| Foggy | 0.5 | 雨滴、水汽和高度雾强度 |
| Wind | 0.5 | 粒子更新周期；气氛噪声固定 |

## 使用与限制

复用 DebugOutputFeature，绑定 Mine_PostProcess_Rainy.mat，开启 Require Depth，Additional Pass Index=1。-1 仅执行 Pass 0。

中点高度近似在跨越较大高度范围的视线上与积分结果存在差异。高度雾依赖场景比例及不透明深度；视觉效果和 GPU 成本需在目标场景确认。
