# 渲染同步墙钟口径

本 reference 记录测量含义与限制；采集合同、样本检查、对照与判读归 [unity-render-benchmark 的测评协议](../../../../skills/unity-render-benchmark/references/benchmark-protocol.md)。

| 口径 | 含义 |
|---|---|
| GPU-synchronized render-request wall milliseconds | 渲染请求到目标纹理可读的墙钟；包含 CPU / 驱动 / 渲染 / 等待及回读 |
| 提交段 | 不等于纯 CPU 成本；与 GPU 工作可重叠 |
| 同步段减提交段 | 不等于独立 GPU 时间 |
| 目标 RT 回读 | 等待目标可读，不证明所有 GPU 队列完全空闲 |
| 整管线 A/B | 不提供单 Pass 的独立边际成本或连续 Game View FPS |
| 历史结果 | 只适用于当时设备、场景、分辨率和配置，不能拼成新的受控对照 |

纯 GPU / 逐 Pass、等画质和跨平台结论需要各自的证据。项目计时适配器仍在功能侧，通过项目资源 ID 取得。
