# Water — FFT 水面材质

**路径：** `Assets/Mine/Shaders/Render/Water/Water.shader`  
**私有函数库：** `Assets/Mine/Shaders/Render/Water/WaterFunction.hlsl`  
**FFT 生成模块：** `Assets/Mine/Shaders/Render/Water/FFT/`  
**类型：** Transparent / Tessellation / UniversalForward  
**目标管线：** Unity 6 + URP 17+

---

## 功能概述

Water 是一个基于三层 FFT 频谱的透明水面材质。几何阶段读取 displacement 纹理完成顶点位移，片元阶段混合法线、透明场景色、PBR 水面光照、泡沫与单点反推焦散。

当前重构仅调整文件职责，没有改变 Shader 名称、材质参数、采样权重或渲染公式。

## 文件职责

```text
FFTWaveOrchestrator + FFT.compute
    └── 生成三层 displacement / normal 纹理
            │
            ▼
Water.shader
    ├── Properties / CBUFFER
    ├── 纹理与管线结构体
    ├── Vert / Hull / Domain
    ├── Frag 与 UniversalForward Pass
    └── include WaterFunction.hlsl
            ├── FFT 波浪、法线、泡沫采样
            ├── PBR 水面光照
            ├── 边缘泡沫与波峰泡沫
            ├── 水面残差焦散
            └── 屏幕折射坐标辅助
```

## 渲染流程

```text
Object Space 顶点
    → Hull Tessellation
    → Domain 中采样 FFT displacement
    → 世界空间水面位置
    → FFT normal 混合
    → 扭曲后的屏幕坐标
    → Scene Depth / Normal / Opaque Color
    → PBR 水面 + 透明场景色
    → 泡沫 + 焦散
    → Transparent 输出
```

## 参数说明

### Opaque

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_baseColorA` | Color | 白色 | 浅水基础色 |
| `_baseColorB` | Color | 白色 | 深水基础色 |

### FFT Wave

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_DisplacementScale` | Float | 1 | FFT 几何位移倍率 |
| `_NormalIntensity` | Range(0, 2) | 1 | FFT 法线与网格法线的混合强度 |
| `_TessellationFactor` | Range(1, 32) | 8 | Hull 阶段固定细分系数 |
| `_FoamIntensity` | Range(0, 1) | 0.5 | 波峰与边缘泡沫阈值控制 |

### Caustics

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_CausticsScale` | Range(0, 1) | 0 | 水面残差匹配半径的基础尺度 |
| `_CausticsIntensity` | Range(0, 2) | 1 | 焦散附加亮度倍率 |

### Transparency

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---:|---|
| `_Distortion` | Range(0, 1) | 0.1 | 屏幕空间背景折射偏移 |
| `_Alpha` | Range(0, 1) | 1 | 场景色与水面 PBR 的混合比例 |

## FFT 数据约定

运行时由 `FFTWaveOrchestrator` 注入三层级联纹理：

| 全局资源 | 内容 |
|---|---|
| `_WaveDisplacement0..2` | RGB 位移，A 通道泡沫/Jacobian 数据 |
| `_WaveNormal0..2` | 编码到 0–1 的世界方向法线 |
| `_WavePatchSize0..2` | 各层世界空间重复尺寸 |

位移采样权重直接相加；法线权重为 `0.6 / 0.3 / 0.1`；泡沫权重为 `0.5 / 0.3 / 0.2`。

## 焦散实现

当前焦散使用单点反推，不进行多射线追踪或后处理累积：

```text
水底 scenePosWS
    → 使用水平水面法线反推 surfaceHit
    → 在 surfaceHit 采样 FFT surfaceNor
    → 使用真实法线再次反推 correctHit
    → residual = correctHit - surfaceHit
    → confidence = 1 / (1 + residual² / radius²)
    → 焦散强度
```

`confidence` 表示候选水面点的闭环匹配程度，视觉上提供柔和焦散形态，但它不是严格的光能密度。

### 已知限制

- 水面与水底的反推距离仍以世界 Y 高度差计算，主要适合近似水平水面。
- 每个水底像素只有一个候选水面点，不能累加多个水面解的光贡献。
- `confidence` 是非负附加亮度，太阳直射时可能提高画面平均亮度，不保证能量守恒。
- `_CausticsScale` 同时参与匹配宽度和深度变化，视觉宽度不是独立物理参数。
- `correctNor` 保留用于水面闭环分析，当前最终 confidence 使用位置残差。

## 泡沫实现

- `ComputeWaveFoam`：读取 displacement alpha，使用 `_FoamIntensity` 做波峰阈值。
- `ComputeEdgeFoam`：根据场景深度差生成岸边遮罩，再用 FFT 泡沫扰动边缘。
- 最终泡沫为 `saturate(edge + wave)`，并使用主光颜色叠加。

## 屏幕折射

水面法线沿世界位置产生 `_Distortion * 0.1` 的偏移，并转换为屏幕坐标。`ComparePositionSS` 使用深度差判断扭曲坐标是否穿透前景；穿透时回退到未扭曲坐标。

使用前需在 URP Asset 中启用：

- Opaque Texture
- Depth Texture
- Depth Normals（当前焦散接收面遮罩需要）

## 性能概览

主要片元开销来自：

- 水面法线：3 次 normal 纹理采样。
- 焦散：2 次 `ComputeFFTNormal`，共 6 次 normal 纹理采样。
- 泡沫：边缘泡沫与波峰泡沫分别读取三层 displacement alpha。
- 场景数据：Scene Color、Scene Depth、Scene Normals。

当前焦散没有多射线循环，也不依赖额外 RenderPass；其限制与性能优势来自同一个单候选点近似。

## 扩展方向

- 将焦散匹配半径从 `_CausticsScale` 中独立出来。
- 使用有界聚焦项或统计归一化改善平均能量稳定性。
- 若允许额外缓冲，使用低分辨率前向投影或能量累积解决多水面解问题。
- 将固定 Tessellation Factor 升级为基于相机距离的自适应细分。

## 维护约定

- 渲染入口、结构体、材质参数和 Pass 保留在 `Water.shader`。
- Water 专属数学和采样函数放在 `WaterFunction.hlsl`。
- FFT 生成逻辑继续由 `FFT/FFT.compute` 与 `FFTWaveOrchestrator.cs` 维护。
- 跨效果通用函数继续使用 `Assets/Mine/Special/HLSL/`，不复制进 Water 私有库。

## 统一场接入

`FFTWaveOrchestrator` 现在继承 `FieldProvider`，注册为 Wave 输出。
有 FieldManager 时统一调度；没有 Manager 的旧场景仍由 Provider 自行更新。
FFT 频谱、IFFT 和资源继续由原组件持有，既有 GUID、引用、级联参数与全局纹理名保留。

公共采样库位于 `Assets/Mine/Scripts/FieldManager/WaveField.hlsl`，
WaterFunction.hlsl 保留原材质位移倍率、法线强度和泡沫混合权重。
停用 FFT 时全局纹理解绑，波浪采样回退为零位移、零泡沫和向上法线。
编辑模式使用实时场时钟；播放模式使用 Time.time。
公共风场不会自动覆盖 FFT 的频谱风速/风向参数。

原始输出与公共 GPU 采样对照：位移误差 5.96e-8，法线误差 0，泡沫混合一致。
