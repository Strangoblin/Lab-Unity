# Unity Developer — Memory Index

> 按日期命名的 memory 文件索引。新 memory 以 `YYYY-MM-DD-<slug>.md` 格式添加。

---

## 技术栈（不变式）

- Unity 6 + URP 17+，macOS Metal
- 自动化：unityctl（Editor 远程控制）、Roslyn（C# 运行时注入）
- UnityCtl 宿主边界：bridge 与 Editor 必须处于同一执行环境；沙盒 bridge 与真实宿主 bridge/Editor 不互通，以 `Unity connected` + `unityctl wait` 为连接证据
- 清理/整理回退偏好：只使用 Git（精确 pathspec 的 stash/历史），不再生成压缩备份包；永久禁止 `git stash --all`

## 关键架构决策

- Shader 参数端点策略：统一在参数入口钳制合法范围，避免逐计算追加冗余保护；尚未统一实施时明确记录待办。见 [2026-09-18-parameter-range-policy.md](2026-09-18-parameter-range-policy.md)。

- PCSS 阴影：PSSM 4-cascade split → tiled atlas → blocker search → penumbra → variable PCF
- POSS 逐物体软阴影：Shadow Atlas Tile Grid → Compute 屏幕空间解算 → PCF 软边缘，与 CSM 共存
- 交互系统：Manager/Processor 分离架构，RT 管理下放，正交相机 CustomRenderer 深度比较输入
- Shader 组织：`Assets/Mine/Shaders/` 按效果分层
- 后处理：Unity 6 Blitter API（`_BlitTexture`，非 `_MainTex`）
- 知识库分类：按实际职责区分 `standard/`、`script/`、`shader/postprocess/`、`shader/render/`、`shader/hlsl/`（`particle/` 占位）；特征族目录可混合 `.cs/.shader/.compute/.hlsl`，但只有 `README.md` 是 markdown；`shader/hlsl/` 对应 `Assets/Mine/Special/HLSL` 共享库，单效果私有库与其 shader 同目录

---

## Memory 文件

| 文件 | 日期 | 摘要 |
|------|------|------|
| [2026-09-18-parameter-range-policy.md](2026-09-18-parameter-range-policy.md) | 2026-09-18 | 参数 0/1 端点风险统一入口钳制，原型阶段避免逐点冗余保护；Snowy 纹理风效 |
| [2026-09-17-pbrtoon-urp17.md](2026-09-17-pbrtoon-urp17.md) | 2026-09-17 | PBRToon 五 Pass 实例化/Stereo 与 URP 17 结构迁移，保留原材质算法 |
| [2026-07-24-pcss-integration.md](2026-07-24-pcss-integration.md) | 2026-07-24 | PCSS 软阴影完整方案 |
| [2026-07-30-interaction-system.md](2026-07-30-interaction-system.md) | 2026-07-30 | 正交交互系统：Manager/Processor 架构 + Verlet 波方程 + 移动域重投影 |
| [2026-07-30-fgd-lut-baker.md](2026-07-30-fgd-lut-baker.md) | 2026-07-30 | FGD LUT 烘焙工具 + ENVFunction 合并 + _UseFGDLut 自动检测 |
| [2026-08-06-poss-per-object-soft-shadow.md](2026-08-06-poss-per-object-soft-shadow.md) | 2026-08-06 | POSS 逐物体软阴影：重构 + PCF + 极简组件，ShaderTagId 废弃改用专用 ShadowCaster 材质 |
| [2026-08-04-water-caustics-screen-independent.md](2026-08-04-water-caustics-screen-independent.md) | 2026-08-04 | 水体焦散屏幕无关化：ddx/ddy 基底分析 + 常数 sceneDet + 世界空间 corrDet |
| [2026-08-07-ssr-dda-fisheye-w-sign-flip.md](2026-08-07-ssr-dda-fisheye-w-sign-flip.md) | 2026-08-07 | SSR DDA 鱼眼扭曲根因：近平面 w 过零导致齐次插值奇异，DDA/Ray3D 盲区互补 |
| [2026-08-25-feature-screen-debug.md](2026-08-25-feature-screen-debug.md) | 2026-08-25 | RendererFeature 屏幕调试：真实场景中间 RT 输出 + Game View 观察，替代 Probe/Pixel Probe |
| [2026-08-27-starrynight-angle-field.md](2026-08-27-starrynight-angle-field.md) | 2026-08-27 | 多中心角场拓扑（N 源必断裂）+ 圆周值/周期消费配方 + 四种融合方案对比 + StarryNight 三文件拆分 |
| [2026-09-02-starrynight-wheat-bands.md](2026-09-02-starrynight-wheat-bands.md) | 2026-09-02 | TheStarryNight 麦田前景收尾：多层横带 + 循环位移回卷 + SKY/MOUNT/WHEAT 三分类命名 |
| [2026-09-03-editor-window-family-ruling.md](2026-09-03-editor-window-family-ruling.md) | 2026-09-03 | 窗口壳归属裁定(更正)：editor-baker-window.cs 从未落盘，EditorWindow 壳归 window/ 家族非 baker |
| [2026-09-03-standard-code-window-family.md](2026-09-03-standard-code-window-family.md) | 2026-09-03 | standard 补代码框架(standard-shader.shader/standard-script.cs) + renderpass 双变体合并 + postprocess 文档并入 README + window 家族新开 |
| [2026-09-03-generator-unification.md](2026-09-03-generator-unification.md) | 2026-09-03 | Curve/Noise Generator 统一：CurveBake 共享骨架去重 + C2 余数/空结果修复 + period 死参清理 + PackChannels 下沉 + 窗口迁工具目录 Editor/ |
| [2026-09-03-render-hlsl-template-families.md](2026-09-03-render-hlsl-template-families.md) | 2026-09-03 | render 家族落地(直写单 Pass + 复杂材质对) + hlsl 新家族：Special/HLSL 共享库三档依赖决策表 + 私有/共享拆库裁决 |
| [2026-09-04-interior-mapping-archive.md](2026-09-04-interior-mapping-archive.md) | 2026-09-04 | InteriorMapping + InteriorMap Baker 完成归档：正式产物、方向/投影约定、验证证据与迁移/清理结果 |
| [2026-09-17-uv-aspect-anisotropy.md](2026-09-17-uv-aspect-anisotropy.md) | 2026-09-17 | UV 空间各向异性：屏幕 UV 内做 R/S 必然剪切/拉伸 + 逆映射语义 + TRS2D_InverseTransformUV |
