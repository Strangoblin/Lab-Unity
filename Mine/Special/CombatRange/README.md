# CombatRange：综合靶场

更新：2026-10-04（Asia/Hong_Kong），Unity `2022.3.57f1c2`，3D Physics。

打开 [Assets/Scenes/CombatRange.unity](../../../Scenes/CombatRange.unity)，进入 Play。综合靶场把玩家射击、受击、移动和两套配件自定义放在同一场景，默认“精确单发”发射器及“均衡防护”装备，连续受击默认关闭。

左侧“发射器 / 装备”页签分别显示八个发射器槽和生命、护盾、体力、能量四个装备槽；点击槽位或配方选择装配。右侧滚动面板显示四条资源、实际射击费用、三靶命中/伤害/状态、受击减免及两边弹丸记录。

| 操作 | 效果 |
| --- | --- |
| `WASD` / `Left Shift` | 移动 / 冲刺，消耗体力 |
| 方向键 / 靶场右键 | 调整玩家瞄准 |
| `Space` / 靶场左键 / 玩家射击按钮 | 玩家开火，消耗人物能量 |
| `H` / 敌方测试弹按钮 | 敌炮向玩家发射一次 |
| `T` | 开关连续受击 |
| `R` | 补满玩家资源，重置位置、状态、靶标、两边弹丸及射击冷却 |
| `Esc` | 关闭配件或配方列表 |

先点击 Game View 获得键盘焦点。展开选择列表后暂停角色操作，搜索不会误触射击和重置。换装不补满资源、不清除状态与冷却，只有显式测试重置清除这些运行状态。

玩家发射器通过 [EquipmentEnergySource](Runtime/EquipmentEnergySource.cs) 使用 `EquipmentState.Energy`。发射器计算基础费用，装备应用人物消耗倍率；射击成功后提交费用，初始发射容量失败时回滚。未绑定外部来源的敌炮保留独立能量。玩家/敌方弹丸池分别为 512 / 128，不竞争初始发射容量。

| 目录 | 职责 |
| --- | --- |
| `Runtime` | 共享能量适配、综合玩家输入、瞄准、两向射击和状态面板 |
| `Editor` | 新场景生成与保存绑定校验 |
| `Tests` | 共享能量交易及保存场景的集成测试 |

CustomLauncher / CustomEquipment 的配件、配方、编译器与运行状态继续独立维护。场景专用材料在 `Assets/Scenes/CombatRangeResources`；旧 Range / Storage 场景和专用材料归档于 [legacy-range-storage.zip](../../../../Docs/CombatRange/Archive/legacy-range-storage.zip)，不再生成旧测试场景。

菜单 `CelToon/Combat Range` 提供 Open / Create or Rebuild / Validate Combat Range。重建前结束 Play、关闭目标场景并保存未命名场景；重建保留已有配件与配方中的手工参数。

完整说明见 [Docs/CombatRange/README.md](../../../../Docs/CombatRange/README.md)，测试结果与截图见 [VALIDATION.md](../../../../Docs/CombatRange/VALIDATION.md)。
