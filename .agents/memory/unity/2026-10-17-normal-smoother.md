# 2026-10-17 NormalSmoother 搬运（CelToon → Lab）

## 范围

- 源：`C:\Programs\Unity\Unity Project\CelToon\Assets\Mine\Scripts\NormalSmoother.cs`
- 目标：`Assets/Mine/Scripts/NormalSmoother/NormalSmoother.cs`、`NormalSmoother.md`
- 未搬运：CelToon `Assets/Editor/NormalSmootherWindow.cs`（本次请求只点名 `.cs`）

## 实现

- 命名空间 `Mine.NormalSmoother`；静态工具类，公开 API 与设置字段名保持源项目一致（`angleThreshold` / `positionEpsilon` / `storageMode`），便于对照与后续窗口移植。
- 按项目脚本规范重排：读取源数据 → 构建拐角 → 位置分组 → 法线聚类 → 输出重建；魔法数字提为常量；补 XML 注释与 `// ═══` 区块分隔。
- 行为保持：面法线、拐角面积权重、180° 全连通、顶点色编码 `n * 0.5 + 0.5`、UV0–7、子网格、骨骼权重、输出顶点超过 65535 时切 `UInt32`。
- 移植中自查修正：补读 `bindposes` / `indexFormat`（漏读会导致蒙皮绑定丢失、索引格式回退 UInt16）。

## 验证（已验证）

- `unityctl asset refresh`：编译成功，日志无 error CS / 异常。
- 等价性：原文件原样改名 `ReferenceNormalSmoother` 作为参考，与移植版在内置 Cube、Sphere、双 submesh Cube 上跑 7 组参数（角度 0/60/180/45/400/90，两种存储模式）共 21 例，逐项比对顶点、法线、切线、颜色、UV0–7、三角形索引、子网格数、索引格式与网格名：全部一致。
- 额外：骨骼权重与 bindposes 复制一致；60,025 顶点起伏网格输出 321,953 顶点，索引格式 UInt32 与参考一致。
- `check_norm.py` 对 `.cs` / `.md` 均通过。

## 未验证 / 注意

- 无画面级验收（项目内暂无消费者）；CelToon 的 Editor 窗口未搬运。
- 原算法按“拐角面积”加权，同一面两个三角形共享的顶点会被计两次，平直四边形面在 180° 下会产生非对称法线。这是原实现行为，不是移植偏差（内置 Cube 已复现）。
- 返回网格归调用方；`VertexColors` 模式覆盖源顶点色；源网格须开启 Read/Write。