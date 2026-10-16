# DiffuseGI High 性能再平衡

## 用户目标与范围

- Diffuse 本身比 Specular 昂贵，尤其 High 卡顿；用户选择“允许少量细节取舍，优先明显提速”，先临时克隆验证，不直接改原场景档位。
- 本轮将正式 Diffuse High 从 `(2,8,64)` 改为 `(3,8,64)`：宽高各 1/3，仍 8 射线/64 步；不是 Medium 的 `(4,6,48)`。Low/Medium、AO/Specular、通用步进、完整 cosine 半球、黑色 miss/总射线平均、两级 1→2 重建与时缓不改。
- 原 Renderer 当前内存为 SSGI off，原设置保持 Medium、Diffuse/Specular 时缓 0.5；仅临时 Diffuse-only 克隆设 High。未 Play、保存场景/Renderer、提交或推送。HEAD 基准 f9b3ad8，大量既有 dirty 改动保持。

## 诊断与实现

- 修正早期计时器逐块新建 Feature 并让 RendererScope 保留旧 history 的问题；复用 Feature/在计时外 Create 释放旧 history。早期滞留版本只作方向性定位，不用于精确结论。
- 修正后模块配对在 1080p 三轮中 Diffuse High 均慢于 Specular High；Diff 高档块中位数 23.305/25.591/24.320 ms，Spec 为 17.661/17.911/18.472 ms。低中档与背景漂移重叠，不报稳定倍率。不能以 GI Off 的管线启停差直接充当某个 Pass 的边际成本。
- Diffuse 一像素追踪 4/6/8 条射线，Specular 一条。NoTrace 方向性控制比 NoSpatial 更明显，优先减少 High 追踪网格，而非先移除抗抖滤波。
- 先保留质量局部关键字的固定 4/6/8 射线、24/48/64 步编译预算及无关键字 uniform fallback；实际生产静态预算与动态基线输出一致，但耗时方向跨轮不一致，**不宣称关键词单独明显提速**。不合入强制循环展开。
- 1/4 High 原型有更明显的边缘/细节损失，未合入；其静止视角与后续 1/3 不完全相同，不用两者 RMS 百分比作受控横比。
- High 1/3 在 1080p 从 960×540 降至 640×360，追踪像素约少 55.6%。滤波核的屏幕覆盖也扩大，可能软化照明细节、改变边缘与噪声；不是等画质重构。
- 历史签名已有 `_sampling.x=divisor`，旧 1/2 High 只拒绝 Diffuse 一帧、不重分配全分辨率历史，不改 `_sampling.w=3` 采样实现版本。

## 正式计时与判读

Apple M2 / Metal、Unity 6000.3.14f1，复制当前 Main Camera，临时 Diffuse-only Renderer、Indirect Debug、DDA、时缓 0.5；每块 8 预热 / 12 测量、三轮交错反向。**GPU 同步整次请求墙钟**：Stopwatch → SubmitRenderRequest → ReadPixels(1×1)；不是纯 GPU、逐 Pass 时间或连续帧率。

| 1080p | Half High 中位数 ms | Third High 中位数 ms | 变化 |
|---|---:|---:|---:|
| 正式首轮 0 | 21.167 | 21.797 | +3.0% |
| 正式首轮 1 | 20.846 | 16.161 | −22.5% |
| 正式首轮 2 | 21.391 | 18.668 | −12.7% |
| 有界复测 0 | 22.860 | 15.202 | −33.5% |
| 有界复测 1 | 18.058 | 14.158 | −21.6% |
| 有界复测 2 | 20.093 | 14.523 | −27.7% |

首轮波动明显、方向不一致，不能单独认定稳定收益。只读宿主检查发现同时存在应用启动活动，但不证明它就是干扰源；未关闭任何用户应用。只追加一次相同入口复测，三轮均降低约 22–33%，支持本场景高分辨率下的降低耗时趋势，**不承诺固定百分比**。早期原型三轮曾约降 35%，不替换较嘈杂正式结果。642×366 的首轮方向也不一致，不保证低分辨率收益。到此收敛，不追逐更多快样本。

