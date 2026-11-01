# ScreenWarp 自定义屏幕遮盖 — 2026-10-19

## 用户意图与范围

用户已确认首版测试没有问题，指出屏幕外像素为黑色，偏向使用自定义素材在上层遮盖。沿用本会话已授权的 `Assets/Mine/Shaders/PostProcess/ScreenWarp/` 工作区，在首版 `603b129` 上扩展；保持独立 ScreenWarp，不接入深度重投影、相机参数或处理器框架。

## 已交付契约

- `Settings.overlayTexture` 为借用的普通 straight RGBA Texture2D，`overlayOpacity` 默认 1；贴图为空时有效不透明度为 0，兼容原配置。Feature 不创建、修改或销毁用户贴图。
- 遮盖在场景逆采样及 Border 填色之后，按原始输出逻辑 UV 铺满全屏，不随 warp 变形，也不受源 RTHandle padding/scaleBias 影响。全屏 Alpha 素材允许重叠有效画面来掩盖接缝，不只裁在越界像素内。
- 私有 `ScreenWarp_CompositeOverlay` 实现 straight RGBA source-over，正确保留非恒定底图 Alpha/HDR；Alpha 为 0 直接返回底图。输入 Alpha 的契约是覆盖率 `[0,1]`。
- 变形 Strength 与 Overlay Opacity 独立：零变形仍可显示素材；两者关闭且 Debug Off 才跳过。SourceUV/OutOfBounds 调试绕过遮盖。
- 维持同一 Raster RenderGraph Pass 和颜色输出；启用遮盖多一次 2D 采样，不新增 Pass/RT，不修改 Camera/depth/normal/motion。
- 未提供正式美术素材，因此槽位留空；未创建替代素材或改存量 PC_Renderer 配置。建议 PNG 中心透明、边缘不透明，按目标比例制作并保留导入 Alpha。

## 复用与协作

- BlendFunction 只有普通 RGB 混合，Weather 粒子混合也不满足一般 straight RGBA source-over；私有函数留在本效果 HLSL，未扩大共享库。
- 纹理引用/参数快照沿现有 Feature 约定，实际执行回调绑定。独立只读复核未发现确定缺陷。
- OpenCode `opencode-go/deepseek-v4.1-flash` 完成隔离 README 草稿，exit 0；主会话完整审阅并修正填色/叠加顺序、资源所有权及零强度措辞，再经门禁合入。worker 未写受管源码。
- 管线验证 worker 只修改 staging 测试脚本；主会话审阅 diff，运行实际 Unity 并归档复跑。

## 实际验证

- Unity 6000.3.14f1 / URP 17.3.0 / Apple M2 Metal；刷新编译成功。
- GPU：752 组 / 3,741,696 像素，0 失败，最大 RGBA 绝对误差 `3.629922866821289e-5`；包含新增 320 组遮盖、376 组 padded 源、两个检出的负控制。涵盖独立输出 UV、横竖屏、四模式、零/满变形、Alpha/不透明度端点与中间值、HDR/底图覆盖率、Debug 绕过。
- 真实 URP：52 请求 / 632 断言，0 渲染错误；基础 30 请求保持，新增遮盖 22 请求 / 270 断言。透明中心保持变形后 RGBA、边框固定、零强度独立显示、空贴图/opacity=0 保持底图、调试不被遮挡均通过。
- 两个验证脚本已归原功能 `Validation~/`，从归档入口实际复跑，结果相同；横竖屏完整 PNG 已查看，临时方向素材仅作可重建验证输入。
- 原 Renderer 数组/实例与开关、场景列表/dirty、活动 RT 恢复检查通过；未保存场景或调用 SaveAssets。用户 SampleScene 与四个未跟踪 HDR 文件保持；PC_Renderer 文件无本轮 diff。
- 五个源码/验证文件全量 norms 检查无 error/warning，git diff --check 通过。
- 最小报告：[Shader](evidence/2026-10-19-screenwarp-overlay/shader-checks.json)、[URP](evidence/2026-10-19-screenwarp-overlay/pipeline-checks.json)。本次输出/PNG/委派日志在 `tmp/sessions/2026-10-19-screenwarp-overlay-b72c/output/`，草稿在同任务 staging/fixtures；非长期依赖，不进 Git。

## 边界

当前黑边属于逆采样越界。上层边框可遮盖这些区域，但未来深度重投影的内部遮挡空洞需要另行处理。未验证 Camera Stack、XR、硬件动态分辨率、其他 Feature 组合、GPU 耗时或用户正式素材的艺术效果；保持首版对应边界。历史首版记录与证据不覆盖。

新增记录/证据供后续行为回归与诊断使用；验证源码为重建入口，无外部二进制 fixture 依赖。后续若整体退役本功能，再按存量清理规则评估归档，不把临时输出当生产素材。
