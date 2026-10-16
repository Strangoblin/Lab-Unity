---
name: 2026-09-17-uv-aspect-anisotropy
description: UV 空间各向异性 — 屏幕 UV 内做 R/S 必然剪切/拉伸，R/S 移入等比空间 + 逆映射语义
date: 2026-09-17
---

# UV 空间各向异性（Snowy.shader / TRS.hlsl）

## 背景

`Snowy.shader` 是标准 UV 内的单面片实验：屏幕 UV 经逆 TRS 映射回面片 UV，可见面片沿对角线从左上角移到右下角，同时旋转并放大。原始实现直接把屏幕 UV 丢给后来被删除的 `TRS2D_InverseTransformPoint`（无 aspect 版），面片在非方形屏幕上被**剪切**。

## 核心洞察一：逆映射，参数描述的是物体

后处理采样的是屏幕 UV，只能逐像素问"该从贴图哪里读"，无法逐纹素写。故代码写的是 `q = M⁻¹(p)`，而可见物体是

```
{ p | M⁻¹(p) ∈ [0,1]² } = M([0,1]²)
```

**逆像的原像就是正变换**——面片在屏幕上的姿态恰好等于 `translation / angle / scale` 本身。代入验证：面片中心 `M(0.5,0.5) = (0.5,0.5) + translation = (cycle, 1-cycle)`，正是 `//(0,1) -> (1,0)` 注释写的东西。

参数解释权归谁，是 `TRS2D_TransformUV` 与 `TRS2D_InverseTransformUV` 的唯一区别。另外 `(T·R·S)⁻¹ = S⁻¹·R⁻¹·T⁻¹` **顺序是反的**，不能靠"取负调用正变换"。

## 核心洞察二：UV 空间不是各向同性空间

UV 是 `[0,1]²` 归一化空间，但屏幕是 `W × H`。同为 0.1 的 u 增量与 v 增量，像素长度相差 aspect 倍。**在 UV 空间里做 R/S，等价于把一个各向异性缩放夹在旋转和物体之间**，正方形必然被剪切。

正确分解：

```
R/S 在等比空间（u * aspect）执行    —— 保证形状是刚体旋转的
T   留在 UV 空间                    —— 保证 (0,1)->(1,0) 角到角位移语义不变
```

`translation` 是**位置**而非距离，等比转换对位置是双射，往返后轨迹逐位不变——所以修 aspect 不影响运动路径，只影响形状。

## 踩坑一：第一版修错了

第一版把 `A(p) = (p.x * aspect, p.y)` 同时作用到了面片单位正方形上：

```hlsl
float2 local  = A(uv - translation, aspect);   // 错：A 也污染了面片空间
float2 center = A(pivot, aspect);
return Ai(Rotate(local - center, -θ) / scale + center, aspect);
```

后果：`[0,1]²` 在等比空间里成了 `aspect × 1` 的矩形。**剪切修掉了，但留下一个被 aspect 拉长的长方形**——形状仍然是错的，只是错法变了。面片单位正方形必须原样保持，只对屏幕 UV 和物体落点做等比转换。

## 踩坑二：测量空间的 aspect 必须与变换一致

写形状验证时，用 `(uv.x * W, uv.y * H)` 换像素，却给变换传了不同的 `asp`。结果只有 `W/H == asp` 那一行是正方形，其余全错——**量的是一个不属于被测变换的空间**，等于在验证脚本里重犯了被测的那个错误。像素空间的 aspect 必须由被测 aspect 导出（`W = H * asp`）。

## 最终实现

```hlsl
float2 TRS2D_TransformUV(float2 particleUV, float2 translation, float radiansAngle,
                         float2 scale, float2 pivot, float aspect)
{
    float2 local = TRS2D_RotateIsotropic((particleUV - 0.5) * scale, radiansAngle);
    float2 center = TRS2D_UVToIsotropic(pivot + translation, aspect);
    return TRS2D_IsotropicToUV(local + center, aspect);
}

float2 TRS2D_InverseTransformUV(float2 uv, float2 translation, float radiansAngle,
                                float2 scale, float2 pivot, float aspect)
{
    float2 center = TRS2D_UVToIsotropic(pivot + translation, aspect);
    float2 local = TRS2D_RotateIsotropic(TRS2D_UVToIsotropic(uv, aspect) - center, -radiansAngle);
    return local / max(abs(scale), float2(1e-6, 1e-6)) * sign(scale) + 0.5;
}
```

