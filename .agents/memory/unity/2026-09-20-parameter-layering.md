# 2026-09-20 — 效果参数分三层：Technical / Performance / Artistic

## 背景

用户要求整理效果参数面板。分两轮落定：

**第一轮**给出两分组判据——技术性参数（`shadowMapResolution` / `cascadeCount` / `pssmLambda` /
`shadowDistance` / `depthBias` / `normalBias` / `quality`）保持不变，其余综合为美术性调整，
特点是「一个参数控制对应效果的一个功能」，参数精简。参照物是 Snowy 的
`Randomness` / `Coldness` / `Frost` / `Wind`。

**第二轮**用户自己推翻了第一轮的归类：

> `quality` 和 `shadowmap` 确实同源，但它们既不能说完全属于技术或者美术，不如说对性能影响更大，
> 而且不需要微调，直接划档位就行，所以新增性能档统一这两个参数

用户决策：档位表 = **3 档复用现有采样级**（High 保持当前默认）；**`cascadeCount` / `shadowDistance`
一并进档位**（知悉代价：级联数会改变阴影清晰的分布范围）。

## 结论 — 判据（三层）

| 组 | 判定标准 | 改它的理由 | 暴露形式 |
|---|---|---|---|
| Technical | 几何与投影、深度偏移 | 正确性（漏光/痤疮/覆盖） | 连续可调 |
| Performance | 纯成本，**不需要微调** | 帧率 | **划档位（枚举）** |
| Artistic | 每个参数对应一个观感维度 | 画面好看与否 | 连续可调 |

第三层的判据不是「属于技术还是美术」（那是个伪二分），而是两个正交问题：

1. **成本占比** —— 对帧率的影响是否压倒性
2. **是否需要微调** —— 合理取值是连续区间，还是有限几组固定组合

两个都答「是」的参数不该逐个暴露。给 8 个整数滑条，用户组合出的多数取值要么性能崩、
要么画质崩；给 3 个档位，每个档位都是已知良态。**暴露形式本身就是信息**。

`pssmLambda` 明确**不进**档位：它决定 texel 密度在级联间的分布方式，不是总成本量——
这是「同源」判据的边界，别把它扩大成「所有跟 shadow map 沾边的都进档位」。

**实现细节不进任何一组，内化为 shader 常量。** PCSS 落地：`PCSSFunction.hlsl` 顶部
`TEMPORAL_CLAMP_RADIUS / SIGMA / DEPTH_SCALE / NORMAL_POWER` 四个 `#define`，
替代原来的 5 个 uniform + 5 个 nameID + 5 次 `SetCompute*Param`。

PCSS 三层的字段分布：技术 5（`pcssComputeShader` / `pssmLambda` / `depthBias` / `normalBias`
+ 1 枚举）+ 性能 1 + 美术 3 + 调试 2。前两轮合计 20 → 13 → 11。
美术组为 `softness`（半影尺度）/ `blur`（保边模糊，0=关）/ `temporal`（时域收敛，0=关）。
三个开关类参数一律用「0 = 关闭」而非独立 `bool` + 强度两个字段——省一个字段，
且强度为 0 时本就无意义。

## 踩坑 1 — 「参数半接线」：一侧接线、另一侧是字面量

**本轮最重要的发现，跨效果通用。**

`shadowMapResolution` 在 C# 侧 `PCSSFeature.cs` 被用来做三件事：分配 Atlas RT、
烘焙 texel-aligned 正交投影进 `_CascadeLightVP`、设置逐级联 viewport。
但它**从未作为 uniform 传给 shader**——shader 里所有 texel↔UV 换算全是硬编码字面量
（`512.0` / `1024.0` / `2048.0`，共 9 处）。默认 2048 下两边数值恰好一致，
所以缺陷长期不可见，`PCSS.md` 的「已知限制」里甚至已经记了一笔却没人当回事。

**为什么危险**：它把「参数可调」变成了一句谎话。参数在 Inspector 里能动，
C# 侧真的按新值在跑，shader 侧却仍按旧值换算 → 分辨率一变就是必现的画面错乱，
而**错乱的原因离参数十万八千里**。一旦这种参数被升格为档位（本轮正是如此），
缺陷从「潜在」直接变「必现」。

**诊断手法 —— 这条必须记住**：

