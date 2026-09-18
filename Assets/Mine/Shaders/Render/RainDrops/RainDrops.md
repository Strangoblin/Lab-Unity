# RainDrops — 程序化雨滴着色器

**路径:** `Assets/Mine/Shaders/RainDrops/RainDrops.shader`
**可复用库:** `Assets/Mine/Special/Shaders/RainDrop.hlsl`
**类型:** Transparent (URP UniversalForward)
**目标管线:** Unity 6 URP 17+

---

## 架构设计

### 空间变换管线

```
ObjectUV ──[RemapUV]────→ WorldUV ──[Grid划分]──→ st ──[RemapGridUV]──→ ShapeSpace ──[DropShape]──→ 结果
  大世界扭曲               st∈[0,1)×[0,1)       小世界→物体空间        纯形状采样
  flow/wiggle/slip                                  aspect/scale/stretch   SDF/遮罩/法线/折射
```

| 阶段 | 函数 | 职责 |
|---|---|---|
| 大世界 | `RemapUV` | 列流速差异、sin 横向扭曲、松弛振荡器滑落 |
| 物体空间 | `RemapGridUV` | 中心归零、aspect 校正、size 缩放、dropLen 拉伸、seed 抖动 |
| 采样 | `DropShape` | 上尖下圆 SDF → mask/normDist/radialDir/offset |

### 层级架构

所有图层共用同一个 `DropLayer` 函数，通过 `DropConfig` 结构体差异化参数：

```
DropConfig cfg1 ──→ DropLayer ──→ result1 ──┐
                                            ├── BlendLayer ──→ 最终 drops
DropConfig cfg2 ──→ DropLayer ──→ result2 ──┤      ↑
                                            │   可链式追加无限层
DropConfig cfgN ──→ DropLayer ──→ resultN ──┘
```

### 当前三层配置

| 层 | 特征 | dropLen | dropSize | 折射 | 运动 |
|---|---|---|---|---|---|
| 流动水滴 | 尖顶拉伸、强折射 | `_DropLength` | `_DropSize` | `_Refraction` | RemapUV 扭曲 + 滚动 |
| 静态小雨滴 | 圆形、密集、折射减半 | `0.5` | `_DropSize` | `_Refraction` | 慢脉冲 |
| 细长水痕 | 极细长、稀疏、仅湿润 | `0.99` | `_DropSize * 0.5` | `_Refraction * 0.2` | 极慢、残留感 |

### 层级混合策略 (BlendLayer)

| 字段 | 策略 |
|---|---|
| `mask` | `max(a, b)` — 并集 |
| `offset` | `(a×wa + b×wb) / total` — mask 加权平均 |
| `normDist` | mask 加权平均 |
| `radialDir` | `normalize(a×wa + b×wb)` — 加权方向合成 |

---

## RainDrop.hlsl 可复用库

其他 shader 只需 `#include` 即可接入完整雨滴效果：

| 类别 | 符号 |
|---|---|
| 结构体 | `DropShapeResult`, `DropLayerResult`, `DropConfig` |
| 工具 | `Hash2D`, `Saw` |
| 空间变换 | `RemapUV`, `RemapGridUV` |
| 形状 | `DropShape` |
| 编排 | `DropLayer` |
| 混合 | `BlendLayer` |

### DropConfig 字段

```hlsl
struct DropConfig
{
    float  speed;       // 滚动速度 (0=静止)
    float  wiggle;      // 松弛振荡幅度
    float  range;       // 形状随机抖动范围 (越大越随机)
    float2 gridScale;   // 网格密度 (cols, rows)
    float  coverage;    // 覆盖率 [0,1]
    float  dropLen;     // 顶部拉伸 [0,1], 0=圆形, 1=尖顶触顶
    float  dropSize;    // 底部圆半径
    float  dropFacing;  // >0.5=面朝摄像机, <0.5=背朝摄像机
    float  sawSpeed;    // Saw 脉冲周期速度
    float  sawSmooth;   // Saw 软边宽度 [0,1]
    float  refraction;  // 折射强度
    bool   applyRemap;  // 是否应用 RemapUV 扭曲
};
```

---

## 参数说明