- 面片约定为单位正方形 `[0,1]²`（中心 0.5），与 Sprite/粒子贴图一致
- `scale` 语义是**屏幕高度占比**（各向同性尺寸的必然结果），不再是 UV 宽度占比
- aspect 走参数而非库内直读 `_ScreenParams`：库的契约是零 URP include，直读会把"调用方必须先 include Core.hlsl"变成隐含前置依赖（HLSL 编译所有函数体，无人调用也会炸）

`_ScreenParams` 在 URP 里声明于 `Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl`，**非内置 CG 全局**。注意它是**相机目标**尺寸：效果若跑在半分辨率 RT，应改从 RT 自身尺寸取 aspect。

## API 决策：2D 接口一律内建 aspect

曾同时存在无 aspect 的 `TRS2D_TransformPoint` / `TRS2D_InverseTransformPoint` / `TRS2D_TransformDirection`。三者已**删除**：本库 2D 半边定位就是 UV 空间，UV 空间永远各向异性，"无 aspect 的 UV 变换"没有合法用例，只留作踩坑入口——最初的 bug 正是照它写出来的。

配套改名 `TRS2D_Rotate` → `TRS2D_RotateIsotropic`，把空间前提写进函数名：在 UV 空间直接调用它会剪切，且**静默无报错**。

正变换 `TRS2D_TransformUV` 是补上的。只留逆变换，下一个需要正变换的人就会手搓一个——那正是 bug 的来路。配对存在也让"参数解释权相反"这件事在接口层可见。

## 验证方法与数据

**解析前向映射**算四角（无离散误差），量三个数：边长比 `L1/L2`、相邻边点积 `cos(e1,e2)`、面积。

| 版本 | 边长 px | L1/L2 | cos(e1,e2) | 面积 px² | 判定 |
|---|---|---|---|---|---|
| 原版（无 aspect） | 132.2 × 99.1 | 1.3342 | −0.4553 | 11664 | 平行四边形（被剪切） |
| 第一版 | 144.0 × 81.0 | 1.7778 | 0.0000 | 11664 | 长方形（被拉伸） |
| 修正版 | 81.0 × 81.0 | 1.0000 | 0.0000 | 6561 | 正方形 ✓ |

（`W/H = aspect = 1.7778`，`cycle = 0.5`，`scale = 0.075`，期望 81 px/边）

扫描整屏栅格化交叉验证：`11662 / 11662 / 6560`，与解析值差在边界像素。

**最终版回归**（aspect ∈ {0.5, 1, 1.333, 1.778, 2.333} × scale ∈ {0.06, 0.075, 0.095}）：

- 往返恒等 `T(T⁻¹(p)) == p`：15000 点（含 `[0,1]²` 外），最大误差 **2.0e-7**
- 四角形状：12/12 组合 `L1/L2 = 1.0000`、`cos = 0.0000`、面积比 `1.0000`，边长精确等于 `scale * H`
- 中心轨迹：所有 aspect 下逐位相同，恒为 `(cycle, 1-cycle)`

判定口诀：`L1/L2 = 1 且 cos = 0` 才是正方形；`cos ≠ 0` = 剪切，`L1/L2 ≠ 1` = 拉伸。两个数分别对应两类错误，能定位到"修错了哪一半"。

**方法论坑**：不要用二阶矩协方差的特征值判形状。正方形与长方形的特征值是**简并的**（主轴任意），取到的"角"其实是包围盒极值点，量出来的边长比恒为 1.0，完全掩盖错误。用解析前向映射算四角。

## 关键函数

- `TRS2D_UVToIsotropic` / `TRS2D_IsotropicToUV`（TRS.hlsl）：UV ↔ 等比空间桥接
- `TRS2D_TransformUV` / `TRS2D_InverseTransformUV`（TRS.hlsl）：UV 空间的 2D TRS 对，aspect 内建
- `TRS2D_RotateIsotropic`（TRS.hlsl）：等比空间内的正交旋转基元
- `SnowyParticleUV`（Snowy.shader）：单面片姿态插值 + 逆映射

## 教训

**各向异性空间里做旋转必然变形**：这是与 ddx/ddy 基底陷阱同类的基底错误——把变换放错了空间，且静默无报错。要刚体旋转，R/S 就必须与各向同性基底配对；验证时，测量空间也必须与变换空间同基底。见 rules/shader-development.md。
