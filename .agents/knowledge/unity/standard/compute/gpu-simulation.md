# GPU 模拟的时序与稳定性

> 用 Compute Shader 驱动状态场（水波、雪地、粒子高度场）时反复踩到的同一组坑。
> 实例系统：`Assets/Mine/Scripts/InteractionManager/`（Verlet 波方程）。

---

## 状态双缓冲：在 kernel 内双写，不要 CopyTexture

Verlet 需要 `h_curr` 与 `h_prev` 两份。写成「dispatch 前 `CopyTexture(curr → prev)`」会让
`prev == curr`，动量项恒为零 —— 波不会传播，且看不出是错的。

正确做法是 kernel 内同时写两张 RT：`outTex = h_new`、`outPTex = h_curr`。

## RT 时序：compute 读到的是谁写的

`ClearRT` 放在 `Update()` 开头、compute 也在 `Update()` 里跑时，相机的交互输入本帧尚未渲染，
compute 读到的是全零。把 `Process()` 排到 `ClearRT` 之前，让 compute 消费**上一帧**的相机输出。

判读：状态场恒为零要先查写入顺序，不要先怀疑算法。

## 时间步暴走：CFL 条件与 dt 无关化

物理波速 `c` 随 `dt` 变化会破坏 CFL 条件（`c·dt` 越过格子尺度），表现为格子纹。
把系数归一到固定基准（像素空间系数 + 归一化到 `areaSize = 10`），让稳定域不再依赖帧率。

## 边界：硬截断会积能

RT 边缘直接丢弃波能量 → 边缘堆积 → 拖尾。用 `smoothstep` 淡出边界带
（8% 边缘 ≈ 20 px）把能量耗散掉。

## 移动域重投影的符号

世界位移 → 像素位移的映射方向容易写反：物体沿 +X 移动时，**旧数据留在 +X 侧的像素**，
所以 `deltaPixels = -worldDelta * scale`（取负）。符号写错的症状是拖尾朝反方向偏。

## 正交相机的深度解码

正交相机 `near < 0` 时，经 GPU 矩阵后的深度是正向编码，`LinearEyeDepth` 按反向 Z 解码会得到全错的值。
直接比较**原始 depth buffer 值**，再乘 `(far - near)` 恢复世界单位。

## 正交矩阵：同步给相机再回读

手动构造 V/P 矩阵却不赋给相机，会让 shader 采样偏移。正解是把 transform 与 `orthoSize`
同步到相机，再从相机回读实际矩阵 —— 相机是唯一真源，不要在 CPU 侧维护第二份。