### Drop Layer — 流动水滴

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_Columns` | Range(4, 40) | 12 | 网格列数 |
| `_Rows` | Range(1, 6) | 2 | 网格行数 |
| `_Coverage` | Range(0, 1) | 0.85 | 格子产生水滴的概率 |
| `_DropSize` | Range(0, 0.5) | 0.35 | 水滴底部圆半径 |

### Motion — 运动

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_FlowSpeed` | Range(0, 1) | 0.2 | 流动速度（每列随机调制） |
| `_Wiggle` | Range(0, 1) | 0.5 | 松弛振荡器滑落幅度 |

### Shape — 形状

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_DropLength` | Range(0, 1) | 0.6 | 顶部拉伸：0=圆形，1=尖顶触达格子顶部 |
| `_DropFacing` | Toggle | 0 | 0=背朝摄像机(钟形发散)，1=面朝摄像机(中心放大+边缘压缩) |

### Refraction — 折射

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_Refraction` | Range(0, 0.06) | 0.025 | 透镜折射强度 |

### Wetness — 湿润着色

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_Darken` | Range(0, 0.5) | 0.15 | 湿润区域变暗程度 |
| `_Desaturate` | Range(0, 1) | 0.25 | 湿润区域去饱和度 |

### Specular — PBR 高光

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `_SpecIntensity` | Range(0, 5) | 1.2 | 高光强度 |
| `_Roughness` | Range(0, 1) | 0.15 | 粗糙度（内部 `smoothness = 1 - roughness`） |
| `_NormalTilt` | Range(0, 1) | 0.4 | 法线倾斜（0=中心无突起，1=边缘最大倾斜） |

---

## 函数详解

### RemapUV

```
objectUV
  → sin 横向扭曲 (频率按位置随机)
  → sin 纵向压缩 (每列不同频率, colR)
  → flow scroll (time * speed * colR)
  → 松弛振荡器: slipCycle = pow(frac(time * 0.12 + colR * 2), 4)
    慢速积累 → 快速释放, 模拟"挂住→突然滑落", 无回退
```

### RemapGridUV

```
st ∈ [0,1)×[0,1)  (滴中心在 0.5, 0.5)
  ① 中心归零: uv = st - (0.5, 0.5)
  ② 宽高比:   uv.y *= lerp(aspect, 1/scale, dropLen)
  ③ 缩放:     uv *= (rand.x * 0.4 + 1.2) / size
  ④ 抖动:     uv += rand
  返回单位形状空间 (圆半径=1, 锥尖在 y≈1)
```

格子坐标系:

```
 (0,1) ┌─────────┐ (1,1)
       │         │
       │  格子   │
 (0,0.5)    ●    │  ← 水滴中心 (0.5, 0.5)
       │   (圆)  │     底部圆形, 顶部可拉伸为尖顶
       │         │
 (0,0) └─────────┘ (1,0)
```

### DropShape

单位空间 SDF（底部圆半径=1，顶部锥尖在 y=1）：

```
dy > 0 (顶部): taper = 1 - saturate(dy), rx = max(taper, 0.01)
               dist = length(dx/rx, dy)  ← 越往上越窄
dy ≤ 0 (底部): dist = length(dx, dy)    ← 半圆

遮罩: 1 - smoothstep(0.7, 1.0, dist)  ← 固定软边 (外层 30% 渐变)

折射:
  背朝摄像机: lens = t*(1-t)*4    ← 钟形, 中间最强
  面朝摄像机: lens = (t-0.45)*2.5 ← 中心负(放大), 边缘正(压缩)
  offset = radialDir * lens * refraction * mask
```

### DropLayer

```
DropConfig 解包 → aspect / gridUV / id / st / Hash2D
  → coverage 剔除
  → ti = frac(time * sawSpeed + rand 相位)
  → intensity = Saw(sawSmooth, ti)
  → rand = (rand - 0.5) * range
  → RemapGridUV(st, rand, dropLen, aspect, dropSize)
  → DropShape(shapeUV, intensity, refraction, facing > 0.5)
  → 输出 DropLayerResult
```

### BlendLayer

链式混合任意数量的 DropLayerResult。每步将新层按 mask 权重融合到累积结果中。

---

## 使用方式

### 前置条件

1. **URP Asset** → 勾选 **Opaque Texture**
2. 材质指定为 `Render/RainDrops`
3. 渲染队列自动 Transparent

### 添加新图层

```hlsl
DropConfig cfg = { speed, wiggle, range,
    float2(cols, rows), coverage, dropLen, dropSize, dropFacing,
    sawSpeed, sawSmooth, refraction, applyRemap };
