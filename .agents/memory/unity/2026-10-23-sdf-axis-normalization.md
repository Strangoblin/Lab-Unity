# SDF 逐轴归一化与 SSC 的 XZ 软边（2026-10-23）

用户澄清：SSC 只需 XZ 平面边缘过渡；Box 若长短轴均真正归一化，可直接继续使用 Box。这取代同日 [初次云盒接线](2026-10-23-ssc-sdf-boundary.md) 的三维、最短半尺寸羽化方式。

## 当前契约

- `Assets/Mine/Special/HLSL/SDF.hlsl` 的 `SDF_Box` 改为 `q=abs(position)/max(abs(bounds),1e-6)`，返回 `saturate(1-max(q.x,max(q.y,q.z)))`。`bounds` 仍为半尺寸，每个轴各自中心到边界覆盖度从 1 降至 0；长短轴各自半程均为 0.5，不再受最短轴世界长度限制。
- `SDF_Square` 同步使用二维逐轴归一化；Box 的 `y=0` 切片等价于 Square 的 XZ 输入。球/圆和组合算子不变。
- 这是归一化盒覆盖度，不是世界单位欧氏有符号距离。非均匀坐标缩放改变世界距离度量；不能作为 ray march 步长。低于 `1e-6` 的尺寸按 epsilon 处理，不声明退化轴上的严格缩放不变性。
- 仓库实际图元消费者为 SSC；GrassInstance 仅 include 共享库，未调用这些图元。

## SSC 接入

- `Assets/Mine/Shaders/PostProcess/SSC/SSC.shader` 继续用 Box，输入 `float3(positionWS.x,0,positionWS.z)`，云盒 XZ 中心仍为当前 `(0,0)`。Y 的平滑交给原 `DensityH`。
- 保留用户已调整的 `boundaryFade=0.8`。此时 XZ 每侧世界过渡宽度为对应轴半尺寸乘 0.8；保存的 size `(500,20,500)` 对应 X/Z 各 200 单位，Y 高度不再控制水平过渡。
- 仍在原阈值判断后才乘边界权重；`Noise`、`DensityLOD`、`DensityH` 函数体未改。
- 独立检查高度处于 `[paramUD.y,paramUD.x]` 内才采噪声，避免 `middle=1` 时 `DensityH` 的 saturate 使盒上方光照探针残留密度。这是硬范围限制，不是额外 Y 羽化。
- 尚未改动无世界步长的 alpha 积分；局部密度平滑仍可能被长路径累积成近不透明，不声明最终视觉问题已完全解决。

## 验证

- 精确检查本次两文件相对开工快照的增量；规范检查与 `git diff --check` 通过，保留全部其他既有改动，未提交或推送。
- native 并行助手只读复核归一化公式、轴缩放不变性及 Box/Square 等价性，无源码修改。
- 已运行 Editor，恢复同宿主 Bridge 并由 `unityctl wait` 确认连接。实际图形 API 为 Metal。
- 临时内存 Shader 调用真实共享库，10 个输入、30 项 GPU float 读回断言通过：中心、长/短轴半程、非均匀对角线、边界/外部、逐轴尺寸变化、负 bounds 及 Y 置零切片。
- 对临时 SSC Material 的 32/64/128/256 四关键字、两个 Pass 同步编译，共 8 组合通过；`ShaderUtil.GetShaderMessages` 为 0。GPU 探针与材料/RT 均在 finally 销毁，活动 RT 恢复，场景 dirty 状态未变，未保存资产或场景。
- 未做整幅相机画面或耗时对照；本轮验证支持归一化与编译结论，不代替视觉验收。一次性验证脚本/快照收尾精确清理。
