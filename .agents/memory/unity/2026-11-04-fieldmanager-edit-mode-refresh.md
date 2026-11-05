# FieldManager 编辑模式刷新（2026-11-04）

`FieldManager` / `FieldProvider` / `WindSource` 都是 `[ExecuteAlways]`。Unity 在编辑模式下只会在场景重绘时调用它们的 `Update`；编辑器空闲时实测 55 秒内 `WindFieldProvider.Version` 与 `SampleTime` 完全不变（`realtimeSinceStartup` 正常增长）。因此改 Inspector 里的 `WindSource.Velocity`、`Radius` 或 provider 的 Base Wind 后，值虽然写进组件，但 Compute 不会重新分发，`_WindFieldTexture` 仍是旧内容，观感就是“面板参数无效”；进入 Play Mode 后 PlayerLoop 正常驱动，所以立刻恢复。

修复：`FieldProvider.OnValidate`（覆盖所有 provider，含 `FFTWaveOrchestrator` 的 override）在非 Play 且 `IsReady` 时调用 `FieldManager.Instance.UpdateFields()`，再用 `UnityEditorInternal.InternalEditorUtility.RepaintAllViews()` 强制重绘。实测通过 `SerializedObject` 模拟 Inspector 修改后，`Version` 每次 +1，640×360 渲染变化 13.37%，无需手动刷新。同日用户要求移除局部风源，`WindSource.OnValidate` 随该类一起删除；编辑模式刷新现在只由 `FieldProvider.OnValidate` 承担。

注意区分：同日的“风场速度不稳定”是另一件事，根因是 `WindField.compute` 用“瞬时速度 × 运行时间”推进相位导致漂移随编辑器运行时长线性加速，已改为解析积分并按扰动周期取模。相关记录见 [2026-11-03-grass-manager-template](2026-11-03-grass-manager-template.md)。