> **grep 数值字面量，不要 grep 参数名。**

参数名的搜索结果是「C# 里用了」，看着完全正常。真相只在数值层面：
搜 `2048` 会命中 shader 里那些本该是 `_AtlasParams` 的地方。
判据 = 对一个参数名执行 `grep -rn "<参数名>"` 得到的文件集合，
与 `grep -rn "<该参数的默认值>"` 得到的文件集合，**差集里若出现消费方文件，就是半接线**。

**修复契约**：尺寸类参数一旦下发，就必须是**唯一来源**，两边都从它推导。
PCSS 用 `float4 _PCSS_AtlasParams = (tile, 1/tile, atlas, 1/atlas)`，
`2×2` 布局固定故 `tile = atlas/2`，布局本身与分辨率无关。
打包成「尺寸 + 倒数」沿用工程既有的 `_ScreenSize` 惯例（`.xy` size / `.zw` reciprocal）。

**排除项**：`PCSS.compute` 里 `1.0 / 4096.0` 看着也像分辨率量，实为**深度域**比较 epsilon
（喂给 `DepthCmpLit` / `BlockerSearch` / `VariablePCF`），数值上恰好等于 `2×atlasRes`
纯属巧合。**参数化前必须确认每个字面量的语义域，不能按数值匹配。**

## 复用手法 1 — 位级等价的字面量参数化

把字面量抽成 uniform 有理由担心「画面会不会变」。判据是 IEEE754：
**除以 2 的幂 与 乘以它的倒数，结果位级相同**（1/2048 = 2⁻¹¹ 精确可表示，
除以 2 的幂是指数位平移，无舍入）。所以 `x / 2048.0` → `x * _AtlasParams.w`
在默认档下是**逐字节等价**的重构，不是近似。

这条给出一个很强的验证策略：**默认档下新旧代码应当画面无差别**，
所以「改了但看不出变化」是**通过**而非失败。真正的证据必须来自**非默认档**——
默认档下等价，只有换档才走得到修复的路径。冒烟测试必须换档跑。

（反例：若字面量是 0.1 这类非二进制精确值，`x / 0.1` 与 `x * 10.0` 不位级等价，此手法不成立。）

## 复用手法 2 — 档位表用纯函数展开，不在 pass 间传状态

档位展开结果被两个 pass 需要（Caster 要 RT 尺寸与级联数，PCSSPass 要 atlas 参数下发给 shader）。
不把 Caster 的私有 `m_AtlasRes` 提升成跨 pass 读取的公共状态，而是**两个 pass 各自调用同一个
纯函数** `Settings.GetTier(performance)` 重新展开：

```csharp
public readonly struct PerformanceTier { ... }
static readonly PerformanceTier[] k_Tiers = { ... };
public static PerformanceTier GetTier(Performance p) => k_Tiers[(int)p];
```

纯函数无状态 ⇒ 不存在不同步的可能，比「传一份、信它没被改」更强。
**推广**：跨 pass 需要的派生量，优先让两边各自从同一纯函数推导，而不是共享可变状态。

## 验证证据

- C# 编译通过（`unityctl asset refresh`）。
- 字面量清零：atlas 路径上不再有 `2048.0` / `1024.0` / `512.0`；`4096.0` 仅剩深度 epsilon 一处。
- uniform 契约静态核对：`_PCSS_AtlasParams` 两侧声明/绑定均存在，宽度均为 `float4`。
- 档位表展开：反射调 `GetTier` 逐档 dump，三档数值与设计表逐字段一致；
  High 档与整理前资产配置（2048 / 4 级联 / 50m / quality=High）逐字段相同。
- **键名匹配的证明手法**：资产回读到 High **不能**证明键名对——字段初始值就是 High，
  键名写错会静默回落成同一个值（上一轮同一个坑）。做法是**把资产改成非默认档再回读**
  （改成 `performance: 0` → 回读得 `Low`），顺便当换档冒烟用。一箭双雕。
- 运行时四量齐动（Low 档实跑，反射读活动 pass）：`ShadowRT=1024x1024`、
  `CascadeCount=2`、`CascadeSplits=(6.04, 30.00, 0, 0)`、`PCSS_LOW=True PCSS_MEDIUM=False`
  —— 档位驱动的四个量**全部在运行期生效**，非仅面板显示。
