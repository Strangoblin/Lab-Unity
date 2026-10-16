# ProductionTools — 生产类工具统一框架

> Generator / Baker 一类「烘焙或生成资产」的编辑器工具的共享骨架。
> 统一**固定资产路径**与 **Bake / Load / Save** 基础行为；生成逻辑仍留各自服务层，窗口只做壳。

---

## 架构

```
ProductionToolWindow (抽象 EditorWindow)     ← 统一骨架
  ├── AssetPath          ← 固定资产路径（Save = Load 单一来源）
  ├── BakeCore()         ← 子类唯一必须实现的烘焙入口
  └── Bake / Load / Save ← 统一行动行与资产所有权
        ├── AssetFileUtility   ← 落盘 / 回看 / 目录补齐
        └── ProductionToolPaths ← 各工具固定路径注册

FGDLutBakerWindow / SSSLutBakerWindow / NoiseGeneratorWindow
CurveGeneratorWindow / InteriorMapBakerWindow      ← 消费者
  └── 服务层：FGDLutBaker / SSSLutBaker / NoiseGenerator / CurveBake / InteriorMapTextureBaker
```

**关键设计**：与 `InteractionManager` 同构 —— 框架只管共享输入与流程（路径、行动、所有权），
各工具自管其设置分区与预览；框架不承载任何生成算法。

---

## 文件夹

| 路径 | 内容 |
|------|------|
| `Editor/ProductionToolWindow.cs` | 抽象窗口骨架：路径行、行动行、落盘 / 回看、资产所有权 |
| `Editor/AssetFileUtility.cs` | 资产 IO：路径校验、原位更新保存、回看、目录补齐 |
| `Editor/ProductionToolPaths.cs` | 固定资产路径注册表 |

---

## 统一契约

子类必须实现：

| 成员 | 说明 |
|------|------|
| `AssetPath` | 固定资产路径，Save 落盘与 Load 回看共用同一来源；以 `[SerializeField]` 字段承载，默认值取自 `ProductionToolPaths` |
| `BakeLabel` | 行动行首个按钮文案：Baker 型 `"Bake"`，Generator 型 `"Generate"` |
| `BakeCore()` | 调服务层烘焙 / 生成，返回待保存资产；失败返回 `null` |

子类按需覆写：`CanBake`（框架未就绪时禁用）、`ShowBakeProgress`（Generator 型关掉）、
`HasSaveableAsset`（多输出模式按当前模式判断）、`OnAssetChanged()`（刷新自己的预览）。

**所有权铁律**：`BakeCore()` 返回的资产所有权移交框架 —— 窗口关闭或换源时销毁；
经 `AssetDatabase` 载入或落盘的资产归资产系统，永不销毁。
子类覆写 `OnDisable` 必须 `protected override void OnDisable()` 并调用 `base.OnDisable()`。

---

## 固定资产路径

| 工具 | 路径 |
|------|------|
| FGD LUT | `Assets/Textures/FGD_LUT.asset` |
| SSS LUT | `Assets/Textures/SSS_LUT.asset` |
| Noise 2D | `Assets/Textures/Noise2D.asset` |
| Noise 3D | `Assets/Textures/Noise3D.asset` |
| Interior Map | `Assets/Textures/InteriorMap.asset` |
| Curve | `Assets/Mine/Data/Curves/NewCurve.asset` |

烘焙纹理统一在 `Assets/Textures/`（既有资产的实际位置），曲线数据在 `Assets/Mine/Data/Curves/`。
新工具在 `ProductionToolPaths` 登记，窗口内不再出现路径字面量。

---

Noise 的载入 / 已保存 2D 资产不会被参数预览替换；重新 Generate 才应用新参数。3D 切片读取当前资产实际像素和尺寸；没有资产时保留参数预览，不可读资产显示提示。

## 接入新工具

1. 生成 / 烘焙逻辑写成本目录外的服务层静态类（参考 `FGDLutBaker`、`InteriorMapTextureBaker`）。
2. 在 `ProductionToolPaths` 登记固定路径。
3. 窗口 `sealed class XxxWindow : ProductionToolWindow`，实现三个契约成员。
4. GUI 脊柱：标题 → 设置分区 → `DrawAssetPathRow()` → `DrawActionRow()` → 预览分区。

---

## 与改造前的行为差异

| 差异 | 说明 |
|------|------|
| 保存保留 GUID | 路径上已有同类型资产时原位更新（`CopySerialized`），不再 `DeleteAsset` + `CreateAsset`；材质与场景引用不失效 |
| 只写目标资产 | 改用 `AssetDatabase.SaveAssetIfDirty`，不再调用全局 `SaveAssets`，避免顺带写回会话内存态 |
| 统一覆盖确认 | 覆盖已存在的**不同**资产前一律确认；保存自身（Load 后直接 Save）不打扰 |
| 统一进度条 | Baker 型显示，Generator 型不显示 |
| Curve / Noise 新增 Load | 两族原无回看入口，现由框架统一提供 |
| Noise 双模式单一当前资产 | Generate / Load 或 2D 自动打包换源时释放上一份未落盘结果；只切模式不保证立即释放，已落盘资产始终保留 |

## 验证

可复跑入口、隔离资产的生命周期和覆盖边界见 [Validation~/README.md](Validation~/README.md)。
