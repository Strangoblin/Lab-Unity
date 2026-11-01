# PBRFunction 拆分与代表模型补充（2026-10-30）

按要求把毛发部分从 `Assets/Mine/Special/HLSL/PBRFunction.hlsl` 拆成独立库，并在原库补入有代表性的漫反射 / 高光模型。

## 变更

- 新增 `Assets/Mine/Special/HLSL/HairFunction.hlsl`（guard `HAIRFUNCTION_HLSL_INCLUDED`），承接 `Hair_ShiftedTdotH` / `Hair_Specular` / `Hair_Lobe` / `BRDF_Hair`，函数体与签名逐字未改。
- `PBRFunction.hlsl` 删除 Hair 段，文件头注明已拆出，guard 不变。
- 唯一调用方 `Assets/Mine/Shaders/Render/Fur/ShellInstanced.shader` 改 include HairFunction；仓库内无其他文件调用 Hair 系列。
- 新增函数：`Diff_OrenNayar` / `Diff_Minnaert` / `Diff_AshikhminShirley` / `Diff_Wrap`；`Spec_D_BlinnPhong` / `Spec_D_Beckmann` / `Spec_D_GTR1` / `Spec_D_Charlie`；`Spec_V_SmithCorrelated` / `Spec_V_Kelemen` / `Spec_V_Neubelt`；`F_SchlickF90`；`BRDF_OrenNayar` / `BRDF_Sheen`。

## 约定（新函数）

- 除 Charlie 外，所有 D 项沿用库内既有约定：参数为 `alpha = perceptualRoughness²`。
- `Spec_D_Charlie` 的第二个参数是**感知粗糙度**（`invAlpha = 1/roughness`），与原式一致，**不要再平方**；与同库其他 D 项语义相反，是刻意保留的陷阱点。
- `Diff_OrenNayar` 的 `sigma` 是坡角标准差（弧度，0..π/2），不是 roughness；常用映射 `roughness * PI/2`。实现为去掉三角函数的点积形式：`A + B * max(VdotL - NdotL*NdotV, 0) / max(NdotL, NdotV)`。
- `Spec_V_SmithCorrelated`（Heitz 2014 高度相关）已含 `1/(4 NdotV NdotL)`，与 `Spec_CK` 同量纲，可 `Spec_D_GGX * Spec_V_SmithCorrelated * F` 直接替换 `Spec_CK * F`。
- `Spec_D_GTR1` 在 `alpha² ≈ 1` 时返回 `1/PI`（均匀分布极限），避免 `log(1)=0` 的除零。

## 家族聚合（同日追加）

- `PBRFunction.hlsl` 作为 PBR 家族入口，在自身 guard 内 include `HairFunction.hlsl` 与 `FabricFunction.hlsl`，调用方只引 PBRFunction 即可用全部 PBR 模型。子库仍可独立 include，靠各自 guard 防重复展开。
- 这突破了原先的「零兄弟 include」家族铁律，因此同步改了知识库与技能模板：`knowledge/unity/standard/shader/shader-structure.md` 与 `skills/auto-manager/templates/shader/hlsl/README.md` 均记为唯一例外，其余共享库仍禁止兄弟 include。
- 未调用 PBR 家族子库的旧调用方（Water / PBRToon / ShellInstanced 等）保持原 include 不变；PBRToon 同时引 PBRFunction 与 FabricFunction，重复展开由 guard 吸收。
- 代价只有编译期文本量增大，未使用的函数不进最终变体。

## Smith 遮蔽修正（同日追加）

- `Spec_G_Smith` 第三参数由 `alpha = roughness²` 改为**感知粗糙度**，`k = (roughness+1)²/8` 才与 Schlick-GGX 的定义一致；三个库内调用点同步改为传各自感知粗糙度：`Spec_CK` 直接传 `roughness`，`Trans_GGX` 传 `sqrt(alpha)`（保住原 `alpha` 下限 0.001），`BSDF_Evaluate` 直接传 `roughness`。
- 量化影响：r=0.5 时 k 由 0.1953 升到 0.2812（+44%），r=0.2 时 +33%，r=1 不变；即 r<1 的表面掠射角遮蔽变强、边缘略暗。
- **无外部调用方**：仓库内 `Spec_G_Smith` / `Spec_CK` / `BRDF_Classic` / `BRDF_Burley` 均无 shader 调用，现有材质（PBRToon / Water 走的是 `Spec_Unity` + `F_Fast` + `Diff_Lambert`）观感不受影响。
- `Spec_G_SKSmith` 未改名（避免无谓改动），只订正注释为「Karis 移动端遮蔽近似，非 Smith-GGX 精确解」。

## 验证

- `python3 .agents/skills/agent-maintenance/scripts/check_api_refs.py Assets/Mine/Special/HLSL` 全部通过。
- `glslangValidator -D -S frag -e main -V` 以 stub prelude（`real`→`float`、补 `PI` 与 `SafeNormalize`）同时编译两个库并通过，所有新函数均在入口强制调用；Smith 修正后重跑 `Spec_G_Smith` / `Spec_CK` / `BRDF_*` / `BTDF_Classic` / `BSDF_Evaluate` 仍 exit 0。
- **未做** Unity / Metal 实际变体编译与画面验证；新函数尚无调用方。