- 零错误：Console 无 error/warning；`Editor.log` 自标记点后无 `Shader error in 'PCSS'`、
  无 `Kernel at index ... is invalid`。
- Uniform 确实到达的**反向推理**：若 `_PCSS_AtlasParams` 未绑定则为 0，`.x = 0` 会让
  `20.0 * halfW0 / .x` 变 INF、`maskUV = maskPixels * 0` 变 0，输出必是 NaN/INF 垃圾。
  实测 `_PCSS_SoftShadow` 的 R 范围 `[0.7642 .. 1.0000]` 全部有限且干净 → uniform 到达。
  （比「同块相邻行的 `_ScreenSize` 能用所以它也能用」的同构推理更强。）

> 换档冒烟测试中途我曾误判「atlas 恒空、PCSS 对画面零贡献」，**该结论是错的**，
> 已在用户提示下复核推翻。复盘见 [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md)——
> 根因是把「分块资源的某几块为空」当成了缺陷，而它其实是场景几何分布的正确结果。

## 遗留

- 视觉判读（三档观感刻度、换档时 `softness`/`blur`/`temporal` 刻度是否稳定）由人工在 Game View 判读。
  注意当前阴影**尚未接入光照合成**（只在特定物体 shader 挂了 `CustomShadowCaster` 投射），
  观察走 `showShadowMap` 调试直出；且 `DebugOutputFeature`（`m_Active: 1, debug: 1`）驱动的
  Snowy `SnowAtmosphere` 晚于 PCSS，观察前需先关掉。
- 采样核形状未动：Low/Medium 仍是 32 点 VogelDisk 的**前缀**，不是独立设计的稀疏核。
- `cascadeCount < 4` 时 2×2 布局浪费 atlas 空间，未引入可变布局。
- 同一判据尚未应用到 SSR / SSSM / DDOF / RimToon —— 它们的 `blurScale` + `enableBlur`
  同样是可合并的开关+强度对；且需按踩坑 1 的判据**逐个查是否存在半接线参数**。
- `PCSS.md:240` 记的「cap 256」与代码 `min(penumbraPixels, 100 / halfWci)` 不符（存量笔误，本轮已修）。

---

## 附 1：核对脚本（uniform 绑定契约）

```python
import re
h  = open('PCSSFunction.hlsl').read()
c  = open('PCSS.compute').read()
cs = open('PCSSFeature.cs').read()

decl = set(re.findall(r'^\s*(?:Texture2D|RWTexture2D|SamplerState)[^\n]*?(\w+);', h, re.M))
decl |= set(re.findall(r'^\s*(?:float4x4|float4|float3|float2|float|int)\s+([^;]+);', h, re.M))
flat = {n.strip().split('[')[0].strip() for d in decl for n in d.split(',')}
flat = {n for n in flat if n.startswith('_')}

bind  = set(re.findall(r'"(_[A-Za-z_]\w*)"', cs))
bind |= set(re.findall(r'PropertyToID\("(_[A-Za-z_]\w*)"\)', cs))

urp_global = {'_CameraDepthTexture', '_CameraNormalsTexture',
              '_WorldSpaceCameraPos', '_ZBufferParams'}
print("HLSL 声明但 C# 未绑定 :", sorted(flat - bind - urp_global) or "无")
print("C# 绑定但 HLSL 未声明 :", sorted(bind - flat - urp_global) or "无")
```

注意：多声明行（`float4 _FrustumRay0, _FrustumRay1, ...;`）会让正则产出带 `)` 的
伪影名，需要人工看一眼再判定。

## 附 2：半接线参数扫描（踩坑 1 的可执行形式）

对每个候选参数 `P`（默认值 `V`），在效果目录内跑：

```bash
# 1) 谁引用了参数名 → 这才是「接线」
grep -rn "P"  <effect-dir> --include=*.cs
# 2) 谁引用了默认值字面量 → 若命中 shader/compute 侧，就是半接线
grep -rn "\bV\b" <effect-dir> --include=*.hlsl --include=*.compute --include=*.shader
```

第 2 步命中即需人工确认该字面量的**语义域**（尺寸？深度？世界单位？），
只有同域才该被参数化。默认值取整十整百时要警惕误报（见 `1.0/4096.0` 反例）。
