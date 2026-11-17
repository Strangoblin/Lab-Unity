# FieldManager 插件化与 Wave 归并（2026-11-05）

## 结论

`Assets/Mine/Scripts/FieldManager/` 从「枚举分派的两个固定场」改为「按 `FieldKey` 注册的插件结构」。新增场不再改 `FieldManager.cs` 或 `FieldProvider.cs`；FFT 已物理迁入 `Wave/`，并直接成为该通道唯一的 `WaveFieldProvider`。

**2026-11-06 更新**：Wave 通道短期内只有 FFT 一种实现，取消抽象基类与 `FFT/` 子目录：原 `FFTWaveFieldProvider` 直接改名 `WaveFieldProvider`，`FFT.compute` 改名 `WaveField.compute`，场景对象名也从 `FFT` 改为 `Wave`。GUID 仍全部保留。同时给三级 cascade 增加独立方向集中度，让中/小尺度形成交叉风浪。

## 结构

- `FieldProvider`：删 `FieldKind` 枚举；新增 `abstract string FieldKey`、`virtual int FieldOrder`（低值先跑，同值保持注册顺序）。一个 `FieldKey` 只允许一个活动 Producer，重复者被 `Disable` 并报错。
- `IFieldBindings`：每个通道的键、GPU 属性 ID 与中性回退，Provider 与缺席时的 Compute 绑定共用。
- `Wind/WindFieldBindings.cs`（Key `Wind`，Order -200）、`Wave/WaveFieldBindings.cs`（Key `Wave`，Order -100）。
- `Wave/WaveFieldProvider.cs`：具体的 Wave Producer（已无抽象基类），`FieldKey`/`FieldOrder` 为 `Wave`/-100，`ResetGlobals` 走 Wave 中性值；原 `Mine.Water.FFTWaveOrchestrator`。脚本 GUID `80f6d5c7…`。
- `Wave/WaveField.compute`：原 `FFT.compute`，GUID `3eac42d9…`。
- 消费者 API 改为 `FieldManager.BindCompute(<Channel>FieldBindings.Instance, shader, kernel)`；删除 `BindWindCompute` 与 `FieldKind` 重载。
- HLSL 随通道存放：`Wind/WindField.hlsl`、`Wave/WaveField.hlsl`，所有 `#include` 已改为新路径（`#include` 按路径解析，不随 `.meta` 走）。
- 序列化字段 `_fftCS` 保留原名，避免改字段名导致场景引用丢失；运行时 RT 调试名改为 `Wave_*`。
- 场景层级：WaterInteractionDemo 中的 `Wave` 已挂到 `FieldManager` 实例下（属于 prefab 实例的 added GameObject，不是 prefab 子物体）；RenderScene 没有 FieldManager，Wave 仍为根对象。FieldManager.prefab 只含 Manager + Wind。

## 方向集中度（2026-11-06）

原三级 cascade 共用同一个 Phillips `cos³` 方向瓣，三层波峰方向一致，视觉上整片水面是规则的平行波。Wind 有涡流扰动，而 Wave 没有；直接往位移里注入涡流会破坏可平铺的周期纹理和 Wave 采样契约。改为在频谱层解决：

- 每级新增 `_cascadeDirectionality`（默认 `{3, 1.6, 0.7}`），`InitSpectrum` 用 `pow(saturate(kDotW), power)` 取代固定 `cos³`。
- 大尺度保持 3（窄方向涌浪），中/小尺度降到 1.6/0.7，形成方向不同的短峰交叉浪。
- 用 downwind 半球 `cos^p` 的方向积分均值在 CPU 计算归一化，保持总波能稳定；power=3 时归一化为 1，等价于原实现。
- 若以后确实需要“涡流”式扰动，应作为独立的表面细节/流场调制通道叠加，而不是改变 FFT 纹理的周期结构。

## 调度

`UpdateFields` 单次遍历已按 `FieldOrder` 排好的列表；注册时做稳定插入排序。`Clock` 在 Play 用 `Time.time`，编辑态用 `realtimeSinceStartup`。Provider 无 Manager 时仍自行 `Advance`，所以旧场景里的 FFT 不依赖 Manager 也能跑。

## 验证（end-to-end）

- 新增 `Validation~/FieldChecks.cs`：GrassManagerDemo `passed=10 failed=0 skipped=0`；RenderScene `passed=9 failed=0 skipped=1`（场景已有 Wave，注销用例跳过）。覆盖继承链、键、顺序、`FieldKind` 不存在、中性全局值、中性 Compute 绑定、缺席 Producer 返回 false、重复 Producer 禁用、注销复位、注册顺序。
- 脚本会临时停用场景 Manager 以独占注册表，结束后重启 `UpdateFields()`；RenderScene 验证后 `_WaveFieldValid=1`、三层 displacement 仍绑定，Grass 场景 `_WindFieldValid=1`。
- Shader 编译：Water 1 pass、GrassWind 1 pass、Plant 4 pass 全 0 error；Wind/FFT/FieldContractProbe/Rain 四个 compute 均可加载。
- 静态扫描无 `FieldKind` / `BindWindCompute` / `FFTWaveOrchestrator` / `Mine.Water` 残留。
- 2026-11-06 改名后复验：Water/GrassWind/Plant 三 Shader 0 error（2/1/4 pass），Wind/Wave/Rain/WaterInteraction 四个 compute 均可加载；GrassManagerDemo `passed=10 skipped=0`，WaterInteractionDemo `passed=9 skipped=1`。
- 方向集中度实测（读回 `Wave_C*_Disp` 的高度通道，沿风向/垂直风向的梯度能量比 perp/par）：uniform `{3,3,3}` → C0 0.356 / C1 0.347 / C2 0.656；spread `{3,1.6,0.7}` → C0 0.353 / C1 0.403 / C2 1.025。大尺度不变，中/小尺度显著变宽，C2 接近各向同性。截图 A/B 在 600px 游戏视图下肉眼差异不明显，判断改动应看该数值指标而不是缩略图。

## 注意

- Unity 编辑器未运行时 `unityctl script execute` 会失败；`unityctl editor launch` 在本机找不到 Hub 安装路径，需要 `--unity-path /Applications/Unity/Unity-6000.3.14f1/Unity.app`，且该命令实际可能只拉起 Hub；直接 `Unity -projectPath` 更可靠，随后 `unityctl bridge start`。
- 若编译失败，Editor 进入 Safe Mode；修好后聚焦 Unity 会自动重新编译并连接 bridge。
- `Validation~` 目录带 `~` 不参与 AssetDatabase 编译，只能通过 `unityctl script execute` 运行。
