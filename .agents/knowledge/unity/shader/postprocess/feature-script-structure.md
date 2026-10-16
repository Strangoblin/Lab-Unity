# Postprocess Feature Script Structure

> `ScriptableRendererFeature` + `ScriptableRenderPass` guidance for shader/postprocess features.
> Shared MonoBehaviour and utility conventions: [standard script structure](../../standard/script/script-structure.md).

## Feature / RenderPass 脚本结构参考

适用于 `ScriptableRendererFeature` + `ScriptableRenderPass` 组合的后处理脚本。

### 1. 整体布局

完整骨架复用 [urp-renderpass.cs](../../../../skills/auto-manager/templates/shader/postprocess/urp-renderpass.cs)：Feature 的 Settings 与生命周期、PassData、RecordRenderGraph 的资源 / Pass 编排各自分层。参数分组见 [parameter-panel.md](../../standard/script/parameter-panel.md)。

### 2. 关键规范

- `PropertyToID` 在 `Settings` 内以 `static readonly int` 预计算
- 每个 Feature 配一个 Debug 开关，控制中间结果可视化
- `ConfigureInput` 声明所需纹理（Color / Depth / Normal）
- RenderGraph 模式下使用 `UnsafePass` + `SetRenderFunc` + `PassData`

### 3. 文件组织

```
Assets/Mine/Shaders/XxxPostEffect/
  ├── XxxPostEffect.shader       — Shader 代码
  ├── XxxFeature.cs              — C# Feature + Pass
  └── XxxPostEffect.md           — 技术文档
```

### 4. 屏幕调试方法（RendererFeature 屏幕调试）

统一流程见 [unity-render-debug](../../../../skills/unity-render-debug/SKILL.md)：实际 Feature 中间 RT / 固定 DebugOutputFeature → camera color → Game View；需要完整固定尺寸图时，经相机请求 RT 回读为 PNG。

Feature 提供可序列化 Debug 开关并复用本帧实际中间资源；独立材质复用已有调试 Feature。相机、输入、恢复、截图及判读约定只在该 skill 维护。
