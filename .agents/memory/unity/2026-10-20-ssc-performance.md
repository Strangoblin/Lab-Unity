# SSC 性能优化落地（2026-10-20）

用户在重设计建议后授权“先改性能部分”。本轮生产改动仅 SSC.shader；保留已有云形状、密度/积分估计量、光照探针距离、面板与用户实时64步/降采样2配置。
沿用 Production、现有 SSC 工作区和知识门禁；未提交/推送，未保存原场景或Renderer。

## 实现与契约

1. 高度剖面为空时不读噪声；localDensity=0时跳过整段光照采样/散射计算。
2. **仅累计Density>=1时精确早停**，此时基线后续stepAlpha=0。未采用近似透射率阈值，不依赖half舍入模式，不改变RGBA覆盖率语义。
3. 普通亮度天空精确检查原4×4低分辨率采样点的深度，以16位uint记录天空成员。全部16点为天空且max(abs(sceneRGB))<=1时，用原B-spline权重的相邻pair恒等式，改为4次bilinear云采样。
4. 天空/几何混合处解码mask恢复原0/1深度权重；非天空或HDR直接走原逐点交错depth/cloud重建，避免缓存16个float跨分支存活。全分辨率云盒遮挡与两条中心积分fallback保持。
5. 两Pass target=3.5以支持uint位操作；未增加RT、Pass、历史或公开参数。Metal已验证，其它平台未实测。

最终生产源码SHA256：`9472265d4fbd217bdf240cd41f794d139d15ac18b54acec218393eb04c416748`。
基线为本轮开始时的实际工作区实现（含上轮质量修复），SHA256 `456dcf3722c79c6eb565ea09c7d91953a517397f7e4bc797a165d787c82194e5`，固定于 `Assets/Mine/Shaders/PostProcess/SSC/Validation~/Fixtures/SSC-before-optimization.shader.txt`。

## 最终性能证据

Apple M2 / Metal / Unity6000.3.14f1，RenderScene，1920×1080 ARGBHalf、Renderer0、无PP、speed/jitter=0。
普通视角复制用户SceneView pose/FOV；边缘场景为固定相机和8根完全位于云盒前的细斜杆，空闲layer隔离、solid背景。
每场景baseline/optimized × 3预算 × 3轮 × 12样本=216样本，另每块8预热，轮间反向。总432有效样本、36块，全部有限且sync>=submit，源码/CSV哈希匹配，全部恢复且dirty前后false。
口径：SubmitRenderRequest + ReadPixels(1×1) 的GPU同步墙钟，含CPU/驱动/等待，**不是纯GPU/Pass时间或实际Game View FPS**。编译/分配/编码/落盘不在计时内。与前轮带Apply等不同适配器的数字不混算。

| 场景/预算 | 原版三轮中位范围ms | 优化后三轮中位范围ms | 每轮中位降幅 |
|---|---:|---:|---:|
| 普通64步/降2 | 8.68–8.78 | 6.46–6.54 | 24.7%–25.9% |
| 普通64步/降1 | 16.53–16.69 | 11.65–11.77 | 28.9%–29.7% |
| 普通128步/降2 | 14.94–16.04 | 10.95–11.33 | 24.2%–30.1% |
| 边缘64步/降2 | 5.08–5.91 | 3.76–4.20 | 24.7%–31.8% |
| 边缘64步/降1 | 9.30–10.62 | 5.74–7.10 | 33.2%–38.3% |
| 边缘128步/降2 | 8.26–11.00 | 6.35–6.52 | 21.1%–42.3% |

每一轮中位与P95均下降。普通64降2的P95原8.91–9.29ms→新6.59–6.61ms；边缘128基线漂移较大，不将42%称为固定收益。块内12样本的P95仅是本次比较，不能承诺连续帧率/帧尖峰全面改善。

## 画面与边界验证

