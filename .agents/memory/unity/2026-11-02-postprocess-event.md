# 后处理 Event 面板（2026-11-02）

所有后处理 `ScriptableRendererFeature` 的面板在 `Debug` 之前统一加 `[Header("Event")] public RenderPassEvent passEvent`，默认值**逐字段等于改造前硬编码的事件**，因此资产默认行为不变。

## 接线方式

- `Settings` 里声明字段（不叫 `renderPassEvent`，避免与基类属性同名）
- Pass 构造函数用它初始化（兼作回退），`AddRenderPasses` **每帧**再赋一次 `pass.renderPassEvent = settings.passEvent` 后 `EnqueuePass` —— 只读一次会导致运行时改面板无效
- 多 Pass Feature 只开放主 Pass；辅助 Pass 跟随主 Pass 或保持固定（见下）

## 默认事件

| Feature | 默认 |
|---|---|
| SSGI / DDOF / SSL | `BeforeRenderingPostProcessing` (550) |
| SSC / SSSM / PCSS / POSS | `AfterRenderingTransparents` (450；PCSS/POSS 的 resolve 在 500) |
| SSO | `AfterRenderingOpaques` (300) |
| MaskBlur / MaskRBR | `AfterRenderingSkybox` (400) |
| ScreenWarp / Weather / Kuwahara / SNN | `AfterRenderingPostProcessing` (600) |

组合 Feature 的特例：

- **PCSS / POSS**：阴影投射 Pass 固定 `AfterRenderingShadows`，只有 resolve 主 Pass 受面板控制（tooltip 已写明）
- **TransparentDepthMerge**：debug Pass 跟随合并 Pass；合并 Pass 事件（默认 500）即决定 SSGI 等下游读到的是未合并还是合并后的深度

## SSGI 特例

三份光照（AO / Diffuse / Specular）共用**一个** `passEvent`。按模块拆成三个入队 Pass 实现过，代价是每模块一份时域滤波（`frameIndex` 跳帧、深度历史写三遍）和 HiZ 金字塔按 `SourceRevision` 分裂，用户判定不值已回退。详见 [透明深度合并](2026-11-01-transparent-depth-merge.md)。

## 已同步的模板与规范

- `.agents/skills/auto-manager/templates/shader/postprocess/urp-renderpass.cs`：Pass 构造改为吃 `Settings`，末尾附 Feature 侧 Settings + AddRenderPasses 骨架
- `.agents/knowledge/unity/standard/script/parameter-panel.md`：分层表新增 `Event` 行（仅 RendererFeature，位于 `Debug` 之前）
- `.agents/knowledge/unity/shader/postprocess/{feature-script-structure,fullscreen-structure}.md`：规范条目与 C# 骨架同步

## 未纳入

`DebugOutputFeature`（调试工具）、`PickerFeature`（选择器）不是后处理；SSGI 的独立 `AOFeature` / `DiffuseGIFeature` / `SpecularGIFeature` 与其共用同一份 `Settings` 类型，加字段会重新出现在 SSGI 总面板里，故保持硬编码。
