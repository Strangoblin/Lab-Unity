# SDF 共享库归一化覆盖度（2026-10-22）

`Assets/Mine/Special/HLSL/SDF.hlsl` 由世界单位欧氏 SDF 改为归一化覆盖度 SDF，并新增二维 `SDF_Circle` 与 `SDF_Square`。用户明确要求输出中心 `1`、边界 `0`。

## 契约

- `position` 与 `radius`/`bounds` 必须使用同一空间单位。
- 输出限制在 `[0,1]`：图元中心 `1`、边界 `0`、外部 `0`。
- 内部有符号距离除以“中心到最近边界的距离”后取负并 `saturate`：`saturate(-signedDistance / centerDistance)`。
- 球/圆以半径归一化；盒/方形以最短半尺寸归一化，因此非均匀盒的中心仍为 `1`，较长轴内部会出现覆盖度饱和平台。
- `opSmoothUnion` / `opSmoothIntersection` 改为 `[0,1]` 覆盖度的平滑最大/最小；`opSmoothSubtraction` 从 `d2` 平滑扣除 `d1`；`k` 为 `[0,1]` 覆盖度混合宽度，极小值按 `1e-6` 兜底。

## 验证

- `python3 .mcp/validation/check_norm.py Assets/Mine/Special/HLSL/SDF.hlsl` 通过。
- `glslangValidator -V -D -S frag` 以包含四个图元与平滑运算的临时入口编译通过。
- CPU 数值断言：球/圆/方形中心 `1`、边界 `0`、外部 `0`；非均匀盒 `(1,2,4)` 中心 `1`、边界与外部 `0`。
- 尚未做 Unity/Metal 实际 Shader 变体编译或画面验证。

## 调用影响

此前的 `SDF_Sphere` / `SDF_Box` 返回世界单位有符号距离，现返回 `[0,1]` 覆盖度。不能继续把返回值当 ray march 步长、梯度距离或平滑布尔的有符号输入；需要距离时应保留内部有符号 SDF 并单独转换。仓库内检索时没有其他文件调用这四个图元。
