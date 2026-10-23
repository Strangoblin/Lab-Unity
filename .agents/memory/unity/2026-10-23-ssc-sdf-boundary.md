# SSC 云盒 SDF 边界密度（2026-10-23）

接手 Codex「修复 SSC 色彩与降采样锯齿」会话。用户本轮要求把 `Assets/Mine/Special/HLSL/SDF.hlsl` 接入密度以软化体积边界，继续保留已换用的 Voronoi 纹理与现有密度算法。

## 实现与范围

- 只修改 `Assets/Mine/Shaders/PostProcess/SSC/SSC.shader`：完整路径 include `SDF.hlsl`，`CalculateDensity` 使用现有 `SDF_Box` 覆盖度；共享库本身未改。
- 坐标与 `CloudRayBounds` 一致：中心 `(0, _CloudParamA.w, 0)`，半尺寸 `_CloudParamB.xyz * 0.5`。视线样本与光照探针共用入口，六面及盒外权重为 0，盒外跳过噪声采样。
- 边界权重为 `smoothstep(0, 0.1, coverage)`；暂定宽度是最短半尺寸的 10%，默认 size `(100,50,100)` 对应 2.5m。更内部权重为 1，未新增面板参数；未来调宽度只需改此局部常量。
- 原噪声 × 高度密度先做原 threshold 判定，最后才乘边界权重，避免重新截断软边。`Noise`、`DensityLOD`、`DensityH`、阈值及散射/质量预算不变。纹理自身的 threshold 切口仍沿用旧规则，本轮只处理体积盒边界。
- 保留任务前已有全部未提交改动，未提交或推送。

## 实际验证与限制

- 门禁写入、规范检查及 `git diff --check` 通过。
- 抽取实际密度函数并包含实际 SDF 库，`glslangValidator` 编译 LOD 1..4 通过；不是完整 Unity/Metal Shader 变体编译。
- 从实际 `SDF_NormalizeCoverage`、`SDF_Box`、`DensityH`、`CalculateDensity` 生成临时 C++ 数值载体，噪声替换为固定值：166,323 项断言通过，覆盖三种盒尺寸、六面/盒外、宽度、有限范围、中间区域保真、middle/thickness/threshold 端点。默认盒侧面内移约 0.0025m 得密度约 2.19e-6；把 feather 放在 threshold 之前的负控会归零，确认检查能区分接线顺序。
- OpenCode worker `opencode-go/deepseek-v4.1-flash` 只读复核了一次；接受其过渡过宽意见，将初稿全半径渐变收窄到上述 10%。其建议的距离反推公式未采用，由主会话检查最终 diff 并验收。
- `unityctl status` 显示 Editor 与 Bridge 均未运行，未启动 Editor，未做 GPU/画面或耗时验证。
- 本轮临时载体/输出仅用于 CPU 与语法验证，收尾已精确清理本次自建的 16 个文件及专属目录；上述结果留在本记录，不作为长期测试入口。
