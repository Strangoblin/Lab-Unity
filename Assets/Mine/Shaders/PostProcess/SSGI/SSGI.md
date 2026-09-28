# SSGI — AO、DiffuseGI 与 SpecularGI 统一管线

入口为 `SSGIFeature.cs`，Renderer 中只需启用一个 SSGI Feature。现有 AO、DiffuseGI、SpecularGI Feature 保留独立运行能力，供对照和回退；同一 Renderer 不应同时启用独立 Feature 与统一 Feature，否则效果会重复合成。

## 帧内顺序

`AfterRenderingSkybox` 获取不透明场景色、深度、法线和运动向量；三个模块都从同一场景色读取，不从其它模块已合成的颜色再次追踪。

```text
Opaque + Skybox
  ├─ AO: Trace → 双边滤波 → 全分辨率保边 Resolve
  ├─ DiffuseGI: Gather → 双边滤波 → 全分辨率 Resolve
  └─ SpecularGI: SSR → SSPR → Cubemap → 空域 → 时域
              ↓
        SSGI.Composite（写回 cameraColor 一次）
              ↓
        URP 拷贝 _CameraOpaqueTexture → 透明物体
```

RenderGraph 在统一 Feature 内直接传递三张 `TextureHandle`，合成 Pass 对每张实际启用的纹理声明读依赖。SpecularGI 保留每相机的双缓冲历史；没有 Cubemap 时环境回退为黑色，不使用 `SampleSH`。

## 合成语义

AO 输出可见度 `V`，DiffuseGI 输出尚未乘接收反照率和强度的间接辐亮度近似，SpecularGI 输出空间与时间滤波后的反射辐亮度。统一 Shader 按以下顺序合成：

```text
AOFactor = 1 - saturate((1 - V) × AOIntensity)
Color    = Scene × lerp(1, AOFactor, SceneAO)
Color   += DiffuseGI × ReceiverAlbedo × DiffuseIntensity × AOFactor
Color    = lerp(Color, SpecularGI, saturate(SpecularIntensity × Fresnel))
```

`SceneAO` 默认 1，延续原 AO 对整个场景色的近似遮蔽；设为 0 可保留原场景色，只让 AO 调制新增 DiffuseGI。Forward 后处理无法拆出场景色中的直接光与环境光，因此这里不是严格的“仅遮蔽间接光”。SpecularGI 沿用原有 Fresnel 混合，不把反射辐亮度当成无条件加法项。

## Renderer 配置与调试

`Assets/Settings/PC_Renderer.asset` 已启用统一 SSGI，AO 和 DiffuseGI 使用各自 Medium 档；SpecularGI 参数从原 Feature 复制，旧 SpecularGI Feature 关闭但保留。统一 Debug 提供 AO、DiffuseGI、SpecularGI 三种分量视图；各子模块原有 Debug 字段只在独立 Feature 中使用。

模块开关影响是否录制对应计算 Pass。Quality 决定固定采样预算和工作分辨率；AO 世界半径只控制遮蔽覆盖，不改变循环次数。

## 验证与限制

2026-09-28 在 SampleScene 的 Main Camera 上，以同一个 256×144 离屏目标逐项切换模块，较全关基线的采样像素差异为 AO 1208、DiffuseGI 146、SpecularGI 1778、全开 1808；四个 Shader 强制导入均为 0 消息，Play Mode 运行无错误日志。该测试证明三路已接入真实管线，不代替 Game View 的主观画质检查。

当前 SpecularGI 的时域阶段仍使用原有 Unsafe Pass；统一 Composite 使用一个全屏 Raster Pass。后续性能分析先检查 SpecularGI 的中间 RT 与 Unsafe Pass 范围，再考虑压缩或合并；DiffuseGI/AO 尚无时域历史。透明物体在合成后绘制，因此不会作为屏幕追踪的命中几何。
