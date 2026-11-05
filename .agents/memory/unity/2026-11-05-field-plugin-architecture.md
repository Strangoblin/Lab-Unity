# FieldManager 插件化与 Wave 归并（2026-11-05）

## 结论

`Assets/Mine/Scripts/FieldManager/` 从「枚举分派的两个固定场」改为「按 `FieldKey` 注册的插件结构」。新增场不再改 `FieldManager.cs` 或 `FieldProvider.cs`；FFT 已物理迁入 `Wave/FFT/`，作为 Wave 通道的唯一 Producer。

## 结构

- `FieldProvider`：删 `FieldKind` 枚举；新增 `abstract string FieldKey`、`virtual int FieldOrder`（低值先跑，同值保持注册顺序）。一个 `FieldKey` 只允许一个活动 Producer，重复者被 `Disable` 并报错。
- `IFieldBindings`：每个通道的键、GPU 属性 ID 与中性回退，Provider 与缺席时的 Compute 绑定共用。
- `Wind/WindFieldBindings.cs`（Key `Wind`，Order -200）、`Wave/WaveFieldBindings.cs`（Key `Wave`，Order -100）。
- `Wave/WaveFieldProvider.cs`：Wave 通道基类，`FieldKey`/`FieldOrder` 为 sealed，`ResetGlobals` 统一走 Wave 中性值。
- `Wave/FFT/FFTWaveFieldProvider.cs`：原 `Mine.Water.FFTWaveOrchestrator`；`FFT.compute` 同目录。GUID 全部保留（脚本 `80f6d5c7…`、compute `3eac42d9…`）。
- 消费者 API 改为 `FieldManager.BindCompute(<Channel>FieldBindings.Instance, shader, kernel)`；删除 `BindWindCompute` 与 `FieldKind` 重载。
- HLSL 随通道存放：`Wind/WindField.hlsl`、`Wave/WaveField.hlsl`，所有 `#include` 已改为新路径（`#include` 按路径解析，不随 `.meta` 走）。

## 调度

`UpdateFields` 单次遍历已按 `FieldOrder` 排好的列表；注册时做稳定插入排序。`Clock` 在 Play 用 `Time.time`，编辑态用 `realtimeSinceStartup`。Provider 无 Manager 时仍自行 `Advance`，所以旧场景里的 FFT 不依赖 Manager 也能跑。

## 验证（end-to-end）

- 新增 `Validation~/FieldChecks.cs`：GrassManagerDemo `passed=10 failed=0 skipped=0`；RenderScene `passed=9 failed=0 skipped=1`（场景已有 Wave，注销用例跳过）。覆盖继承链、键、顺序、`FieldKind` 不存在、中性全局值、中性 Compute 绑定、缺席 Producer 返回 false、重复 Producer 禁用、注销复位、注册顺序。
- 脚本会临时停用场景 Manager 以独占注册表，结束后重启 `UpdateFields()`；RenderScene 验证后 `_WaveFieldValid=1`、三层 displacement 仍绑定，Grass 场景 `_WindFieldValid=1`。
- Shader 编译：Water 1 pass、GrassWind 1 pass、Plant 4 pass 全 0 error；Wind/FFT/FieldContractProbe/Rain 四个 compute 均可加载。
- 静态扫描无 `FieldKind` / `BindWindCompute` / `FFTWaveOrchestrator` / `Mine.Water` 残留。

## 注意

- Unity 编辑器未运行时 `unityctl script execute` 会失败；`unityctl editor launch` 在本机找不到 Hub 安装路径，需要 `--unity-path /Applications/Unity/Unity-6000.3.14f1/Unity.app`，且该命令实际可能只拉起 Hub；直接 `Unity -projectPath` 更可靠，随后 `unityctl bridge start`。
- 若编译失败，Editor 进入 Safe Mode；修好后聚焦 Unity 会自动重新编译并连接 bridge。
- `Validation~` 目录带 `~` 不参与 AssetDatabase 编译，只能通过 `unityctl script execute` 运行。
