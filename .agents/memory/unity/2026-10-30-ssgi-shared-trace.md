# SSGI 统一 Trace 与 Specular HiZ，2026-10-30

用户认为 Diffuse HiZ 有少量光线损失但可接受，要求尝试 Specular；随后明确要求 Diffuse / Specular 共用一个步进模式，将 Trace 提到统一面板外层。

统一 `SSGIFeature.Settings.trace` 提供 DDA / Ray / HiZ 和单一深度金字塔 Compute 引用，两路同步；独立 DiffuseGI / SpecularGI Feature 各有自己的同形 Trace 控件。初始化时旧统一设置以 Diffuse 的旧模式、实验 HiZ 和 Compute 为准；独立 Feature 分别迁移自身旧值。历史切换作用于实际受影响的 GI 通道，AO 不变。旧分量后端字段留作隐藏的序列化迁移来源，不再供统一入口分别控制。

`SSGIDepthPyramid` 提炼 Diffuse 原有请求与布局保护，复用公共深度金字塔服务；统一双路同相机同阶段相同请求可共享一次构建，独立 Specular 在透明物后使用独立阶段标识。缺资源、不支持的深度布局退回 DDA；HiZ 正常 miss 不重跑其他后端，Specular 仍回退 Unity 默认环境反射图。HiZ 是非保守追踪，可能损失薄反射或改变画面；2026-10-30 起改为像素步幅（mip `m` 前进 `2^m` 像素、回退细分、预算 `2 × maxSteps`）。

2026-10-28 Editor 在线时：统一配置迁移后的实际内存设置为 HiZ 且保有已有 Compute 引用；独立与统一 Specular HiZ 检查 1,724,259 条断言通过，覆盖三档、回退、Unity 环境参考、双 GI 历史及粗层深度故障注入；额外同帧探针的 44 条断言确认双路共用一份缓存、单路一份、DDA/Ray 零份。其后补充的独立迁移用例和迁移后的完整回归尚未在 Editor 执行。

2026-10-28 的 M2 / Metal 隔离 SSGI 克隆，比较 Specular 单路以及 Diffuse+Specular 同步模式，两个尺寸、Medium/High、各 3 轮 / 12 次同步读取；部分结果方向相反或与块间波动相当，**未分辨稳定提速**。原始 48 块见 [计时报告](evidence/2026-10-30-ssgi-shared-trace/timing.json)；它包含整次请求、回读与金字塔构建，不是纯 GPU 时间，也不证明等画质。

2026-10-30 用户选择先完成离线工作，Unity 已关闭。当前生产 C#、包含新验证 partial 的临时项目以及生成的 High 对照均经 dotnet 编译通过（无错误，3 个既有其他模块警告）；计时检查器对旧格式、新格式、单/双分辨率及错误组合等合成报告通过。最后修改后的 Shader 导入、完整 Unity 回归与人工动态画质仍待 Editor 恢复后验证，不能当成已完成的最终 GPU 验收。当前工作保留全部既有脏改动，未提交。
