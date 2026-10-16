# NoiseGenerator — 程序化噪声纹理生成

> 日期: 2026-09-03
> 路径: `Assets/Mine/Scripts/NoiseGenerator/`
> 依赖: Unity 6

---

## 概述

`NoiseGenerator` 是一套纯 CPU 的 3D 噪声生成工具，将多种噪声算法打包为静态方法，在 Editor 和 Runtime 均可调用。配套 `NoiseGeneratorWindow` 提供可视化参数面板，一键生成并保存 `Texture2D` / `Texture3D` 资源。

- **Perlin Noise** — 经典梯度噪声，适合自然纹理、体积云、地形高度图
- **Voronoi (Worley) Noise** — 细胞噪声，适合石材纹理、生物结构、星云效果

两种噪声均支持 **无缝平铺（seamless tiling）**，生成的纹理可直接用于 WrapMode.Repeat。

---

## 文件清单

### C# Scripts

| 文件 | 类 | 职责 |
|---|---|---|
| `NoiseGenerator.cs` | `NoiseGenerator` (static) | 外观入口：纹理生成、无缝平铺、通道打包、共享工具 |
| `Noises/PerlinNoise.cs` | `PerlinNoise` (static) | Perlin 3D 噪声核心：perm 表、梯度插值 |
| `Noises/VoronoiNoise.cs` | `VoronoiNoise` (static) | Voronoi 3D 噪声核心：特征点哈希、距离计算 |

### Editor

| 文件 | 类 | 职责 |
|---|---|---|
| `Editor/NoiseGeneratorWindow.cs` | `NoiseGeneratorWindow : EditorWindow` | 可视化面板：参数调节、hash 缓存预览；Generate / Load / Save 与资产所有权由本窗口持有 |

---

## 架构

```
NoiseGenerator (facade)
├── NoiseType enum    → Perlin / Voronoi
├── 纹理生成          → Generate3DTexture / GenerateChannelTexture / PackChannels
├── 无缝平铺          → MakeSeamless2D / MakeSeamless3D（边界拷贝闭合）
├── 采样入口          → Sample3D → SampleTileable3D / SampleBaseNoise
├── 共享工具          → Repeat01 / Lerp / Seed01
│
├── PerlinNoise       → Sample(x,y,z,scale)
│   ├── perm[512]     ← 标准排列表
│   ├── Fade(t)       ← 6t⁵-15t⁴+10t³ 平滑曲线
│   └── Grad(hash, x, y, z) ← 梯度向量点积
│
└── VoronoiNoise      → Sample(x,y,z,scale)
    ├── Hash01        ← 3D 整数哈希 → [0,1)
    └── FeaturePoint  ← 单元格内随机特征点
```

### 数据流

```
调用方
  → NoiseGenerator.Generate3DTexture(size, scale, seamless, noiseType)
    → 遍历 [x,y,z] → Sample3D(wx, wy, wz, scale, seamless, noiseType)
      ├─ seamless=false → PerlinNoise.Sample / VoronoiNoise.Sample
      └─ seamless=true  → SampleTileable3D（四维8点插值→消除接缝）
    → MakeSeamless3D（最后一行/列/层从对侧拷贝，闭合周期）
    → Texture3D.SetPixels + Apply

通道打包
  → NoiseGenerator.PackChannels(Texture2D[] sources, int resolution)
    → 逐像素 GetPixelBilinear 采样各通道 R → RGBA 输出
```

---

## 公共 API

### Texture3D 生成

```csharp
Texture3D tex = NoiseGenerator.Generate3DTexture(
    size: 32,           // 每维分辨率 [4, 256]
    scale: 4f,          // 噪声频率倍率
    seamless: true,     // 是否无缝平铺
    noiseType: NoiseGenerator.NoiseType.Perlin
);
```

### Texture2D 生成（单通道，从 3D 切片）

```csharp
Texture2D tex = NoiseGenerator.GenerateChannelTexture(
    resolution: 256,    // 分辨率 [8, 2048]
    scale: 4f,          // 噪声频率倍率
    seamless: true,     // 是否无缝平铺
    noiseType: NoiseGenerator.NoiseType.Voronoi,
    randomSeed: 42f     // 选择 3D 体中哪个 Y 切片
);
```

### Texture2D 生成（多通道 RGBA 打包）

```csharp
// 各通道独立生成（可不同噪声类型/种子），再打包为 RGBA
var r = NoiseGenerator.GenerateChannelTexture(128, 4f, true, NoiseGenerator.NoiseType.Perlin, 0f);
var g = NoiseGenerator.GenerateChannelTexture(128, 8f, true, NoiseGenerator.NoiseType.Voronoi, 7f);
Texture2D packed = NoiseGenerator.PackChannels(new[] { r, g }, 128);
// 最多 4 个来源 → R/G/B/A；来源为 null 时该通道为 0
// 各通道分辨率可低于输出：低分辨率来源在目标分辨率下双线性重采样，保留低频观感
```

### 原始采样

```csharp
float value = NoiseGenerator.Sample3D(
    x: 0.5f, y: 0.3f, z: 0.8f,
    scale: 4f,
    seamless: true,
    noiseType: NoiseGenerator.NoiseType.Perlin
);
// 返回值 ∈ [0, 1]
```

---

## 噪声算法

### Perlin Noise

经典 3D Perlin 噪声实现：

- **梯度向量**：12 条边方向 + 4 条体对角线（标准的 16 方向集）
- **平滑曲线**：`Fade(t) = 6t⁵ − 15t⁴ + 10t³`（保证 C² 连续，消除视觉上的二阶不连续）
- **排列表**：标准 256 值 × 2 副本（512 条目），避免周期性索引越界
- **输出值域**：`[0, 1]`（内部 `[-1,1]` 经过 `*0.5+0.5` 映射）

