---
name: generator-unification
description: CurveGenerator × NoiseGenerator 两工具框架统一 — 共享骨架去重、死代码清理、窗口迁入工具目录 Editor/ 子目录
metadata:
  type: project
---

# Generator 统一 — 2026-09-03

## 本次做了什么

统一 `Assets/Mine/Scripts/CurveGenerator/` 与 `NoiseGenerator/` 两个编辑器的框架/排版/文案,并修功能缺陷。**脚本模板(Baker/Generator/Manager/Controller)本次不做**(另见同日 template 条目)。

- **CurveBake.cs**:采样循环 + 末尾点 + 法线/曲率块(~90 行)原先在 Bezier/CatmullRom 逐字重复 → 抽公共 `SampleUniform(pts, samples, closed, SegmentSampler)` + 嵌套 `delegate void SegmentSampler(...)`,曲线类只保留段公式。
- **C2 边界修复**:旧实现 `samplesPerSeg = samples / segCount` 丢弃余数,`samples < segCount`(闭合+多点+低采样)时直接空结果 → 均匀分配余数 + 每段保底 1 样本。冒烟:17 控制点闭合 + 16 samples → bezier/catmull = 17 点,总长 7.16(旧实现空/0)。
- **Noise facade**:`Sample3D` 的 `period` 是死参(全路径从未读) → 删除,连带删 Perlin/Voronoi 的 `SamplePeriodic` 与 `Generate2DSliceTexture`(零调用);2D 打包循环下沉为公共 `PackChannels(Texture2D[] sources, int resolution)`(窗口瘦壳化,保留"低分辨率源=有意低频模糊"语义)。
- **窗口迁移约定**:`Assets/Editor/<Window>.cs` → `Assets/Mine/Scripts/<Tool>/Editor/`(与代码同目录,参照 InteriorMapBaker/CustomRenderer 先例)。本环境**禁止对 Assets/Mine 的 Bash 写入/git mv** → 实际做法:write_gated 直写新路径(server.py 自动建目录)+ `git rm` 旧文件。GUID 不保留(meta 重建),EditorWindow 无资产 GUID 引用,可接受。
- 统一:两窗 `sealed` + `_` 前缀字段 + `// ═` 横幅 + Save 覆盖弹 `DisplayDialog` + UI 全英文;Noise 窗 749→~470 行,3D slice/packed 缓存 hash 从 `texture.name`(不可序列化)改为独立 int 字段 + `[NonSerialized] previewTexture`。

## 怎么做的(可复用流程)

1. 门禁链:gate_set_recipe("Production") → g_entry → g_knowledge(loaded_files 需含 "shader-structure.md" 高优文件)→ write_gated。
2. 编译联动风险:F4-F6(删 API)落地瞬间旧窗编译失败 → 同阶段写完新窗再一次性编译,禁止中途编译。
3. roslyn 冒烟:`unityctl script eval` 收多语句方法体需**显式 `return expr;` 结尾**(CS0161),不能隐式返回。

## 教训

- g_knowledge 拒绝 G15_MISSING_HIGH_PRIORITY:loaded_files 必须含 shader-structure.md(即便内容是 C# 结构规范)——knowledge 证据要求列真实可解析文件。
- write_gated 对 Assets/Editor/ 路径不支持 → 迁移只能新路径写 + git rm 旧路径,不能靠单次 git mv。
