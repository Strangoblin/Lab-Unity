# InteractionManager 待办

## 斜坡交互没有修正（2026-11-09 记录）

**状态**：未处理。当前代码按「水平面」假设运行，坡度越大偏差越大。

### 结论

整条链路把「曲面上的真实量」投影进世界 XZ 网格，再一路按平面语义处理，出口没有做逆变换。
丢掉的是两个量：坡度因子 `n·up` 和坡面切空间基。

注意区分：**参照面深度本身不是平的**。雪 / 沙的 `DepthOnly` Pass 输出未施加压痕位移的真实
网格深度（`Snow/SnowGround.shader` 的 `DepthOnly`），再由
`../Special/Renderer/TransparentDepth/TransparentDepthMergeFeature.cs` 合进 `_CameraDepthTexture`。
所以深度是斜的；被压平的是**坐标系**，不是深度。

### 现状的五处平面假设

| # | 位置 | 假设 |
|---|---|---|
| ① | `InteractionManager.cs` `SyncMatrices()` | 交互相机 `LookAt(camPos, center, Vector3.forward)`，正下正交投影，RT 的 UV 直接当世界 XZ |
| ② | `Shaders/InteractorObject.shader` `CaptureFrag` | `penetration = fragEye - sceneEye` 是**沿相机轴（竖直）**的穿透，不是沿法线的深度 |
| ③ | `InteractionProcessor.cs` `WorldDeltaToPixels()` | `scale = resolution / areaSize`，只用 world XZ，各向同性，没有 Jacobian |
| ④ | `Snow/SnowGround.shader`、`Sand/SandGround.shader`、`Shaders/InteractionField.hlsl` `ApplyInteractionSlope()` | 无视 mesh normal，直接 `normalize(float3(-slope.x, 1, -slope.y))` |
| ⑤ | `Shaders/InteractionParallax.hlsl` | 文件头已写明「表面是平整的水平面，TBN 就是世界的 XZ」，沿 `float3(-V.x, 0, -V.z)` 步进后与 `t·vY` 求交 |

另外两处同源的输出侧：

- `Grass/GrassInteraction.hlsl` 的 `GrassBendOffsetWS()` 返回世界 XZ 倾倒 + `(0,-1,0)` 下压。
  草本身的静态朝向是对的（`GrassManager/Grass/GrassWind.shader` 用 `grassProp.normal` 建 TBN），
  所以是「站得对，倒得歪」。
- `../Shaders/Render/Water/WaterFunction.hlsl` 的 `ComputeInteractionWave()` 沿世界 Y 位移。
  水本身就是水平面，对水不算错；坡上水面被地形切穿是另一个问题。

### 误差量级

| 坡度 | `1/cosθ` | 表现 |
|---:|---:|---|
| 20° | 1.06 | 基本看不出 |
| 30° | 1.15 | 幅度偏大、足迹偏长，可察觉 |
| 45° | 1.41 | 幅度与足迹明显失真，法线朝向错误 |

具体分项：

1. **穿透幅度**被放大 `1/cosθ`：同样物体压在坡上读数偏大。物体背面轮廓在竖直投影下与坡面错位，
   测边缘时还会产生「拖影」式穿透。
2. **足迹沿坡向拉伸** `1/cosθ`：当前 12 m / 256² = 4.69 cm 一纹素是**水平**尺度，斜面上真实面长
   是 `4.69/cosθ`；12 m 的交互域在坡向只覆盖 `12·cosθ` 的实际表面（45° 时 8.5 m）。
3. **位移换算无 Jacobian**：同一物理位移在坡上被当成水平位移，恒小 `cosθ`；加上 `SnappedCenter`
   按 XZ 纹素吸附，坡上纹素格与曲面不对齐，还会有闪烁。
4. **法线重建强行 +Y**：得到的是「水平面的法线 + 扰动」，不是「坡面法线 + 扰动」，
   45° 时明暗方向会直接出错。
5. **视差步进按水平平面求交**：坡上系统性过冲 / 欠冲，还会吃掉原本「纵向 5–7 个纹素」的收敛余量。

### 阻塞点

`Assets/Settings/IA_Renderer.asset` 现在是 `copyNormal: 0`、`normalLayerMask` 为空，
**渲染管线里拿不到参照面法线**，① ② ④ 想修都没有 `n·up` 可用。

### 修法分层

**L1 · 消费端加坡度修正（推荐先做）**
提供一张与交互域同分辨率的坡度 / 坡向数据（`n·up` 或直接法线），在三处各采样一次：

1. 穿透幅度乘 `n·up`，把竖直读数换回法向深度；
2. `WorldDeltaToPixels` 乘逆 Jacobian（坡向 × `1/cosθ`，横坡 × 1）；
3. 法线合成挪进切空间：`normalize(N + T·(-slopeU) + B·(-slopeV))`。

能治好 ②③④，其中 ④ 视觉上最明显。足迹拉伸在 <25° 时不可见，可以后置。

**L2 · RT 换成曲面参数化**
捕获相机仍正下，但语义从「世界 XZ」改成「沿法线投影到地形」，每纹素求交。
准确但要重写 `InteractionWorldToUV` 和所有 compute 的坐标语义，代价大。

**L3 · 交互场降级为派生量**
不再把 RT 当唯一真源，改在消费端坡面局部重采样。相当于拆掉现有架构，不建议。

### 与 TerrainManager 的接口

`TerrainManager` 已经是地形数据的唯一权威，`TerrainGrid.TrySample()` 直接返回 `Normal`，
CPU 侧免费；GPU 侧只要把法线的 XZ 分量烘成一张与交互域同分辨率的 RT 上传即可。

这比开 `copyNormal` 更划算：不用改 Renderer 资产、不用多一次全屏法线拷贝，而且数据层是唯一真源，
草、交互、渲染的坡度天然一致。

### 一个便宜的降级判据

坡度低于约 20° 时所有误差都在 6% 以内，玩家看不出来。可以设一个坡度阈值：
低于阈值的区域走现有代码路径，超过才切到修正分支。这样大部分地形不受改动影响，
需要回归验证的面被压到很小。

### 验收方向

- 平地（0°）行为与现在逐像素一致 —— 修正必须是「只在坡上生效」。
- 同一物体以相同相对姿态压在 0° / 30° / 45° 坡上，交互印记的法向深度、足迹长度一致。
- 坡面法线朝向正确：压痕的明暗随坡面倾斜，而不是始终朝上。