## 质量与回归

- 质量单独复制当前 SceneView 静止视角、640×360、关闭克隆后处理/AA、时缓 0.5、32 次预热、64 样本（帧 32–95），读取生产持久 history；不是最终画面截图或帧末池化 global。
- HalfHigh / ThirdHigh / HalfHighRepeat 的表面 mask、Shader 时间、预算与射线帧匹配；重复基线聚合漂移低于 0.2%。两次正式报告帧差 RMS 均约 +7.1%、时间方差 +12.8%、平均亮度约 −0.095%。
- 复测 RMS `0.001184393716 → 0.001267995788`，方差 `1.134270619e-6 → 1.280021609e-6`；均值图表面亮度 RMS 差 `0.001595640917`。目视均值 PNG 已检查，局部细节有损失；人工运动/地面观察仍需验收。
- **地面抖动没有解决**，本轮是用户允许的画质/成本交换。Low/Medium 未降低追踪量，不据此宣称 Diffuse 三档都已明显提速。
- Metal 68 组 / 1,152,944 断言，DDA 距离/错误后端历史/空间颜色/遗漏 Fine 四个负控制均通过；新增真实原生尺寸、三档互斥关键词、独立/统一入口和旧 HalfHigh 历史拒绝断言。AO 隔离、Specular 三档冻结回归 RGB 差 0，GI 两入口逐档/后端匹配。
- 修改共享验证 helper 后追加 `run.sh --compare`，原 16 样本单遍/两级对照及完整矩阵再次通过；单遍基线只冻结编排/步幅，档位分辨率跟随当前值，避免混入 High 网格差异。
- Python AST、Shell 语法、规范检查、SSGI diff whitespace、三档 CPU 采样检查通过。内置子代理只在 tmp 写三份 prepare/check/runner 草稿并做离线正例与拒错控制，主会话检查后经门禁合入/运行；未重试此前被拒的 OpenCode 源码外发。
- 每次 finally 校验原设置、Renderer 引用/实例/Active、场景 dirty；自建 fixture/meta 已清理。前后磁盘 SHA256：SampleScene `200e8639ded8e085b1f0ff9ea2ac40223ed49bf915c49e70573e18210dce77f8`，PC_Renderer `16aded5498bf765202f650f2f9c2f6fdbeba7113ee9e753e39675dc87531574b`。

## 可复现入口与归属

- 生产：`Assets/Mine/Shaders/PostProcess/SSGI/DiffuseGI/DiffuseGIFeature.cs`（High 网格），同目录 Shader/helper（质量编译预算）。
- 功能专属测评：`Validation~/run_high_budget.sh --isolated-diffuse`，`prepare_high_budget.py`、`HighBudgetChecks.cs`、`check_high_budget.py`。复用 CameraChecks partial 的克隆/持久历史/显示 helper，不保存第二份完整验证器。
- baseline 从当前生产 Feature 精确改类 token 与 High 除数 3→2，检查唯一命中和 token 差异；记录源码和生成文件 SHA256，不复制长期生产实现。
- 所有正式原始块（含首轮噪声）、输入清单与源码快照、代表 PNG、模块配对和回归报告见 [evidence/2026-10-15-diffuse-high-performance](evidence/2026-10-15-diffuse-high-performance/)。只保留必要输入/证据，不整包 tmp。快照为历史输入，维护真身仍在功能目录。
- 原始正式输出：`tmp/sessions/2026-10-15-diffuse-high-budget-d675940f/output`、`tmp/sessions/2026-10-15-diffuse-high-budget-073d2af9/output`。完整场景是当时 live 输入，未冻结整个项目；快照不能保证相同绝对数值。运动、局部 floor、XR、其它 API/设备与其它场景仍未验。

前序采样临时记录已按新路由补为 [2026-10-14-diffuse-sampling-sequence.md](2026-10-14-diffuse-sampling-sequence.md)，保留“墙壁良好、地面残留”的人工反馈，不覆盖成性能优化已经修复稳定性。