### Voronoi (Worley) Noise

3D 细胞噪声，计算到最近特征点的距离：

- **特征点生成**：每个整数格点的特征点偏移量由 `Hash01(cx,cy,cz, seed)` 确定，落在 `[0,1)³` 范围内
- **搜索邻域**：包含自身在内的 3×3×3 共 27 个格点
- **距离度量**：欧几里得距离
- **输出值域**：`[0, 1]`（`1 − clamp(minDist, 0, 1)`，靠近特征点 = 0，远离 = 1）

---

## 无缝平铺机制

`NoiseGenerator` 实现两层无缝保障：

1. **采样阶段**（seamless=true 时）：
   - `SampleTileable3D` 对输入坐标做 8 个偏移采样（`{0,1}³`），通过三线性插值消除周期边界的梯度不连续
   - 坐标超出 [0,1] 时 `Repeat01` 做模运算

2. **后处理阶段**（MakeSeamless）：
   - `MakeSeamless3D`：将最后一层/行/列的像素值替换为第一层/行/列的对应值
   - `MakeSeamless2D`：同理，消除纹理自身的边界接缝

---

## Parameters

### Texture3D 设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `size` | int | 32 | 每维分辨率，范围 [4, 256] |
| `scale` | float | 4 | 噪声缩放/频率倍率，越大噪声越密集 |
| `seamless` | bool | true | 是否生成无缝平铺纹理 |
| `noiseType` | NoiseType | Perlin | 噪声算法选择 |

### Texture2D Channel 设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `resolution` | int | 32 | 分辨率，范围 [8, 2048] |
| `scale` | float | 4 | 噪声缩放/频率倍率 |
| `seamless` | bool | true | 是否无缝平铺 |
| `noiseType` | NoiseType | Perlin | 噪声算法选择 |
| `randomSeed` | float | 0 | 随机种子，控制 3D 体中采样的 Y 切片位置 |

---

## 使用方式

### 通过 Editor 窗口

1. 菜单栏：`Tools → Noise Generator...`
2. 顶部切换输出模式 `Texture3D` / `Texture2D`
3. 调整参数（噪声类型、分辨率、缩放、无缝选项；2D 模式逐通道卡片独立调整，支持 `Randomize` 种子）
4. 点击 `Generate` 生成当前模式资产。3D 切片显示当前资产实际像素；尚无 3D 资产时按参数预览。切片按轴和位置缓存。
5. 窗口持有当前模式的 `Asset Path`，默认值：`Assets/Textures/Noise3D.asset` / `Assets/Textures/Noise2D.asset`。Save 与 Load 共用当前模式的一条路径；覆盖其他同类型资产前确认，保留 GUID；Load 后直接 Save 不弹覆盖确认。
6. 点击 `Load` 回看已有资产；不恢复生成参数。已载入或保存的 2D 资产不会被参数预览替换；要应用新参数，点击 `Generate`。未落盘的 2D 程序化预览仍随参数更新。
7. 窗口只持有一个当前资产；新的 Generate / Load 或 2D 自动打包会换源，释放被替换的未落盘结果。切换输出模式本身不保证立即释放；已落盘资产始终由 AssetDatabase 管理。

窗口独立继承 `EditorWindow`，资产 IO 与生命周期均在本工具内实现。3D 预览按载入纹理的真实宽、高、深切片；纹理不可读时提示预览不可用，不以当前参数伪造载入内容。

### 通过代码

```csharp
using UnityEngine;

// 生成 3D Perlin 噪声纹理
var noise3D = NoiseGenerator.Generate3DTexture(32, 4f, true, NoiseGenerator.NoiseType.Perlin);

// 生成 2D Voronoi 无缝纹理
var noise2D = NoiseGenerator.GenerateChannelTexture(512, 8f, true, NoiseGenerator.NoiseType.Voronoi, 0.5f);

// 四通道打包（R 法线细节 / G 粗糙度 / B 高度）
var packed = NoiseGenerator.PackChannels(new[] { channelR, channelG, channelB }, 512);

// 对 Shader 中的体积噪声进行手动采样
float n = NoiseGenerator.Sample3D(uv.x, uv.y, uv.z, 4f, true, NoiseGenerator.NoiseType.Perlin);
```

### 常见用途

| 用途 | 推荐噪声 | 设置 |
|------|----------|------|
| 体积云基础层 | Perlin 3D | `size=64, scale=4` |
| 体积云细节层 | Voronoi 3D | `size=64, scale=12` |
| 地形高度图 | Perlin 2D | `resolution=512, scale=8` |
| 石材/大理石 | Voronoi 2D | `resolution=512, scale=6` |
| 水波法线图 | Perlin RGBA (4通道) | `channels=4, scale=3` |

---

## 扩展方向

| 方向 | 说明 |
|------|------|
| Simplex Noise | 比 Perlin 具有更好的各向同性，减少方向性 artifacts |
| FBM / Turbulence | 多倍频叠加（楼座式），octaves + lacunarity + gain |
| GPU Compute Shader | 将生成移到 GPU，处理 256³ 及以上的体纹理 |
| Voronoi 距离类型 | 除 `F1`（最近点）外增加 `F2`（次近点）、`F2-F1`（边缘）、`F1*F2` |
| Domain Warping | 用一组噪声扭曲另一组噪声的输入坐标，产生有机流动效果 |
| Curl Noise | 从 Perlin 梯度场提取旋转分量，用于无散流体速度场 |
| 运行时异步生成 | 用 `UniTask` 或 `Job System` 在后台线程生成，避免主线程卡顿 |
| 序列化 3D 切片 | 将 3D 体纹理的切片导出为 2D 纹理数组序列，用于 Flipbook 动画 |
