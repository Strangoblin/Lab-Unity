# SSGI RAY / DDA 渲染计时

当前主线是旧 SSR 框架的增量 DDA，Medium 使用 Diffuse 48 / Specular 64 样本。当前测量与已退役逐格 DDA 的历史证据分开记录；退役源码不再保留。

## Diffuse High 性能再平衡（2026-10-15）

用户允许少量间接光细节取舍、优先明显提速。仅把 Diffuse High Trace 宽高从各 1/2 改为各 1/3，8 射线/64 步、两级空间重建与时域不变；Low/Medium、AO/Specular 不改。1080p 原生追踪像素从 960×540 降为 640×360，约减少 55.6%，不是把 High 直接降成 Medium。质量关键字固定循环预算的等价调整没有稳定分辨出额外性能收益；不强制循环展开。

先前配对诊断使用修正后的 Feature 复用策略，1080p 三轮中 Diffuse High 块中位数为 23.305/25.591/24.320 ms，Specular High 为 17.661/17.911/18.472 ms；同轮 Diffuse High 均较慢。低中档与背景波动重叠，不给稳定倍率。Diffuse 一像素追踪 4/6/8 条射线，Specular 一条；删 Trace 的方向性控制比删空间核更明显，因此本轮先缩小 High 追踪网格，而不是先移除抗抖滤波。早期逐块新建 Feature 留存旧 history 的诊断只作定位，不作为最终精确数字。

正式复跑在 Apple M2 / Metal、Unity 6000.3.14f1、当前 Main Camera、DDA、临时 Diffuse-only Renderer、Indirect Debug、时缓 0.5 下进行。基线由当前生产 Feature 仅改 High 除数为 2，候选使用正式除数 3；两支共用实际生产 Shader。每块预热 8 / 测量 12 请求，3 轮交错/反向。口径仍是 `SubmitRenderRequest → ReadPixels(1×1)` 同步整次请求墙钟，**不是纯 GPU、逐 Pass 时间或 Game View 连续帧率**。

| 1080p | 1/2 High 块中位数 ms | 1/3 High 块中位数 ms | 相对变化 |
|---|---:|---:|---:|
| 正式首轮：轮 0 | 21.167 | 21.797 | +3.0% |
| 正式首轮：轮 1 | 20.846 | 16.161 | −22.5% |
| 正式首轮：轮 2 | 21.391 | 18.668 | −12.7% |
| 有界复测：轮 0 | 22.860 | 15.202 | −33.5% |
| 有界复测：轮 1 | 18.058 | 14.158 | −21.6% |
| 有界复测：轮 2 | 20.093 | 14.523 | −27.7% |

首轮存在明显背景/提交段波动，方向不一致，不能单独宣称稳定提速。宿主进程只读检查可见同时有应用启动活动，但不能证明具体干扰源；未关闭用户应用或 Editor。只追加一次相同入口复测，1080p 三轮均降低约 22–33%；支持本场景较高分辨率下降低耗时的趋势，不承诺固定提速比例。更早原型曾测约 35%，不以其替换较嘈杂的正式报告。642×366 首轮方向也不一致，低分辨率收益不作保证；不拼接不同轮次/脚本的绝对值计算效果成本。

画质另用当前复制 SceneView 静止视角、640×360、时缓 0.5、预热 32 / 采样帧 32–95，读生产持久 history。1/2 / 1/3 / 重复 1/2 的精确表面 mask 和时间/预算匹配；重复基线聚合漂移小于 0.2%。正式两次均测得帧差 RMS 约增加 7.1%、时间方差约增加 12.8%、平均亮度约 −0.095%。降低分辨率会软化局部照明并可能改变边缘/噪声，**不是等画质优化，也没有消除地面抖动**。1/4 High 原型因更明显边缘/细节损失未合入；其画质窗口与后续视角不完全相同，不横向比较百分比。

68 组 / 1,152,944 断言与四个 GPU/历史负控制通过，覆盖 High 原生尺寸、独立/统一入口、互斥质量关键字、旧 1/2 High 仅拒绝 Diffuse 历史且不重分配 RT。Specular 三档冻结 RGB 回归差为 0。原场景/Renderer 的设置、Active/实例、dirty 与磁盘内容保持；未 Play/保存。

