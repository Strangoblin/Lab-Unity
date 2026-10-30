# SSGI 快路径与 AO 历史，2026-10-27

用户批准先做三项优化，不做历史闲置回收；Diffuse HiZ 留在下一阶段。保留会话前 SSGI、场景及其他脏改动，未提交。

- AO 依据钳制后的模式、半径、bias、角偏移、深度滤波、档位与独立 falloff 建立历史签名；深度布局和缩放逐通道检查。只失效该路，不因强度或非零 blend 变化失效；独立 AO 请求运动向量。
- 采样帧计数与历史 ping-pong 分离；时缓 0 跳过该路 history resolve、失效旧颜色但保留已分配纹理，所有路均为 0 时省去深度写入。三路强度全关跳过统一 Pass，并使该相机旧历史失效，避免快速重开读到不连续历史。
- Diffuse 在射线循环外准备 `SST_CreateTraceContext` 和 `SST_CreateTraceConfig`；每条射线保留公共几何输入验证。`SST_Trace` 原入口与历史 RAY 对照仍可用。

Unity 6000.3.14f1 / M2 Metal：编译通过；临时 Renderer 真实相机回归 94 组、1,153,554 断言，五个负控制均检测到注入错误，覆盖多相机、混合通道、零时缓、重开、AO 独立 falloff、其他 Feature 输入。现有检查不代表 XR、动态有效区、所有场景或人工运动画质验收。

固定相机、321×179 下新旧追踪准备 RAY/DDA × Low/Medium/High × Trace/Confidence 共 12 组，输入帧号一致、容差 0.0005 内通过；注入错误负控制通过。旧 Shader 与适配器和 [原始报告](evidence/2026-10-27-ssgi-fast-paths/comparison.json) 在同目录。补充的旧 RAY 验证兼容宏不改变生产追踪分支。

性能为临时独立 Diffuse 克隆、DDA、时缓 0.5，654×393 与 1920×1080、Medium/High，各块 8 次预热、12 次采样、三轮交错。整次 SubmitRenderRequest + 1×1 ReadPixels 同步墙钟不是纯 GPU 或单 Pass；块中位数随轮次变化明显，部分方向反转，不能分辨追踪准备的性能收益，不能宣称提速。生产源码中显式共享准备是结构性去重，编译器也可能已执行相似优化。

原场景和原 Renderer 设置由比较器检查并恢复；临时 fixture 由 runner 清理。Editor 保持开启。后续 HiZ 应独立设计和验收深度金字塔构建、读依赖和性能收益，不将本轮数据当成 HiZ 基线。
