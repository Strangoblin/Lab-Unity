# MaskBlur 暗点控制历史证据

背景见 `2026-10-09-maskblur-stratified-darkspots.md`：恒定输入、硬圆、有限分层采样与两轮 H/V 比较。
六项实际 GPU 原始回读与源输入以线性小端 float32 RGBA、640×360 存于 inputs/；同字节源只保留一个。
manifest.json 使用归档内相对路径，包含原始 taps、半径、轮数、核强度和裁切定义。
original-results/ 保存已有报告和对照图；六份生成的 NPY 不永久归档。
expected-reference.json 冻结原 NPY 的 SHA256、shape、dtype 及原报告校验；必要 RGBA 输入保留。

历史 CPU 算法冻结当时分段核、双线性积分与权重；仅修正路径和强制输出注入。
依赖 Python 3、NumPy、Pillow。以 Python 环境运行本目录 analyze_dense_reference.py，
传 `--manifest <本目录>/manifest.json --out-dir <当前任务>/output/dense-reference --self-check`。
完整 self-check 校验重生成的六份 NPY 文件 SHA 与不可变期望、原报告数值，完全不读取旧 NPY。
2026-10-10 已从正式归档完整复跑通过，六份旧生成数组已按批准清单删除。
该命令从保存的 GPU 回读计算 CPU 参考，不运行 Unity/GPU，也不验证最新 Hash/BlurFunction 改动。
旧 Shader 控制脚本、阶段 baseline 和当前 dirty MaskBlur 实现继续留原处；本次只归档完成的控制证据。
浮点参考/统计可重复，图片字体编码可能随 Pillow 版本改变；verification 记录数值对照结果。