复跑：`bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run_high_budget.sh --isolated-diffuse`。输入 SHA256、正式两轮所有原始样本、代表均值 PNG 与回归结果归档于 Unity memory 的 `2026-10-15-diffuse-high-performance` 条目。采集器复用现有 CameraChecks helper，不是通用 GPU profiler；移动镜头、局部地面质量、XR/其他 API 与其它场景仍待验证。

## 当前增量框架：2026-10-13

Unity 6000.3.14f1 / URP 17.3 / Apple M2 / Metal，SampleScene 原 Main Camera 姿态和 SSGI 设置，Medium AO + 双 GI。测量时原 Renderer 的内存状态为 MaskRBR 开启、SSGI 关闭；用户批准显式 `--isolated-ssgi`，仅在一次性 Renderer / Feature 克隆开启 SSGI、排除其它 Feature。**这是原场景的临时 SSGI-only 配置，不是当前 Game View 配置的性能。** 原场景与 Renderer 未保存、引用及 Active 状态保持。

计时为 `SubmitRenderRequest` 到单像素 `ReadPixels` 完成的 GPU 同步墙钟，包含 CPU、驱动提交、等待、整套相机管线与回读；不是纯 GPU 时间、Trace Pass 时间或连续帧率。每个尺寸/后端组合 3 轮 × 12 样本，每块预热 8 次，轮换组合顺序，共 24 块 / 288 样本。实际关键字、生产 shader、48/64 uniform、6 条 Diffuse 射线及相同起始射线帧（8）均检查。

| Diffuse / Specular | 642×366 中位 / P95（ms） | 1920×1080 中位 / P95（ms） |
|---|---:|---:|
| RAY / RAY | 3.837 / 7.202 | 9.290 / 10.274 |
| DDA48 / DDA64 | 3.712 / 4.829 | 9.190 / 10.020 |
| RAY / DDA64 | 3.776 / 4.641 | 9.320 / 10.139 |
| DDA48 / RAY | 3.587 / 4.328 | 9.341 / 11.562 |

本轮双 DDA 与双 RAY 的 1080p 中位数相差约 0.10 ms（约 1.1%），块间波动重叠，**不宣称显著提速**；本次配置未重现原逐格版本的数倍延迟增加。新旧算法、预算和原 Renderer 内存状态不同，不能把下方历史数字当作同配置、等画质 A/B。当前 DDA 是完整可见投影线段的固定样本而非完整像素覆盖，薄前景漏追和运动稳定性仍需人工画面验收。

必要证据由 unity-developer memory 的 `2026-10-13-ssr-framework-rebuild` 条目归档：原始 24 块计时、生产输入 SHA256、CLI 响应与两尺寸最终显示 PNG。PNG 不证明等画质或 GPU 耗时。

## 历史逐格框架：结论与口径

2026-10-13，Apple M2 / Metal，SampleScene 原 Main Camera、PC_Renderer 的实际设置：Medium AO + DiffuseGI + SpecularGI、原资源/距离/时域/后处理保持。当前 Game View 为 642×366，另测固定 1920×1080。重复对照重现 DDA 的整次渲染延迟增加，主要增量来自漫反射 DDA。

计时为 `SubmitRenderRequest` 到单像素 `ReadPixels` 完成的同步墙钟，包含 CPU、驱动提交、等待、全部相机管线和回读开销；**不是纯 GPU 时间，不是 Trace Pass 的 profiler 时间，也不是 Game View 连续帧率**。报告另保留提交段，不将其标为纯 CPU 成本。

最终各对照 3 轮 × 12 样本，轮换顺序，每块预热 8 次；每块重建临时 Feature，实际第一条测量射线帧索引均为 8。RT 与单像素回读纹理在计时外分配。两次从权威入口复跑，趋势保持。

| Diffuse / Specular 后端或 DDA 上限 | 642×366 中位 / P95（ms） | 1920×1080 中位 / P95（ms） |
|---|---:|---:|
| RAY / RAY（48 / 64 样本） | 3.644 / 3.971 | 9.358 / 10.350 |
| DDA256 / DDA256 | 6.395 / 8.018 | 22.273 / 27.686 |
| RAY / DDA256 | 3.999 / 4.410 | 10.373 / 11.123 |
| DDA256 / RAY | 6.168 / 7.501 | 20.958 / 25.968 |
| DDA128 / DDA128（仅验证） | 5.375 / 6.674 | 13.500 / 15.158 |
| DDA48 / DDA64（仅验证） | 3.685 / 4.167 | 8.240 / 8.985 |

