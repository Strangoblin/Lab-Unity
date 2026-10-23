# SSC Circle 边界与列密度驱动高度：结构计划（2026-10-23）

用户要求：先把 XZ Box 换为 Circle 试效果；重新设计整体密度，使云层高度随局部密度变化；此次先计划新结构。**已实施只有边界图元切换，下文密度重设计尚未实施。**

## 已实施与当前基线

- `Assets/Mine/Shaders/PostProcess/SSC/SSC.shader` 改为 `q=positionWS.xz/max(abs(_CloudParamB.xz)*0.5,1e-6)`，使用 `smoothstep(0,0.8,SDF_Circle(q,1))`。当前 XZ=500×500 时是半径250的圆，边界渐变范围为径向200世界单位；不同XZ尺寸时是贴合体积的椭圆。
- Y 不参与此边界权重；全局高度范围检查、原噪声/高度/阈值/光照积分仍保留。RayBounds 使用原 AABB 作保守包围，圈外密度为0。
- 共享 Box/Square 的逐轴归一化保留，见 [已验证契约](2026-10-23-sdf-axis-normalization.md)。此次没有回退共享库修改。
- 当前结构近似 `rho=F(x,z,detailLOD)*H(y)`，所有XZ位置共用相同的bottom/top/middle，故云顶趋于齐平。thickness 只是高度剖面幂，不是每列几何厚度。
- 当前 DensityLOD 的低频和不同数量正值高频相加，LOD既影响密度均值也影响轮廓；新宏观高度不能从该混合输出直接生成。
- 当前累计使用 `alpha=rho`，没有世界步长，小局部密度会被长路径积分至接近不透明。新高度场本身不解决此问题。

## 建议数据流与职责

```text
XZ坐标/统一风场 ─→ 固定宏观噪声 ─→ 连续列密度 C ─→ 局部云顶
                                                    ↓
当前Y ──────────────────────────────────────→ 局部高度权重 H
细节噪声(detailLOD) ─────────────────────────→ 内部细节 E
Circle边界 ─────────────────────────────────→ 域权重 B
最终 rho = C × H × E × B ─→ 步长一致的透明度 ─→ 现有散射/合成
```

1. **全局体积与域边界。** `height/size` 定义最大体积。默认固定云底 `y0=centerY-sizeY/2`，最高允许云顶为 `y0+sizeY`；Circle 只作全局XZ域的平滑收口。AABB仍为最大包围。固定云底、只让云顶起伏是本计划的暂定设计假设。
2. **稳定的XZ列密度。** 新入口 `SampleColumnDensity(float2 xz)` 只取固定低频形状，不依赖当前Y、每段ray的detailLOD或质量档。复用现有Voronoi R贴图与统一风坐标，不增Weather贴图或3D贴图。建议 `u=saturate((macroNoise-coverageThreshold)/(1-coverageThreshold))` 后用保留完整值域的连续曲线得到 `C`；threshold=1明确返回空。避免使用很窄的smoothstep把绝大部分非空列直接饱和为1，否则仍会齐高。
3. **先定高度，再定点密度。** `heightFactor=lerp(1,HeightCurve(C),HeightVariation)`，`localTop=y0+sizeY*heightFactor`；第一版以单调线性曲线为基线、HeightVariation=1，C=0直接空，C增大则云顶升高。随后 `h=(y-y0)/(localTop-y0)`，在局部区间外返回0，局部区间内计算连续高度权重H。低C列不仅更稀，还更矮；高C列更高、更饱满。不能用最终rho反推localTop，否则形成相互依赖。
4. **高度剖面与内部细节。** middle解释为每列局部高度区间中的相对峰位，thickness仍只控制丰满度。新H的上下端连续归0。高频独立为E，只调制/侵蚀内部浓度，不参与localTop；以稳定底值和归一化权重组织LOD，避免均值随层数系统性漂移。首版仍用2D噪声；随高度错位细节UV作为后续有界增强，不把完整3D噪声列为前置条件。
5. **统一密度入口。** 私有 `CloudColumn` 保存C/bottom/top，`BuildCloudColumn(xz)`、`EvaluateHeightProfile(y,column)`、`SampleCloudDetail(p,lod)` 最后由 `EvaluateCloudDensity(p,lod)` 编排。视线和光照探针均调用同一入口；低频结果在本次求值中复用，避免拆函数后重复采同一层噪声。`rho=C*H*E*B` 不再追加最终硬threshold；coverage在列密度入口连续重映射。
6. **密度与积分分离。** rho只是归一化介质分布，全局光学浓度由sigma控制，`stepAlpha=1-exp(-sigma*rho*stepLength)`，再乘剩余透射率合成。采样步长来自裁到场景深度的可见云段；光照探针距离使用独立世界尺度，移除20×step耦合。保留当前双色、双HG和环境光路径，首版不重写多次散射或增加太阳光线步进。

Circle域B暂不参与云顶计算，职责是边界淡出；用于决定高度的是稳定宏观列密度C，而不是包含边界/细节/高度权重的最终rho。若需域边缘连云顶一起压低，后续再对照 `HeightCurve(C*B)`，不能在本版隐式混入。

## 参数与迁移计划

| 职责 | 建议参数/现有来源 | 约束 |
|---|---|---|
| 最大空间范围 | 现有height与size | size.y是最大厚度，height仍是最大体积中心高度 |
| 分布与运动 | 现有scale/speed/rotation/纹理 | 宏观形状使用统一风坐标；细节不能反向驱动高度 |
| 云量 | Coverage，来自原threshold的重新定义 | 连续列密度重映射；不是原硬截断的简单改名 |
| 高度起伏 | Height Variation，0统一最大高度、1完全由C驱动 | 提供明确消融对照，默认方案取1；HeightCurve首版不额外堆面板参数 |
| 高度剖面 | middle/thickness对应局部峰位/丰满度 | 迁移后按局部列高度解释，需重新校准画面 |
| 光学浓度 | Density对应sigma | 与高度响应分开，控制透过率而非云顶 |
| 细节 | Detail/Erosion强度 | 仅改变E，不改变C/localTop |
| 性能 | 现有Low/Medium/High | 宏观C与localTop不随档位变化；细节与积分预算可变 |

旧值迁移是后续实施的一部分；不把现有Renderer资产自动保存为新语义，不承诺逐像素保旧。

## 建议实施顺序与验收

1. **先列密度与局部云顶。** 固定时间/风，检查C和localTop调试输出；用固定C=0/.25/.5/1验证空/低/中/高云柱。先允许替换旧Height函数，光照路径不扩展。
2. **再组合高度与细节。** 检查rho、XZ剖面和侧视轮廓；关闭E时仍应有高低云团；开E不能移动宏观localTop。质量档切换及ray内LOD分段不能造成云顶台阶。
3. **接入步长一致积分。** 对均匀介质用解析透射率作控制；32/64/128步的整体透明度应接近一致。检查视线/光照使用同一密度定义，以及Circle域外、局部云顶外、全局体积外探针为0。
4. **最后校准参数与画面。** 保持固定相机/纹理/时间，对比旧平顶与新列高度；确认稀云低、浓云高、云顶连续、边缘可见软化，再整理参数迁移与成本。首轮不加新RT/Pass/天气贴图，不扩大为阴影重写。

计划涉及后续 `SSC.shader` 的私有函数与 `SSCFeature.cs` 的参数接线；正式密度重设计尚未获本轮实施指令。当前Circle试验的规范检查及Metal两Pass×四关键字编译通过，Shader消息为0；未做新密度结构实现或视觉验证。