- baseline-repeat负控制全部差0；float HDR全通道有限。最终visual含当前视角、前景粗/细杆、云内几何和黑云/HDR1000，共16组，独立保存raw/PNG/误差CSV。
- 普通背景RGB的最大MAE约0.0001414、P99<=0.0009766、最大差0.0029297，含硬件过滤/浮点/半精度量化，非逐像素RGB等价。
- 不能把URP最终alpha=1当作云覆盖率：coverage适配器把Pass0 alpha编码到RGB后showSSC，64步/降采样0/1/2/3四组与基线**逐像素差0**。
- HDRBoundaryCheck：实际校准背景与材质RGB1000，真实噪声/白噪声×天空/云内细杆四组全部通过、全部负控差0、中心RGB差0。真实噪声全图max差0.0625（相对1000为6.25e-5），白噪声max0.0004883。白噪声中心baseline/optimized均为精确黑，细杆案例覆盖中心fallback。
- RGB111110Float与ARGBHalf的空云/可见云正负控制通过：空云max/RMS=0，可见云RMS约0.05604/0.05571；不是空跑。
- 两Pass×64/128/256六变体Metal同步编译，ShaderUtil最终0条消息；64/128实渲与计时，256未做性能测量。正式shader、两个新验证脚本规范与git diff --check通过。
- 最终现场：RenderScene dirty=false、Play=false，内存COUNT64/downsample2/speed1/jitter1，原蓝白色与shader引用全部恢复，磁盘Renderer未写回。

## 被测试否决的方案

- 近似早停即使T<=.0001，也不能凭“RGBA16F最近舍入”断言alpha必为1：实际白噪声/HDR1000测试出现优化值0.48828125而基线为0。最终改精确饱和早停，删除相关近似误差界。
- 四次bilinear在HDR背景会放大覆盖率过滤舍入：初版黑云实际噪声、背景1000的全图max差2.5。最终加入HDR保护走原point-weighted路径。
- 初版缓存16float depthWeight跨分支，在边缘低预算配置波动较大；最终用sky bitmask，非天空保旧交错流程，正式新基准不混用中间版本。
- 初版HDR脚本嵌套Case数组被动态JsonUtility省略，补显式cases.csv；测试自身保留失败标记，未把空报告当通过。初始“真实噪声中心必须全黑”不成立（噪声孔洞），改为实际噪声误差目标+白噪声不透明正控。

## 复现与证据

入口及协议见 `Assets/Mine/Shaders/PostProcess/SSC/Validation~/README.md`：OptimizationCheck的visual/coverage/benchmark/benchmark-edge，HDRBoundaryCheck和HDRCheck。
历史PerformanceProbe固定读pre-optimization fixture，避免新shader已无旧补丁标记而失效；它只用于重跑旧.995探针，不是当前优化基准。
验证源码/基线已归档并实际复跑。运行输出在 `tmp/sessions/2026-10-20-ssc-opt-c1/output/`；大幅面raw float可由脚本重建，长期保留以下小报告和选图。

- [普通场景逐样本](evidence/2026-10-20-ssc-performance/normal-benchmark-samples.csv) / [元数据](evidence/2026-10-20-ssc-performance/normal-benchmark-metadata.json)
- [边缘场景逐样本](evidence/2026-10-20-ssc-performance/edge-benchmark-samples.csv) / [元数据](evidence/2026-10-20-ssc-performance/edge-benchmark-metadata.json)
- [画面误差](evidence/2026-10-20-ssc-performance/visual-metrics.csv)、[覆盖率](evidence/2026-10-20-ssc-performance/coverage-metrics.csv)、[HDR四组](evidence/2026-10-20-ssc-performance/hdr-cases.csv)
- [被否决的近似早停原始失败数据](evidence/2026-10-20-ssc-performance/rejected-approximate-stop-cases.csv)
- [修改前](evidence/2026-10-20-ssc-performance/before.png)、[修改后](evidence/2026-10-20-ssc-performance/after.png)、[细前景](evidence/2026-10-20-ssc-performance/thin-foreground.png)

所有者SSC性能验证；依赖上述源码hash、RenderScene/噪声/光照和协议。后续正式基准替代时可人工确认清理长期证据。
OpenCode按指定deepseek-v4.1-flash参与只读复核，但读取了范围外memory后中止（130、未见写入）；最终实现、独立数学复核与全部GPU验证由主会话/原生子代理完成。
云团黏连、积分步数与光照耦合、参数精炼/质量档仍归后续重设计；本轮未改这些语义。运动、XR、动态分辨率、其它GPU/平台、连续FPS仍待验证。
