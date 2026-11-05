# FieldManager 功能回归

在已连接本项目的 Unity Editor 中运行：

```sh
unityctl status
unityctl script execute Assets/Mine/Scripts/FieldManager/Validation~/FieldChecks.cs
```

成功返回 JSON：`failed=0`。场景已有 Wave Producer 时，`registry.unregister_resets_globals` 会跳过（`skipped=1`），其余用例应全部 PASS。失败项出现在 `details` 的 `FAIL` 行，不能仅根据 CLI 有输出判定通过。

覆盖 FFT → Wave → FieldProvider 继承链、Wind/Wave 的 FieldKey 与 FieldOrder、`FieldKind` 已移除、IGPU 全局中性值、Compute 中性绑定、缺席 Producer 返回 false、重复 Producer 被禁用并记录错误、注销后全局值复位、以及 Wind 先于 Wave 的注册顺序。场景已有 Wave 时直接检查，缺席时用无资源的临时 Wave Producer 注册后检查并立即注销。

脚本只创建临时 GameObject，并在 finally 销毁；会临时停用场景中的 FieldManager 以独占注册表，结束后重新启用并 `UpdateFields()` 恢复场景快照。重复 Producer 用例预期在 Console 产生一条 `Duplicate field producer for 'Wind'` 错误，脚本会消费并断言该条，不计入失败。不保存场景、不进入 Play、不创建资产。