drops = BlendLayer(drops, DropLayer(input.uv, _Time.y, cfg));
```

### 其他 Shader 复用

```hlsl
#include "Assets/Mine/Special/Shaders/RainDrop.hlsl"
// 即可使用 DropLayer / BlendLayer / DropConfig 全部功能
```

### 性能

- 每层 ~10 条指令 (Hash + RemapGridUV + DropShape + BlendLayer)
- 1 次 `SampleSceneColor` + 1 次 `GetMainLight`（与层数无关）
- 无 3×3 邻域搜索，无分支发散

---

## 不同表面的着色推荐

### 玻璃窗（默认）

雨滴在玻璃外侧，水珠独立、折射强、高光锐利。

| 参数 | 推荐值 | 原因 |
|---|---|---|
| `_Refraction` | 0.025 | 强透镜扭曲，透过水滴看场景变形 |
| `_DropLength` | 0.6 | 尖顶明显，模拟重力拉长 |
| `_DropFacing` | 0 | 背朝摄像机，钟形折射 |
| `_Darken` | 0.15 | 轻微变暗 |
| `_Desaturate` | 0.25 | 轻微去饱和 |
| `_Roughness` | 0.15 | 镜面般锐利高光 |
| `_SpecIntensity` | 1.2 | 明亮高光 |
| `_NormalTilt` | 0.4 | 明显突起感 |

### 皮肤 / 粗糙表面

水渗入皮肤纹理，无独立水珠、无折射、泛 diffuse sheen、本色更饱和。

| 参数 | 推荐值 | 原因 |
|---|---|---|
| `_Refraction` | 0 | 水膜太平坦，不成透镜 |
| `_DropLength` | 0 | 圆形湿斑，无尖顶 |
| `_DropFacing` | 0 | 无关紧要（折射已关） |
| `_Darken` | 0.25 | 更明显的变暗 |
| `_Desaturate` | -0.3 | **负值=增饱和**（水填平微孔→表面散射减少→本色更纯） |
| `_Roughness` | 0.6 | 宽泛的 diffuse sheen |
| `_SpecIntensity` | 0.4 | 微弱高光 |
| `_NormalTilt` | 0.05 | 几乎无突起，湿斑是平的 |

需要去饱和→增饱和切换时，将 Frag 中：
```hlsl
lerp(wetColor, luma.xxx, _Desaturate * w)
```
改为：
```hlsl
lerp(wetColor, wetColor * 1.15, _SaturateBoost * w)  // 增饱和
```

### 车漆 / 光滑金属

雨滴大颗、高光强烈、折射明显。

| 参数 | 推荐值 | 原因 |
|---|---|---|
| `_Refraction` | 0.04 | 更强扭曲 |
| `_DropLength` | 0.7 | 更长尖顶 |
| `_Darken` | 0.1 | 弱变暗 |
| `_Desaturate` | 0.1 | 弱去饱和 |
| `_Roughness` | 0.05 | 极锐利高光 |
| `_SpecIntensity` | 2.5 | 非常亮的高光斑 |
| `_NormalTilt` | 0.6 | 强突起感 |
| `_Coverage` | 0.6 | 稀疏大颗 |

### 雨后路面 / 湿润土壤

水膜覆盖、极暗、几乎无高光、增饱和。

| 参数 | 推荐值 | 原因 |
|---|---|---|
| `_Refraction` | 0 | 无透镜 |
| `_DropLength` | 0 | 圆形扩散 |
| `_Darken` | 0.45 | 大幅度变暗 |
| `_Desaturate` | -0.5 | 强增饱和 |
| `_Roughness` | 0.9 | 极模糊高光 |
| `_SpecIntensity` | 0.1 | 几乎无 |
| `_NormalTilt` | 0 | 无突起 |

---

## 已知限制

- URP 多层透明重叠时 `_CameraOpaqueTexture` 不含后面透明物体
- 形状为程序化 SDF，非物理 Navier–Stokes 水膜模拟
- 仅主光源高光 (`GetMainLight`)，不支持多光源
- `Desaturate` 仅支持正向（去饱和），皮肤的增饱和需手动改 shader

---

## 扩展点

- `DropShape` 可替换为纹理采样：`tex2D(_DropTex, shapeUV * 0.5 + 0.5)`
- 新增图层只需声明 `DropConfig` + 一行 `BlendLayer`
- `BlendLayer` 混合策略可按需修改（如改为 `lerp` 替代加权平均）
- 折射模式可扩展为三档（球面/柱面/平面）替代当前 bool 切换
