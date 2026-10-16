# Generator Templates

**定位**:从参数/场景输入生成**可再生**资产(噪声纹理、曲线数据、网格等)的编辑器工具。改参数随时重出,结果通用、可反复 Save。

**族内结构**:核心层 [generator-facade.cs](generator-facade.cs)(静态 facade 骨架)+ 窗口壳 →
复制 [production-tool/](../production-tool/README.md) 完整模板，按 Generate 形态(`ShowBakeProgress => false`)
在 staging 替换业务占位符，形成独立 `EditorWindow`；所需流程为窗口类内成员，项目不安装共享基类。

**骨架要点**(CurveGenerator / NoiseGenerator 为项目范例，通用生成契约以 production-tool 为准):
- 核心 = 静态 facade 类(如 `NoiseGenerator`)提供生成 API(Editor 与 Runtime 均可调),窗口只做瘦壳
- 窗口归位 `category.Script` 解析且已授权目录下的 `<Tool>/Editor/`,与 facade 同目录;配套中文 `.md` 文档同目录
- 窗口脊柱见 [window/](../window/README.md):标题 → 设置 → Output 路径行 + Browse → `[Generate][Load][Save(DisabledScope)]` → Preview/Info
- Generator 特有:预览带 hash 缓存(参数不变拖动 slice 不重建)；Load 用于回看已存资产，预览缓存不能替换已载入资产
- 窗口字段 `_` 前缀 + `[SerializeField]`;UI 文案英文;Save 与 Load 共用窗口本地路径字段，默认值按当前任务授权绑定，不设历史项目默认值
- 曲线类数据消费者走 `CurveAsset` 类 ScriptableObject(可 `CreateAssetMenu`)

**历史项目范例（仅溯源，当前地址经资源接口解析）**:

| 范例 | 路径 | 学习点 |
|------|------|--------|
| NoiseGenerator | `Assets/Mine/Scripts/NoiseGenerator/` | facade(生成/打包/无缝)+ 窗口 + .md 完整标准形 |
| CurveGenerator | `Assets/Mine/Scripts/CurveGenerator/` | facade + 共享采样骨架 + parent-only 输入 + Scene 预览 |
| Grass Generator | `Assets/Editor/GrassGeneratorEditor.cs`(遗留位置) | 反例对照:窗口仍在 Assets/Editor,新工具勿仿 |

**与相邻族边界**:
- vs Baker:Generator 可再生、无生成进度条;Baker 一次性重烘焙见 [baker/](../baker/README.md)
- 纯数据服务(无窗口需求)不属于 Generator,按 [script-structure.md](../../../../../knowledge/unity/standard/script/script-structure.md) 组织即可
