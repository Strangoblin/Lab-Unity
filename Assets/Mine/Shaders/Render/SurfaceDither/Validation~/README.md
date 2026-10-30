# SurfaceDither Validation

`Validation~` 以 `~` 结尾，Unity 不会导入。这里保存隔离渲染证据与可复跑的验证入口。

## 运行

菜单：`Tools/Surface Dither/Validate`

批处理：

```bash
Unity -batchmode -quit \
  -projectPath <项目根> \
  -executeMethod Mine.SurfaceDither.SurfaceDitherValidator.Validate \
  -logFile /tmp/surface-dither-validate.log
```

## 输出

| 文件 | 内容 |
|---|---|
| `results.txt` | 最近一次编译、Pass/变体、隔离渲染统计与 PASS/FAIL |
| `near-*.png` | 相机 Z=-3.2 的 512×512 显示图 |
| `far-*.png` | 相机 Z=-6.4 的 512×512 显示图 |
| `brightness-*.png` | `_DotContrast=0` 的输入亮度诊断图 |
| `object-space-*.png` | `_CoordinateMode=1` 的对象空间投影图 |

## 条件

- 2.2×2.2×0.2 平板立方体，FOV 32，URP Game/Base 相机。
- 预览场景使用平铺环境光 0.55，专测抖动算法；预览场景主光未被 URP 选中，
  因此主光/阴影只验证编译，画面需在真实场景人工确认。
- 统计以绿色背景区分物体；`dark` / `light` 阈值为亮度 0.25 / 0.6，
  边缘密度是水平相邻非背景像素的亮度跳变比例。
- PNG 是显示证据，不是线性 HDR 数值证据。
