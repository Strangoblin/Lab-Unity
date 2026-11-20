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

## 2026-11-20 补：layerMask 与 passEvent 并列

带对象遮罩来源的后处理 Feature（当前 MaskBlur、MaskRBR）把 `layerMask` 从 `Technical` 移入 `Event` 组，与 `passEvent` 上下相邻：`layerMask` 定空间范围（谁参与），`passEvent` 定帧内时机（何时执行），两者共同构成效果的适用范围，且都以「静默失效」为失败模式。两者都补了 tooltip；`layerMask == 0` 时在 `AddRenderPasses` / 录制阶段进入该状态警告一次（修好后重新武装）。

配套改了两份 README（参数表新增 `Event` 行 + 适用范围说明）与 `.agents/knowledge/unity/standard/script/parameter-panel.md`。`Special/Renderer/` 下的 ColoredShadow / TransparentDepthMerge 仍把 `layerMask` 放在 `Technical`，本次未迁移。

**MaskRBR 的 source 与 layerMask 不冲突，是从属关系**：`source == Sky` 时 CPU 侧把层掩码强制置 0（`sourceMode == Sky ? 0 : layerMask.value`），改 `layerMask` 不改变任何输出。但 `ScreenMaskRenderer.RecordObjects` 的契约是「空对象列表仍执行清零」且 `AllowPassCulling(false)`，所以 Sky 模式每帧仍分配并清黑一张全屏 R8——无收益的固定开销，要省掉需让空层掩码不录制对象遮罩、并给主遮罩 Pass 换一个合法 Blit 源纹理。`source = Objects` + `layerMask = Nothing`（默认值）是静默全黑，已加警告。两效果默认值刻意不对称：MaskBlur `Everything`（材质必须自带 `MaskBlur` Pass 才参与）、MaskRBR `Nothing`（覆盖材质会把整屏不透明物当光源）。

## 未纳入

`DebugOutputFeature`（调试工具）、`PickerFeature`（选择器）不是后处理；SSGI 的独立 `AOFeature` / `DiffuseGIFeature` / `SpecularGIFeature` 与其共用同一份 `Settings` 类型，加字段会重新出现在 SSGI 总面板里，故保持硬编码。
