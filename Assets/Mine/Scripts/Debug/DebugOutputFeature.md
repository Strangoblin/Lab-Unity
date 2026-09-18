# DebugOutputFeature

通用全屏材质 / Shader 调试 RendererFeature：将 Shader 输出经 RenderGraph 临时 RT 写回真实相机颜色目标，先执行材质的第 0 个 Pass，再可选执行第二个 Pass。

## 输入

- **Debug Material**：优先使用指定材质，保留贴图、参数与关键字；直接引用，材质修改即时生效。
- **Debug Shader**：未指定材质时，创建使用此 Shader 默认参数的内部材质。
- **Additional Pass Index**：输出时追加的材质 Pass，默认 -1 为普通复制；非法索引同样回退复制。
- **Require Depth**：请求场景深度，并向 RenderGraph 声明两个阶段对深度的读取；需要深度的 Shader 必须开启。
- **Debug**：是否输出调试画面；两个输入都为空时不执行。

外部材质由调用方持有，Feature 不销毁它。内部材质在 Shader 切换、使用外部材质或 Feature 释放时销毁。运行时更换材质或 Shader 也会更新。

## 使用

在 Renderer Data 添加 DebugOutputFeature，指定 Debug Material（例如 Snowy 的材质）或 Debug Shader，开启 Feature Active 与 Debug，在 Game View 观察。纹理和参数直接在材质 Inspector 中设置。

Shader 应使用 URP Blit.hlsl 的 Vert、Varyings 和 _BlitTexture。Feature 请求可采样中间颜色纹理，输入当前场景色；最终画面是否保留场景由 Shader 决定。

验证顺序：刷新并检查 Shader 编译 → 检查实际相机与 Renderer Feature → Play Mode 查看日志和 Game View。临时验证后恢复 Debug 和 Active 原始状态。

Snowy 两阶段原型：Additional Pass Index=1、Require Depth=true。复用已有的输出 Blit 执行气氛 Pass，不增加颜色临时 RT。
