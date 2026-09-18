# Snowy — 屏幕撞击雪斑原型

Shader 为 `PostProcess/Snowy`。Pass 0 为撞击雪斑，Pass 1 为现有气氛。两个 Pass 共用唯一的材质噪声纹理 **Snow Noise / _SnowTex**，读取 R 通道。现有材质已迁移 Editor 中的 Noise2DRG.asset 绑定。

## 撞击周期

保留十层，尺寸倍率 1、2、3 各为 4、4、2 层。各层以十分之一个周期错开：

- cycle=0：重新随机屏幕落点、噪声旋转与尺寸，落点分布在 UV 的 0.05–0.95 内。
- cycle=0–0.5：圆形 SDF 半径从 0 平滑增长到最大值，生命周期 Alpha 为 1。
- cycle=0.5–1：半径保持最大值，Alpha 平滑降至 0。
- 下一周期重新选点；每周期内位置、噪声与朝向固定，不再下落或摆动。

sphere SDF 在屏幕平面上表现为圆：`length(particleUV - 0.5) - radius`。固定最大尺寸的 TRS 负责等比坐标映射，SDF 半径控制生长，噪声 R 通道调制透明度，输出白色雪斑。默认白纹理显示圆斑；噪声纹理显示斑驳轮廓内部。软边使用固定 0.02 局部单位。

Wind 控制周期速度；Randomness 控制尺寸差异，不关闭随机落点。噪声与面片朝向在淡出阶段保持固定，保证只改变 Alpha。尺寸单位为屏幕高度，不使用真实粒子深度。

## 气氛与测试

气氛保留当前精简的霜边、深度雾、纹理风效和调色公式，只将纹理统一为 SnowTex；各气氛采样方式沿用当前版本。粒子噪声使用 LinearRepeat。

DebugOutputFeature 指定 Snowy 材质，开启 Debug，Additional Pass Index=1、Require Depth=true，保存 Renderer 配置。Pass 0 输出粒子，再经 Pass 1 统一处理，因此雪斑仍会受气氛影响。

参数 0/1 端点风险依用户约定留待入口统一钳制，不在计算点逐处增加保护。