Medium 的 256 是每条 DDA 射线的单元上限，不是实测平均次数；与 Diffuse RAY 48 相差约 5.3 倍，与 Specular RAY 64 相差 4 倍。Diffuse 每个 Trace 像素有 6 条射线，Specular 一条。DDA 遍历全相机深度网格，而不是 1/4 宽高的 GI RT，因此屏幕分辨率也影响覆盖成本。所有组实际关键字、shader、RAY/DDA uniform 与 6 条 Diffuse 射线均检查。

128 / 48 / 64 是预算敏感性控制，临时 shader 以编译期上限限制原 `maxSteps`；变化还可能包含编译器利用该上限的效果，不能归因成准确的平均步数或逐指令成本。降低 DDA 预算会提前返回 `BUDGET`，可能丢失远端首命中；它不是加大 stride，也没有通过等画质或运动稳定性验收。**本次不调整生产预算、后端默认、采样或滤波算法。**

当时建议优化逐格遍历；用户随后选择改以已验证的旧 SSR 增量框架为主线，完整二维单元/slab 合同退役，仅留文档参考。当前 DDA 是完整可见投影线段上的固定样本，可能漏掉薄前景，不是原逐格实现的等画质优化。不能以源码除法数量代替 Metal 指令数，也不能以更低墙钟证明画质等价。

## 复跑

先连接当前工程的 Editor / bridge，确认 SSGI 工作区已授权，再在仓库根执行：

```bash
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run_timing.sh
```

默认入口严格要求原 Renderer 的 SSGI 已开启，且无其它 Active Feature；不满足时拒绝，不替用户改配置。如果用户明确批准只在临时克隆中启用 SSGI、排除其它 Feature，则执行：

```bash
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run_timing.sh --isolated-ssgi
```

显式入口写入 `isolatedSSGI=true` 和原始 Feature/Active 清单，克隆以外的配置不变；默认入口不会自动降级成 isolated 模式。

验证入口复用 `CameraChecks.cs` 的 RendererScope、真实 Game camera 请求与 Capture。两入口都要求实际 Main Camera 使用 Renderer 索引 0、原 Renderer 唯一 SSGI、Medium 双 GI，当前 Game View 尺寸与 1920×1080 两种不同尺寸；临时相机复制原相机姿态和 UniversalAdditionalCameraData，并渲染原场景。

当前入口不生成预算控制 shader；`prepare_timing.py` 只记录实际生产源 SHA256 与预期档位，runner 拒绝遗留 `TimingRuntimeFixture`，不保存场景或 Renderer。finally 校验原 Renderer 实例/Feature 引用/Active、原 Settings 和场景 dirty。

当前矩阵为 RAY/RAY、DDA48/DDA64、RAY/DDA64、DDA48/RAY，所有模式读取真实生产 shader。两尺寸 × 四组合 × 三轮 = 24 块 / 288 样本；`check_timing.py` 检查 success/result、请求入口对应的 isolated 模式、非空完整矩阵、实际 48/64 档位、提交/同步计时均有限、同步段不短于提交段、相同起始射线帧与状态恢复。结果写入 `tmp/sessions/`；退出码 0 或只有元数据不能证明成功。当前入口不能复现退役源码，只能测当前主线。

验证脚本依赖当前 Unity 的 Newtonsoft.Json；曾发现 Roslyn 动态类型的 `JsonUtility` 报告遗漏嵌套采样列表，初次只有元数据的报告不作为证据。现已改用实际验证过的 JSON 序列化路径。

当前版本库验证 39 组 / 89,095 断言、SSGI 实际相机/入口/历史验证 39 组 / 1,152,441 断言通过，库距离、SSGI 距离与历史负控制均识别。完整显示 PNG 是固定镜头最终合成对照，不是画质等价证明；XR、动态有效区、连续运动、其他设备与图形 API 仍未验证。
